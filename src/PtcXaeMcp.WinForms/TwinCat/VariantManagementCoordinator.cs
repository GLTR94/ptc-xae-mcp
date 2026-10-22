using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed class VariantManagementCoordinator(ITwinCatSession session)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string GetVariantStatus()
    {
        string? err = SessionGuards.RequireOpenSolution(session);
        if (err is not null) return err;
        try
        {
            var result = session.GetVariantStatus();
            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }

    /// <summary>
    /// Issue #148 (LIVE-10 / B-11): validate against the project's variant list FIRST, set,
    /// then read <c>CurrentProjectVariant</c> back. A failed or unconfirmed switch restores the
    /// previous variant, so the call never leaves the project with an empty/foreign variant,
    /// and every error carries exactly one "[Error]" prefix.
    /// </summary>
    public string SetActiveVariant(string variantName, bool rebuildAfter)
    {
        if (string.IsNullOrWhiteSpace(variantName))
            return "[Error] variantName is required.";
        string requested = variantName.Trim();

        string? err = SessionGuards.RequireOpenSolution(session);
        if (err is not null) return err;

        VariantStatusResult before;
        try { before = session.GetVariantStatus(); }
        catch (Exception ex) { return McpErrors.EnsureError(ex.Message); }

        var (canonical, validationError) = ResolveVariantName(requested, before);
        if (validationError is not null) return validationError;

        string setMsg;
        try { setMsg = session.SetActiveVariant(canonical!); }
        catch (Exception ex)
        {
            return McpErrors.EnsureError(
                $"Could not activate variant '{canonical}': {ex.Message}{Restore(before.ActiveVariant)}");
        }

        string? after;
        try { after = session.GetVariantStatus().ActiveVariant; }
        catch (Exception ex)
        {
            return McpErrors.EnsureError(
                $"Variant '{canonical}' was set but CurrentProjectVariant could not be read back: " +
                $"{ex.Message}{Restore(before.ActiveVariant)}");
        }
        if (!string.Equals(after, canonical, StringComparison.OrdinalIgnoreCase))
            return McpErrors.EnsureError(
                $"Variant switch to '{canonical}' did not take effect: CurrentProjectVariant reads " +
                $"'{after}'.{Restore(before.ActiveVariant)}");

        if (!rebuildAfter)
            return setMsg + " Rebuild required for changes to take effect.";
        try
        {
            var build = new BuildCoordinator(session).BuildSolutionDetailed();
            return $"{setMsg}\n\n{build.Summary}";
        }
        catch (Exception ex) { return McpErrors.EnsureError(ex.Message); }
    }

    /// <summary>
    /// Maps the requested name to the project's spelling. "[Group]" addresses a variant group.
    /// Returns an "[Error] Unknown variant ... Expected ..." for anything not in the list.
    /// </summary>
    internal static (string? Canonical, string? Error) ResolveVariantName(string requested, VariantStatusResult status)
    {
        bool isGroup = requested.Length > 2 && requested[0] == '[' && requested[^1] == ']';
        if (isGroup)
        {
            string groupName = requested[1..^1].Trim();
            var group = status.Groups.FirstOrDefault(g =>
                string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
            if (group is not null) return ($"[{group.Name}]", null);
        }
        else
        {
            var match = status.AvailableVariants.FirstOrDefault(v =>
                string.Equals(v, requested, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return (match, null);
        }

        var expected = status.AvailableVariants
            .Concat(status.Groups.Select(g => $"[{g.Name}]"))
            .ToList();
        string list = expected.Count == 0
            ? "none — this project defines no variants"
            : string.Join(", ", expected);
        return (null, $"[Error] Unknown variant '{requested}'. Expected one of: {list}. " +
                      "The active variant was not changed.");
    }

    // Best effort: put the previous variant back and say what happened.
    private string Restore(string previous)
    {
        try
        {
            string now = session.GetVariantStatus().ActiveVariant;
            if (string.Equals(now, previous, StringComparison.OrdinalIgnoreCase))
                return $" The active variant is unchanged ('{previous}').";
            if (string.IsNullOrEmpty(previous))
                return $" The active variant now reads '{now}'; there was no previous variant to restore.";
            session.SetActiveVariant(previous);
            string restored = session.GetVariantStatus().ActiveVariant;
            return string.Equals(restored, previous, StringComparison.OrdinalIgnoreCase)
                ? $" Restored the previous variant '{previous}'."
                : $" Restoring the previous variant '{previous}' FAILED — it now reads '{restored}'.";
        }
        catch (Exception ex)
        {
            return $" Restoring the previous variant '{previous}' FAILED: {ex.Message}";
        }
    }
}
