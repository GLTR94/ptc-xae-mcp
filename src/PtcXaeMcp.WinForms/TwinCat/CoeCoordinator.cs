using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed class CoeCoordinator(ITwinCatSession session)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public (string Summary, string Json) ReadDictionary(string treePath, string? indexFilter)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            return ("[Error] treePath is required.", "{}");
        if (treePath.Equals("TIID", StringComparison.OrdinalIgnoreCase))
            return ("[Error] treePath must be a slave node under TIID (not the root).", "{}");

        string? error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, "{}");

        CoeDictionary dict;
        try { dict = session.ReadCoeDictionary(treePath, indexFilter); }
        catch (InvalidOperationException ex) { return ($"[Error] {ex.Message}", "{}"); }

        string json = JsonSerializer.Serialize(dict, JsonOpts);
        string filter = string.IsNullOrWhiteSpace(indexFilter) ? "" : $", filter={indexFilter}";
        string fallback = dict.FallbackNote is not null ? $" [{dict.FallbackNote}]" : "";
        string summary =
            $"{dict.SlaveType}: {dict.ObjectCount} CoE object(s) in offline dictionary{filter}.{fallback}";
        return (summary, json);
    }

    public (string Summary, string Json) ReadStartup(string treePath)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            return ("[Error] treePath is required.", "{}");
        if (treePath.Equals("TIID", StringComparison.OrdinalIgnoreCase))
            return ("[Error] treePath must be a slave node under TIID (not the root).", "{}");

        string? error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, "{}");

        CoeStartupList startup;
        try { startup = session.ReadCoeStartup(treePath); }
        catch (InvalidOperationException ex) { return ($"[Error] {ex.Message}", "{}"); }

        string json = JsonSerializer.Serialize(startup, JsonOpts);
        string entries = startup.EntryCount == 1 ? "1 Startup override" : $"{startup.EntryCount} Startup overrides";
        string summary = $"{startup.SlaveType}: {entries} (SDO-download entries set in project).";
        return (summary, json);
    }
}
