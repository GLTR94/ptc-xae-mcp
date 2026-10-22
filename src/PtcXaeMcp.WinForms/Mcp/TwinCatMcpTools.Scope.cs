using PtcXaeMcp.WinForms.Security;
using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Globalization;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    // ---- twincat_create_scope_project ---------------------------------------

    [McpServerTool(Name = "twincat_create_scope_project", ReadOnly = false, Destructive = false)]
    [Description("Add a TwinCAT Scope (measurement) project to the open solution using the Scope View 'YT Scope Project' template. Returns the new project's path. projectName defaults to 'MeasurementProject'. Requires TE130X Scope View to be installed.")]
    public string CreateScopeProject(
        [Description("Name for the new measurement project. Defaults to 'MeasurementProject' when omitted.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_scope_project", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session).CreateScopeProject(projectName)), cancellationToken);

    // ---- twincat_remove_scope_project ---------------------------------------

    [McpServerTool(Name = "twincat_remove_scope_project", ReadOnly = false, Destructive = true)]
    [Description("Unregister a TwinCAT Scope (measurement) project from the open solution — the counterpart to twincat_create_scope_project. Removes the project node only; does NOT save the solution and does NOT delete files from disk. projectName must match an existing measurement project; required when the solution has more than one (fails loud otherwise so a customer-shipped scope project is never removed by accident). Idempotent: returns a benign summary if no matching project is present. Use this before closing/deleting a created scope project so a blocked close cannot leave a dangling node (E_ABORT 'Missing Project Nodes' on reopen).")]
    public string RemoveScopeProject(
        [Description("Name of the measurement project to remove. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_remove_scope_project", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session).RemoveScopeProject(projectName)), cancellationToken);

    // ---- twincat_add_scope_channel -----------------------------------------

    [McpServerTool(Name = "twincat_add_scope_channel", ReadOnly = false, Destructive = false)]
    [Description("Add a chart → axis-group → channel to the scope project and bind it to a PLC symbol (e.g. 'TIPC^PLC^PLC Projekt^MAIN.rActualValue'). Reuses an existing chart/axis by name if they already exist. chartName, axisName, channelName default to the symbol path. Set stackedAxis=true to place the channel on its own Y-axis. An existing DataPool acquisition with the same Symbol Name and port is reused (not duplicated); otherwise one is created. The acquisition is pointed at the actual PLC symbol so it records data, every field is read back and a field that did not land returns [Error]: symbolName overrides the PLC-side symbol name derived from symbol; targetPort defaults to 851 (TC3 PLC 1 Runtime); sampleMode (e.g. 'Task Sampletime') and sampleTimeMs tune sampling; symbolComment is optional. projectName selects which measurement project to use when the solution has more than one (omit if only one; an ambiguous omission returns a structured [Error] listing the projects). Returns the channel's tree path.")]
    public string AddScopeChannel(
        [Description("PLC symbol path to bind the channel to, e.g. 'TIPC^PLC^PLC Projekt^MAIN.rActualValue'.")] string  symbol,
        [Description("Name of the chart to add the channel to; reused if it already exists. Defaults to the symbol path.")] string? chartName     = null,
        [Description("Name of the axis-group within the chart; reused if it already exists. Defaults to the symbol path.")] string? axisName      = null,
        [Description("Name of the created channel. Defaults to the symbol path.")] string? channelName   = null,
        [Description("True to place the channel on its own Y-axis instead of sharing the axis-group's axis. Default false.")] bool    stackedAxis   = false,
        [Description("PLC-side symbol name for the DataPool acquisition, if it must differ from the name derived from symbol.")] string? symbolName    = null,
        [Description("ADS target port of the PLC runtime to sample from. Defaults to 851 (TC3 PLC 1 Runtime).")] int?    targetPort    = null,
        [Description("Sample mode for the acquisition, e.g. 'Task Sampletime'. Uses the scope's default when omitted.")] string? sampleMode    = null,
        [Description("Sample time in milliseconds. Uses the scope's default when omitted.")] int?    sampleTimeMs  = null,
        [Description("Optional free-text comment stored on the acquisition's symbol binding.")] string? symbolComment = null,
        [Description("Name of the measurement project to use. Required when the solution has more than one; omit when there is exactly one.")] string? projectName   = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_add_scope_channel",
            new { symbol, chartName, axisName, channelName, stackedAxis, symbolName, targetPort, sampleMode, sampleTimeMs, symbolComment, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session)
                    .AddScopeChannel(symbol, chartName, axisName, channelName, stackedAxis,
                        symbolName, targetPort, sampleMode, sampleTimeMs, symbolComment, projectName)), cancellationToken);

    // ---- twincat_read_scope_acquisition ------------------------------------

    [McpServerTool(Name = "twincat_read_scope_acquisition", ReadOnly = true)]
    [Description("Read the full symbol/ADS structure of one DataPool acquisition in the scope project. acqSelector is the acquisition name or 'DataPool^<name>'. Returns every ProjectItem.Properties entry (raw key=value), plus the resolved Symbol Name, Data Type, Sample Mode, Sample Time, and Target Port, and flags acquisitions with no symbol bound (would record nothing). projectName selects the measurement project when the solution has more than one. Read-only.")]
    public string ReadScopeAcquisition(
        [Description("Acquisition name, or 'DataPool^<name>' tree path.")] string acqSelector,
        [Description("Name of the measurement project to read from. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_scope_acquisition", new { acqSelector, projectName }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, info) = new ScopeCoordinator(bridge.Dispatcher.Session)
                    .ReadScopeAcquisition(acqSelector, projectName);
                if (McpErrors.IsError(summary) || info is null)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Path        : {info.Path}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Symbol Name : {info.SymbolName ?? "<none>"}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Data Type   : {info.DataType ?? "<unknown>"}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Sample Mode : {info.SampleMode ?? "<unknown>"}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Sample Time : {info.SampleTimeMs ?? "<unknown>"} ms");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Target Port : {info.TargetPort ?? "<unknown>"}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Target Sys  : {info.TargetSystem ?? "<unknown>"}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  --- all properties ({info.AllProperties.Count}) ---");
                foreach (var p in info.AllProperties)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {p.Key} = {p.Value}");
                return sb.ToString().TrimEnd();
            }), cancellationToken);

    // ---- twincat_set_scope_acquisition_symbol ------------------------------

    [McpServerTool(Name = "twincat_set_scope_acquisition_symbol", ReadOnly = false, Destructive = true)]
    [Description("Set or repair the PLC symbol and ADS/sample settings of an existing DataPool acquisition (the partial-update analogue of twincat_set_scope_labels). acqSelector is the acquisition name or 'DataPool^<name>'. Supply at least one of: symbolName (PLC-side symbol, e.g. 'GVL_HMI.HmiOpened'), targetPort (e.g. 851), targetSystem, sampleMode, sampleTimeMs, timeOffsetS, symbolComment, enabled. Only supplied fields change. Every field is read back; any field that did not land returns [Error] naming what did and did not apply. projectName selects the measurement project when the solution has more than one. Returns the read-back values.")]
    public string SetScopeAcquisitionSymbol(
        [Description("Acquisition name, or 'DataPool^<name>' tree path, of the acquisition to update.")] string  acqSelector,
        [Description("PLC-side symbol name to bind, e.g. 'GVL_HMI.HmiOpened'. Only changed when supplied.")] string? symbolName    = null,
        [Description("ADS target port of the PLC runtime to sample from, e.g. 851. Only changed when supplied.")] int?    targetPort    = null,
        [Description("Target system (AMS NetId) to sample from. Only changed when supplied.")] string? targetSystem  = null,
        [Description("Sample mode for the acquisition, e.g. 'Task Sampletime'. Only changed when supplied.")] string? sampleMode    = null,
        [Description("Sample time in milliseconds. Only changed when supplied.")] int?    sampleTimeMs  = null,
        [Description("Time offset in seconds applied to the acquisition. Only changed when supplied.")] double? timeOffsetS   = null,
        [Description("Free-text comment stored on the acquisition's symbol binding. Only changed when supplied.")] string? symbolComment = null,
        [Description("Whether the acquisition is enabled. Only changed when supplied.")] bool?   enabled       = null,
        [Description("Name of the measurement project to update. Required when the solution has more than one; omit when there is exactly one.")] string? projectName   = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_set_scope_acquisition_symbol",
            new { acqSelector, symbolName, targetPort, targetSystem, sampleMode, sampleTimeMs, timeOffsetS, symbolComment, enabled, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session)
                    .SetScopeAcquisitionSymbol(acqSelector, symbolName, targetPort, targetSystem,
                        sampleMode, sampleTimeMs, timeOffsetS, symbolComment, enabled, projectName)), cancellationToken);

    // ---- twincat_set_scope_axis_mode ----------------------------------------

    [McpServerTool(Name = "twincat_set_scope_axis_mode", ReadOnly = false, Destructive = true)]
    [Description("EXPERIMENTAL — the stacked-axis property key is not documented and not yet verified on a live TE130x. Set the Y-axis display mode of a chart in the scope project. mode is 'overlaid' (all channels share one Y-axis) or 'stacked' (each channel gets its own Y-axis). chartName is the chart's tree name. The value is read back; if no stacked-axis property exists or the read-back differs, returns [Error] listing the chart's property keys. projectName selects the measurement project when the solution has more than one.")]
    public string SetScopeAxisMode(
        [Description("Tree name of the chart to update.")] string chartName,
        [Description("Y-axis display mode: 'overlaid' (all channels share one Y-axis) or 'stacked' (each channel gets its own Y-axis).")] string mode,
        [Description("Name of the measurement project to update. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_set_scope_axis_mode", new { chartName, mode, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session).SetScopeAxisMode(chartName, mode, projectName)), cancellationToken);

    // ---- twincat_set_scope_labels ------------------------------------------

    [McpServerTool(Name = "twincat_set_scope_labels", ReadOnly = false, Destructive = true)]
    [Description("Set or fix the display labels of an existing chart or axis-group node in the scope project. nodeSelector is the node's name. Supply at least one of: name (rename the node), chartTitle (set the chart title text), axisTitle (set the Y-axis title). Flips Show Title to Custom for any supplied title. Every value is read back; if any requested label did not land, returns [Error] naming what did and did not apply. projectName selects the measurement project when the solution has more than one.")]
    public string SetScopeLabels(
        [Description("Name of the chart or axis-group node to update.")] string  nodeSelector,
        [Description("New name for the node (rename). Only changed when supplied.")] string? name       = null,
        [Description("Title text, only where the node has a title property; TE130x 4026.22 has none on a chart or axis group (a chart shows its name — use `name`), so there it returns [Error].")] string? chartTitle = null,
        [Description("Y-axis title text. Only changed when supplied; flips Show Title to Custom.")] string? axisTitle  = null,
        [Description("Name of the measurement project to update. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_set_scope_labels",
            new { nodeSelector, name, chartTitle, axisTitle, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session)
                    .SetScopeLabels(nodeSelector, name, chartTitle, axisTitle, projectName)), cancellationToken);

    // ---- twincat_list_scope_channels ----------------------------------------

    [McpServerTool(Name = "twincat_list_scope_channels", ReadOnly = true)]
    [Description("Enumerate all charts, axis-groups, channels, and acquisitions in the scope project. Returns one line per node with kind (CHART|AXISGROUP|CHANNEL|ACQUISITION), tree path, bound acquisition (for channels), and whether the display label is still at its template default. projectName selects the measurement project when the solution has more than one.")]
    public string ListScopeChannels(
        [Description("Name of the measurement project to list. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_list_scope_channels", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, entries) = new ScopeCoordinator(bridge.Dispatcher.Session).ListScopeChannels(projectName);
                if (McpErrors.IsError(summary) || entries.Count == 0)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                foreach (var e in entries)
                {
                    string bound   = string.IsNullOrEmpty(e.BoundAcquisition) ? "" : $" → {e.BoundAcquisition}";
                    string warn    = e.AtTemplateDefault ? " [DEFAULT LABEL]" : "";
                    string label   = (e.DisplayLabel is not null && e.DisplayLabel != e.Name)
                                     ? $" (label: {e.DisplayLabel})" : "";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Kind,-11} {e.Path}{bound}{label}{warn}");
                }
                return sb.ToString().TrimEnd();
            }), cancellationToken);

    // ---- twincat_get_scope_status ------------------------------------------

    [McpServerTool(Name = "twincat_get_scope_status", ReadOnly = true)]
    [Description("Report whether a TwinCAT Measurement (scope) project exists in the open solution and summarise its structure (chart/axis/channel/acquisition counts). projectName selects the measurement project when the solution has more than one (ambiguous omission returns a structured [Error] listing them). Read-only.")]
    public string GetScopeStatus(
        [Description("Name of the measurement project to report on. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_get_scope_status", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, status) = new ScopeCoordinator(bridge.Dispatcher.Session).GetScopeStatus(projectName);
                if (McpErrors.IsError(summary) || status is null)
                    return summary;

                if (!status.ScopeProjectExists)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Project     : {status.ProjectName}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Charts      : {status.ChartCount}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Axis groups : {status.AxisCount}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Channels    : {status.ChannelCount}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Acquisitions: {status.AcquisitionCount}");
                return sb.ToString().TrimEnd();
            }), cancellationToken);

    // ---- twincat_export_scope_data -----------------------------------------

    [McpServerTool(Name = "twincat_export_scope_data", ReadOnly = false, Destructive = true)]
    [Description("Export the recorded scope data to a file next to the open solution. format is one of CSV (default), BINARY, TDMS, DAT, SVD. Returns the written file path. projectName selects the measurement project when the solution has more than one. Returns [Error] if no data has been recorded yet; any other export failure is reported with its message and HRESULT.")]
    public string ExportScopeData(
        [Description("Export file format: CSV (default), BINARY, TDMS, DAT, or SVD.")] string format = "CSV",
        [Description("Name of the measurement project to export from. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_export_scope_data", new { format, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session).ExportScopeData(format, projectName)), cancellationToken);

    // ---- twincat_start_scope_record ----------------------------------------

    [McpServerTool(Name = "twincat_start_scope_record", ReadOnly = false, Destructive = true)]
    [Description("Start a TwinCAT Scope recording (IMeasurementScope.StartRecord on the scope-config root). The tool does not check the current state itself — call twincat_get_scope_record_state first. Returns [Error] if StartRecord fails or no scope project exists. projectName selects the measurement project when the solution has more than one.")]
    public string StartScopeRecord(
        [Description("Name of the measurement project to start recording on. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_start_scope_record", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session).StartScopeRecord(projectName)), cancellationToken);

    // ---- twincat_stop_scope_record -----------------------------------------

    [McpServerTool(Name = "twincat_stop_scope_record", ReadOnly = false, Destructive = true)]
    [Description("Stop the active TwinCAT Scope recording (IMeasurementScope.StopRecord). Returns [Error] if StopRecord fails. After stopping, the scope enters 'Reply' state and recorded data is available for twincat_save_scope_data or twincat_export_scope_data. projectName selects the measurement project when the solution has more than one.")]
    public string StopScopeRecord(
        [Description("Name of the measurement project to stop recording on. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_stop_scope_record", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session).StopScopeRecord(projectName)), cancellationToken);

    // ---- twincat_get_scope_record_state ------------------------------------

    [McpServerTool(Name = "twincat_get_scope_record_state", ReadOnly = true)]
    [Description("Read the current TwinCAT Scope recording state: 'Config' (not recording), 'Record' (recording in progress), or 'Reply' (recording stopped, data available). Polling-friendly — call before start/stop to guard preconditions. Returns [Error] (never 'Config') when the state cannot be read. projectName selects the measurement project when the solution has more than one. Read-only.")]
    public string GetScopeRecordState(
        [Description("Name of the measurement project to read. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_get_scope_record_state", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, state) = new ScopeCoordinator(bridge.Dispatcher.Session)
                    .GetScopeRecordState(projectName);
                if (McpErrors.IsError(summary) || state is null)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  State       : {state.State}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  IsRecording : {state.IsRecording}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  HasData     : {state.HasData}");
                return sb.ToString().TrimEnd();
            }), cancellationToken);

    // ---- twincat_save_scope_data -------------------------------------------

    [McpServerTool(Name = "twincat_save_scope_data", ReadOnly = false, Destructive = true)]
    [Description("Save the recorded TwinCAT Scope data as a native .svdx file (SaveSVD). The scope must be in 'Reply' state (recording stopped). path is the output file path; if omitted, defaults to ScopeData.svdx next to the open solution. Returns the written file path. Returns [Error] if no data has been recorded. Use twincat_export_scope_data for CSV/TDMS/DAT/BINARY formats. projectName selects the measurement project when the solution has more than one.")]
    public string SaveScopeData(
        [Description("Output .svdx file path. Defaults to ScopeData.svdx next to the open solution when omitted.")] string? path = null,
        [Description("Name of the measurement project to save from. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_save_scope_data", new { path, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                McpPathGuard.CheckOptionalFile(path, bridge.Dispatcher.Session, "path")
                ?? new ScopeCoordinator(bridge.Dispatcher.Session).SaveScopeData(path, projectName)), cancellationToken);

    // ---- twincat_set_scope_record_settings ---------------------------------

    [McpServerTool(Name = "twincat_set_scope_record_settings", ReadOnly = false, Destructive = true)]
    [Description("Set TwinCAT Scope record configuration on the scope-config root. Supply at least one of: recordTimeS (total recording duration in seconds, > 0), ringbuffer (true = Restart Record / ring mode), fileStore (file-store path), recordMode ('UserStart' or 'TriggerStart'), autoSaveMode ('None', 'SVDX', or 'Export'), autoSavePath, filenameMask, syncMode ('Soft', 'Medium', or 'Hard'). Only supplied fields are changed. EXPERIMENTAL — these property keys are matched by normalised token and are not yet verified on a live TE130x; every value is read back and any field that did not land returns [Error] listing the scope root's property keys. projectName selects the measurement project when the solution has more than one.")]
    public string SetScopeRecordSettings(
        [Description("Total recording duration in seconds. Must be > 0. Only changed when supplied.")] double? recordTimeS  = null,
        [Description("True selects ring-buffer ('Restart Record') mode. Only changed when supplied.")] bool?   ringbuffer   = null,
        [Description("File-store path for recorded data. Only changed when supplied.")] string? fileStore    = null,
        [Description("NOT writable: 'Record.Start Mode' is read-only in TE130x (it follows from the trigger configuration). Supplying it returns [Error] for this field; omit it.")] string? recordMode   = null,
        [Description("Auto-save mode: 'None', 'SVDX', or 'Export'. Only changed when supplied.")] string? autoSaveMode = null,
        [Description("Auto-save output path. Only changed when supplied.")] string? autoSavePath = null,
        [Description("Filename mask/pattern for auto-saved files. Only changed when supplied.")] string? filenameMask = null,
        [Description("Synchronisation mode: 'Soft', 'Medium', or 'Hard'. Only changed when supplied.")] string? syncMode     = null,
        [Description("Name of the measurement project to update. Required when the solution has more than one; omit when there is exactly one.")] string? projectName  = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_set_scope_record_settings",
            new { recordTimeS, ringbuffer, fileStore, recordMode, autoSaveMode, autoSavePath, filenameMask, syncMode, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session)
                    .SetScopeRecordSettings(recordTimeS, ringbuffer, fileStore, recordMode,
                        autoSaveMode, autoSavePath, filenameMask, syncMode, projectName)), cancellationToken);

    // ---- twincat_create_scope_trigger_group --------------------------------

    [McpServerTool(Name = "twincat_create_scope_trigger_group", ReadOnly = false, Destructive = false)]
    [Description("EXPERIMENTAL — the trigger-group element id and its property keys are not documented and not yet verified on a live TE130x. Create a trigger group under the scope project's existing Trigger node (never creates that node). action is the trigger event that fires when all sets in the group are satisfied: 'StopRecord', 'StopDisplay', 'RestartDisplay', 'StartSubsave', 'StopSubsave', or 'EventMarker'. The action is read back; if it did not land, the group is deleted again and [Error] lists the group's property keys. Use twincat_add_scope_trigger_set to add conditions to the group. projectName selects the measurement project when the solution has more than one.")]
    public string CreateScopeTriggerGroup(
        [Description("Trigger event fired when all sets in the group are satisfied: 'StopRecord', 'StopDisplay', 'RestartDisplay', 'StartSubsave', 'StopSubsave', or 'EventMarker'.")] string action,
        [Description("Name of the measurement project to create the group in. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_scope_trigger_group", new { action, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session)
                    .CreateScopeTriggerGroup(action, projectName)), cancellationToken);

    // ---- twincat_add_scope_trigger_set -------------------------------------

    [McpServerTool(Name = "twincat_add_scope_trigger_set", ReadOnly = false, Destructive = false)]
    [Description("EXPERIMENTAL — the condition property keys are not yet verified on a live TE130x. Add a trigger set to an existing trigger group in the TwinCAT Scope project. groupName is the name of the trigger group (from twincat_create_scope_trigger_group). setType is 'ChannelSet' or 'DirectorySet' (TriggerSetType ids 0/1 of the Scope Automation Interface). For ChannelSet: supply symbol (PLC channel symbol), threshold (numeric trigger level), edge ('Rising'|'Falling'|'Both'), compare ('Greater'|'Less'|'Equal'). Each condition is read back; any condition that did not land returns [Error] naming what did and did not apply. projectName selects the measurement project when the solution has more than one.")]
    public string AddScopeTriggerSet(
        [Description("Name of the trigger group to add the set to (from twincat_create_scope_trigger_group).")] string  groupName,
        [Description("Trigger set type: 'ChannelSet' or 'DirectorySet' (TriggerSetType 0/1). Default 'ChannelSet'.")] string  setType     = "ChannelSet",
        [Description("NOT supported: the trigger channel is a Guid reference to a DataPool acquisition that the Automation Interface does not expose. Supplying it returns [Error] for this condition; pick the channel in the Scope editor.")] string? symbol      = null,
        [Description("Numeric trigger level. Only used for ChannelSet.")] double? threshold   = null,
        [Description("Trigger edge: 'Rising', 'Falling', or 'Both'. Only used for ChannelSet.")] string? edge        = null,
        [Description("Comparison operator: 'Greater', 'Less', or 'Equal'. Only used for ChannelSet.")] string? compare     = null,
        [Description("Name of the measurement project to update. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_add_scope_trigger_set",
            new { groupName, setType, symbol, threshold, edge, compare, projectName }, () =>
            bridge.Dispatcher.Run(() =>
                new ScopeCoordinator(bridge.Dispatcher.Session)
                    .AddScopeTriggerSet(groupName, setType, symbol, threshold, edge, compare, projectName)), cancellationToken);

    // ---- twincat_list_scope_triggers ----------------------------------------

    [McpServerTool(Name = "twincat_list_scope_triggers", ReadOnly = true)]
    [Description("EXPERIMENTAL — reads the trigger properties by token match (keys not yet verified on a live TE130x). Enumerate all trigger groups and their trigger sets in the TwinCAT Scope project. Returns group name, action, and for each set: setType, symbol, threshold, edge, and compare condition. projectName selects the measurement project when the solution has more than one. Read-only.")]
    public string ListScopeTriggers(
        [Description("Name of the measurement project to list. Required when the solution has more than one; omit when there is exactly one.")] string? projectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_list_scope_triggers", new { projectName }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, groups) = new ScopeCoordinator(bridge.Dispatcher.Session)
                    .ListScopeTriggers(projectName);
                if (McpErrors.IsError(summary) || groups.Count == 0)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                foreach (var g in groups)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  Group '{g.Name}' → action: {g.Action}");
                    foreach (var s in g.Sets)
                    {
                        string cond = new[]
                        {
                            s.Symbol    is not null ? $"symbol={s.Symbol}" : null,
                            s.Threshold is not null ? $"threshold={s.Threshold}" : null,
                            s.Edge      is not null ? $"edge={s.Edge}" : null,
                            s.Compare   is not null ? $"compare={s.Compare}" : null,
                        }.Where(x => x is not null).Aggregate("", (a, b) => a + (a.Length > 0 ? ", " : "") + b);
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    Set [{s.SetType}]{(cond.Length > 0 ? ": " + cond : "")}");
                    }
                }
                return sb.ToString().TrimEnd();
            }), cancellationToken);
}
