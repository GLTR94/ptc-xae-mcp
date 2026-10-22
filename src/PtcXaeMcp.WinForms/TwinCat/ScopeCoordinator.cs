using System.Text;

namespace PtcXaeMcp.WinForms.TwinCat;

// Orchestrates scope-measurement tools (twincat_create_scope_project /
// twincat_add_scope_channel / twincat_set_scope_axis_mode /
// twincat_set_scope_labels / twincat_list_scope_channels /
// twincat_get_scope_status / twincat_export_scope_data).
//
// Pure delegation + input validation — the COM session does all the work.
// Every public method begins with SessionGuards.RequireOpenSolution and wraps
// the session call in a try/catch that turns InvalidOperationException into
// "[Error] ..." strings, matching the convention used by PlcCodeCoordinator.
internal sealed class ScopeCoordinator(ITwinCatSession session)
{
    // ---- Configure -------------------------------------------------------

    public string CreateScopeProject(string? projectName)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.CreateScopeProject(projectName);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    public string RemoveScopeProject(string? projectName)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.RemoveScopeProject(projectName);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    public string AddScopeChannel(
        string  symbol,
        string? chartName,
        string? axisName,
        string? channelName,
        bool    stackedAxis,
        string? symbolName    = null,
        int?    targetPort    = null,
        string? sampleMode    = null,
        int?    sampleTimeMs  = null,
        string? symbolComment = null,
        string? projectName   = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return "[Error] symbol is required (e.g. 'TIPC^PLC^PLC Projekt^MAIN.rActualValue').";

        if (targetPort is < 1 or > 65535)
            return $"[Error] targetPort {targetPort} out of range (1..65535; default 851 = TC3 PLC 1 Runtime).";
        if (sampleTimeMs is <= 0)
            return $"[Error] sampleTimeMs must be > 0 (got {sampleTimeMs}).";
        if (sampleMode is not null && !ScopeSampleMode.IsKnown(sampleMode))
            return $"[Error] Unknown sampleMode '{sampleMode}'. Expected one of: {ScopeSampleMode.Known}.";
        if (TaskSampletimeConflict(sampleMode, sampleTimeMs) is { } conflict) return conflict;

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.AddScopeChannel(symbol, chartName, axisName, channelName, stackedAxis,
                symbolName, targetPort, sampleMode, sampleTimeMs, symbolComment, projectName);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    // In "Task Sampletime" mode TE130x takes the sample time from the PLC task and ignores a written
    // Sample Time (live 2026-10-06: wrote 50, read back the previous value). Asking for both is a
    // contradiction — refuse it before touching the session instead of failing on read-back.
    private static string? TaskSampletimeConflict(string? sampleMode, int? sampleTimeMs) =>
        sampleTimeMs is not null && sampleMode is not null && ScopeSampleMode.IsTaskSampletime(sampleMode)
            ? "[Error] sampleTimeMs cannot be combined with sampleMode 'Task Sampletime': the sample time then " +
              "comes from the PLC task and a written value is ignored. Use 'Fixed Sampletime' (or 'On Change') " +
              "with sampleTimeMs, or omit sampleTimeMs."
            : null;

    // ---- scope-acquisition-symbols ---------------------------------------

    public (string Summary, ScopeAcquisitionInfo? Info) ReadScopeAcquisition(string acqSelector, string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(acqSelector))
            return ("[Error] acqSelector is required (acquisition name or 'DataPool^<name>').", null);

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, null);

