namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Validation + error mapping for the production safety tools (issue #153, D-8). Every write is
/// validated here BEFORE the session is touched (signal-name convention, IEC identifier = safe file
/// name, supported sub type), and every exception becomes an <c>[Error] …</c> result — a failed or
/// rolled-back write never reads like success.
/// </summary>
internal sealed class SafetyCoordinator(ITwinCatSession session)
{
    private const string NoProjectPrefix = "No TwinCAT Safety project";

    public string List() => Read(session.SafetyList);

    public string ReadIoMap() => Read(session.SafetyReadIoMap);

    public string ReadProgram() => Read(session.SafetyReadProgram);

    public string AddAliasDevice(string? signalName, int subType, string? group)
    {
        if (SafetyAliasRules.Validate(signalName, subType) is { } invalid)
            return invalid;
        if (group is not null && string.IsNullOrWhiteSpace(group))
            return "[Error] group must not be blank (omit it when the project has a single group).";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            string result = session.SafetyAddAliasDevice(signalName!, subType, group?.Trim());
            return result.StartsWith(NoProjectPrefix, StringComparison.Ordinal)
                ? McpErrors.EnsureError(result)
                : result;
        }
        catch (Exception ex)
        {
            return McpErrors.EnsureError(ComCall.IsBusy(ex) ? ComCall.Describe(ex) : ex.Message);
        }
    }

    private string Read(Func<string> read)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        try { return read(); }
        catch (Exception ex)
        {
            return McpErrors.EnsureError(ComCall.IsBusy(ex) ? ComCall.Describe(ex) : ex.Message);
        }
    }
}