        try
        {
            var info = session.ReadScopeAcquisition(acqSelector.Trim(), projectName);
            string warn = info.HasSymbol ? "" : "  [NO SYMBOL — would record nothing]";
            return ($"Acquisition '{info.Name}' (symbol: {info.SymbolName ?? "<none>"}){warn}", info);
        }
        catch (Exception ex)
        {
            return ($"[Error] {ex.Message}", null);
        }
    }

    public string SetScopeAcquisitionSymbol(
        string  acqSelector,
        string? symbolName,
        int?    targetPort,
        string? targetSystem,
        string? sampleMode,
        int?    sampleTimeMs,
        double? timeOffsetS,
        string? symbolComment,
        bool?   enabled,
        string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(acqSelector))
            return "[Error] acqSelector is required (acquisition name or 'DataPool^<name>').";

        if (symbolName is null && targetPort is null && targetSystem is null && sampleMode is null &&
            sampleTimeMs is null && timeOffsetS is null && symbolComment is null && enabled is null)
            return "[Error] At least one field (symbolName, targetPort, targetSystem, sampleMode, sampleTimeMs, timeOffsetS, symbolComment, enabled) must be supplied.";

        if (targetPort is < 1 or > 65535)
            return $"[Error] targetPort {targetPort} out of range (1..65535).";
        if (sampleTimeMs is <= 0)
            return $"[Error] sampleTimeMs must be > 0 (got {sampleTimeMs}).";
        if (sampleMode is not null && !ScopeSampleMode.IsKnown(sampleMode))
            return $"[Error] Unknown sampleMode '{sampleMode}'. Expected one of: {ScopeSampleMode.Known}.";
        if (TaskSampletimeConflict(sampleMode, sampleTimeMs) is { } conflict) return conflict;

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.SetScopeAcquisitionSymbol(acqSelector.Trim(), symbolName, targetPort,
                targetSystem, sampleMode, sampleTimeMs, timeOffsetS, symbolComment, enabled, projectName);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    public string SetScopeAxisMode(string chartName, string mode, string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(chartName))
            return "[Error] chartName is required.";

        string normMode = mode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normMode is not (ScopeAxisMode.Overlaid or ScopeAxisMode.Stacked))
            return $"[Error] Unknown mode '{mode}'. Expected '{ScopeAxisMode.Overlaid}' or '{ScopeAxisMode.Stacked}'.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.SetScopeAxisMode(chartName, normMode, projectName);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    public string SetScopeLabels(
        string  nodeSelector,
        string? name,
        string? chartTitle,
        string? axisTitle,
        string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(nodeSelector))
            return "[Error] nodeSelector is required (chart or axis-group name).";

        if (name is null && chartTitle is null && axisTitle is null)
            return "[Error] At least one of name, chartTitle, or axisTitle must be supplied.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.SetScopeLabels(nodeSelector, name, chartTitle, axisTitle, projectName);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    // ---- Query / list ----------------------------------------------------

    public (string Summary, IReadOnlyList<ScopeNodeEntry> Entries) ListScopeChannels(string? projectName = null)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, Array.Empty<ScopeNodeEntry>());

        IReadOnlyList<ScopeNodeEntry> entries;
        try { entries = session.ListScopeChannels(projectName); }
        catch (Exception ex)
        {
            return ($"[Error] {ex.Message}", Array.Empty<ScopeNodeEntry>());
        }

        if (entries.Count == 0)
            return ("No scope project found in the open solution.", entries);

        int charts   = entries.Count(e => e.Kind == ScopeNodeKind.Chart);
        int axes     = entries.Count(e => e.Kind == ScopeNodeKind.AxisGroup);
        int channels = entries.Count(e => e.Kind == ScopeNodeKind.Channel);
        return ($"Found {channels} channel{(channels == 1 ? "" : "s")} in {charts} chart{(charts == 1 ? "" : "s")}, {axes} axis group{(axes == 1 ? "" : "s")}.",
                entries);
    }

    public (string Summary, ScopeStatus? Status) GetScopeStatus(string? projectName = null)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, null);

        try
        {
            var status = session.GetScopeStatus(projectName);
            if (!status.ScopeProjectExists)
                return ("No TwinCAT Measurement (scope) project found in the open solution.", status);

            string unboundNote = status.UnboundAcquisitionCount > 0
                ? $" ({status.UnboundAcquisitionCount} unbound)"
                : string.Empty;
            return ($"Scope project '{status.ProjectName}': {status.ChartCount} chart(s), {status.ChannelCount} channel(s), {status.AcquisitionCount} acquisition(s){unboundNote}.",
                    status);
        }
        catch (Exception ex)
        {
            return ($"[Error] {ex.Message}", null);
        }
    }

    // ---- Export data -----------------------------------------------------

    public string ExportScopeData(string format, string? projectName = null)
    {
        string normFormat = format?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normFormat is not ("CSV" or "BINARY" or "TDMS" or "DAT" or "SVD"))
            return $"[Error] Unknown format '{format}'. Expected CSV, BINARY, TDMS, DAT, or SVD.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.ExportScopeData(normFormat, projectName);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    // ---- Record lifecycle ------------------------------------------------

    public string StartScopeRecord(string? projectName = null)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try { return session.StartScopeRecord(projectName); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }

    public string StopScopeRecord(string? projectName = null)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try { return session.StopScopeRecord(projectName); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }

    public (string Summary, ScopeRecordState? State) GetScopeRecordState(string? projectName = null)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, null);

        try
        {
            var s = session.GetScopeRecordState(projectName);
            string summary = s.State switch
            {
                ScopeViewState.Record => "Recording in progress.",
                ScopeViewState.Reply  => "Recording stopped; data available.",
                _                     => "Scope is in configuration state (not recording).",
            };
            return (summary, s);
        }
        catch (Exception ex) { return ($"[Error] {ex.Message}", null); }
    }

    public string SaveScopeData(string? path = null, string? projectName = null)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try { return session.SaveScopeData(path, projectName); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }

    public string SetScopeRecordSettings(
        double? recordTimeS  = null,
        bool?   ringbuffer   = null,
        string? fileStore    = null,
        string? recordMode   = null,
        string? autoSaveMode = null,
        string? autoSavePath = null,
        string? filenameMask = null,
        string? syncMode     = null,
        string? projectName  = null)
    {
        if (recordTimeS is not null and <= 0)
            return $"[Error] recordTimeS must be > 0 (got {recordTimeS}).";
        if (recordMode is not null && !ScopeRecordMode.IsKnown(recordMode))
            return $"[Error] Unknown recordMode '{recordMode}'. Expected: {ScopeRecordMode.Known}.";
        if (autoSaveMode is not null && !ScopeAutoSaveMode.IsKnown(autoSaveMode))
            return $"[Error] Unknown autoSaveMode '{autoSaveMode}'. Expected: {ScopeAutoSaveMode.Known}.";
        if (syncMode is not null && !ScopeSyncMode.IsKnown(syncMode))
            return $"[Error] Unknown syncMode '{syncMode}'. Expected: {ScopeSyncMode.Known}.";

        if (recordTimeS is null && ringbuffer is null && fileStore is null && recordMode is null &&
            autoSaveMode is null && autoSavePath is null && filenameMask is null && syncMode is null)
            return "[Error] At least one field (recordTimeS, ringbuffer, fileStore, recordMode, " +
                   "autoSaveMode, autoSavePath, filenameMask, syncMode) must be supplied.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            return session.SetScopeRecordSettings(recordTimeS, ringbuffer, fileStore, recordMode,
                autoSaveMode, autoSavePath, filenameMask, syncMode, projectName);
        }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }

    // ---- Trigger configuration -------------------------------------------

    public string CreateScopeTriggerGroup(string action, string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(action))
            return "[Error] action is required (e.g. 'StopRecord', 'StopDisplay').";
        if (!ScopeTriggerAction.IsKnown(action))
            return $"[Error] Unknown trigger action '{action}'. Expected one of: {ScopeTriggerAction.Known}.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try { return session.CreateScopeTriggerGroup(action.Trim(), projectName); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }

    public string AddScopeTriggerSet(
        string  groupName,
        string  setType,
        string? symbol    = null,
        double? threshold = null,
        string? edge      = null,
        string? compare   = null,
        string? projectName = null)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            return "[Error] groupName is required (name of an existing trigger group).";
        if (string.IsNullOrWhiteSpace(setType))
            return "[Error] setType is required (ChannelSet or DirectorySet).";
        if (!ScopeTriggerSetType.IsKnown(setType))
            return $"[Error] Unknown setType '{setType}'. Expected one of: {ScopeTriggerSetType.Known}.";
        var (normEdge, edgeError) = ScopeTriggerCondition.NormalizeEdge(edge);
        if (edgeError is not null) return edgeError;
        var (normCompare, compareError) = ScopeTriggerCondition.NormalizeCompare(compare);
        if (compareError is not null) return compareError;

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try { return session.AddScopeTriggerSet(groupName.Trim(), setType.Trim(), symbol, threshold, normEdge, normCompare, projectName); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }

    public (string Summary, IReadOnlyList<ScopeTriggerGroupEntry> Groups) ListScopeTriggers(string? projectName = null)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, Array.Empty<ScopeTriggerGroupEntry>());

        try
        {
            var groups = session.ListScopeTriggers(projectName);
            if (groups.Count == 0)
                return ("No trigger groups found in the scope project.", groups);
            int sets = groups.Sum(g => g.Sets.Count);
            return ($"Found {groups.Count} trigger group(s) with {sets} trigger set(s) total.", groups);
        }
        catch (Exception ex) { return ($"[Error] {ex.Message}", Array.Empty<ScopeTriggerGroupEntry>()); }
    }
}
