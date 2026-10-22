using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- Scope measurement surface -----------------------------------------

    public Exception? CreateScopeProjectThrows   { get; set; }
    public Exception? RemoveScopeProjectThrows   { get; set; }
    public Exception? AddScopeChannelThrows      { get; set; }
    public Exception? SetScopeAxisModeThrows     { get; set; }
    public Exception? SetScopeLabelsThrows       { get; set; }
    public Exception? ListScopeChannelsThrows    { get; set; }
    public Exception? GetScopeStatusThrows       { get; set; }
    public Exception? ExportScopeDataThrows      { get; set; }

    public string FakeCreateScopeProjectResult { get; set; } = "Created scope project 'MeasurementProject'.";
    public string FakeRemoveScopeProjectResult { get; set; } = "Removed scope project 'MeasurementProject' from the solution. The solution was not saved.";
    public string FakeAddScopeChannelResult    { get; set; } = "Added channel 'Ch1' under chart 'Chart1' / axis 'Axis1', bound to 'MAIN.rValue'. Path: Chart1^Axis1^Ch1.";
    public string FakeSetScopeAxisModeResult   { get; set; } = "Set axis mode 'stacked' on chart 'Chart1'.";
    public string FakeSetScopeLabelsResult     { get; set; } = "Set labels on 'Chart1': name='My Chart'.";
    public List<ScopeNodeEntry> FakeScopeNodes { get; set; } = new();
    public ScopeStatus FakeScopeStatus         { get; set; } = new ScopeStatus(false, null, 0, 0, 0, 0);
    public string FakeExportScopeDataResult    { get; set; } = @"C:\repos\MyProject\ScopeData.csv";

    public string CreateScopeProject(string? projectName)
    {
        CallLog.Add($"CreateScopeProject(name={projectName ?? "<null>"})");
        if (CreateScopeProjectThrows is not null) throw CreateScopeProjectThrows;
        return FakeCreateScopeProjectResult;
    }

    public string RemoveScopeProject(string? projectName)
    {
        CallLog.Add($"RemoveScopeProject(name={projectName ?? "<null>"})");
        if (RemoveScopeProjectThrows is not null) throw RemoveScopeProjectThrows;
        return FakeRemoveScopeProjectResult;
    }

    public string AddScopeChannel(string symbol, string? chartName, string? axisName, string? channelName, bool stackedAxis,
        string? symbolName, int? targetPort, string? sampleMode, int? sampleTimeMs, string? symbolComment,
        string? projectName)
    {
        CallLog.Add($"AddScopeChannel(symbol={symbol}, chart={chartName ?? "<null>"}, axis={axisName ?? "<null>"}, channel={channelName ?? "<null>"}, stacked={stackedAxis}, symbolName={symbolName ?? "<null>"}, port={(targetPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>")}, sampleMode={sampleMode ?? "<null>"}, sampleMs={(sampleTimeMs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>")}, comment={symbolComment ?? "<null>"})");
        if (AddScopeChannelThrows is not null) throw AddScopeChannelThrows;
        return FakeAddScopeChannelResult;
    }

    public string SetScopeAxisMode(string chartName, string mode, string? projectName)
    {
        CallLog.Add($"SetScopeAxisMode(chart={chartName}, mode={mode})");
        if (SetScopeAxisModeThrows is not null) throw SetScopeAxisModeThrows;
        return FakeSetScopeAxisModeResult;
    }

    public string SetScopeLabels(string nodeSelector, string? name, string? chartTitle, string? axisTitle, string? projectName)
    {
        CallLog.Add($"SetScopeLabels(node={nodeSelector}, name={name ?? "<null>"}, chartTitle={chartTitle ?? "<null>"}, axisTitle={axisTitle ?? "<null>"})");
        if (SetScopeLabelsThrows is not null) throw SetScopeLabelsThrows;
        return FakeSetScopeLabelsResult;
    }

    public IReadOnlyList<ScopeNodeEntry> ListScopeChannels(string? projectName)
    {
        CallLog.Add("ListScopeChannels()");
        if (ListScopeChannelsThrows is not null) throw ListScopeChannelsThrows;
        return FakeScopeNodes;
    }

    public ScopeStatus GetScopeStatus(string? projectName)
    {
        CallLog.Add("GetScopeStatus()");
        if (GetScopeStatusThrows is not null) throw GetScopeStatusThrows;
        return FakeScopeStatus;
    }

    public string ExportScopeData(string format, string? projectName)
    {
        CallLog.Add($"ExportScopeData(format={format})");
        if (ExportScopeDataThrows is not null) throw ExportScopeDataThrows;
        return FakeExportScopeDataResult;
    }

    // ---- scope-acquisition-symbols -----------------------------------------

    public Exception? ReadScopeAcquisitionThrows      { get; set; }
    public Exception? SetScopeAcquisitionSymbolThrows { get; set; }
    public ScopeAcquisitionInfo FakeScopeAcquisitionInfo { get; set; } =
        new ScopeAcquisitionInfo("Acq1", "DataPool^Acq1", "GVL.x", "BOOL", "Task Sampletime",
            "10", "851", "Local", true, Array.Empty<ScopeAcquisitionProperty>());
    public string FakeSetScopeAcquisitionSymbolResult { get; set; } =
        "Set acquisition 'Acq1' symbol: symbolName='GVL.x'.";

    public ScopeAcquisitionInfo ReadScopeAcquisition(string acqSelector, string? projectName)
    {
        CallLog.Add($"ReadScopeAcquisition(sel={acqSelector})");
        if (ReadScopeAcquisitionThrows is not null) throw ReadScopeAcquisitionThrows;
        return FakeScopeAcquisitionInfo;
    }

    public string SetScopeAcquisitionSymbol(string acqSelector, string? symbolName, int? targetPort,
        string? targetSystem, string? sampleMode, int? sampleTimeMs, double? timeOffsetS,
        string? symbolComment, bool? enabled, string? projectName)
    {
        CallLog.Add($"SetScopeAcquisitionSymbol(sel={acqSelector}, symbolName={symbolName ?? "<null>"}, port={(targetPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>")}, system={targetSystem ?? "<null>"}, sampleMode={sampleMode ?? "<null>"}, sampleMs={(sampleTimeMs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>")}, offset={(timeOffsetS?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>")}, comment={symbolComment ?? "<null>"}, enabled={(enabled?.ToString() ?? "<null>")})");
        if (SetScopeAcquisitionSymbolThrows is not null) throw SetScopeAcquisitionSymbolThrows;
        return FakeSetScopeAcquisitionSymbolResult;
    }

    // ---- scope-recording -------------------------------------------------------

    public Exception? StartScopeRecordThrows        { get; set; }
    public Exception? StopScopeRecordThrows         { get; set; }
    public Exception? GetScopeRecordStateThrows     { get; set; }
    public Exception? SaveScopeDataThrows           { get; set; }
    public Exception? SetScopeRecordSettingsThrows  { get; set; }
    public Exception? CreateScopeTriggerGroupThrows { get; set; }
    public Exception? AddScopeTriggerSetThrows      { get; set; }
    public Exception? ListScopeTriggersThrows       { get; set; }

    public string FakeStartScopeRecordResult        { get; set; } = "Recording started.";
    public string FakeStopScopeRecordResult         { get; set; } = "Recording stopped.";
    public ScopeRecordState FakeScopeRecordState    { get; set; } = new ScopeRecordState(ScopeViewState.Config, false, false);
    public string FakeSaveScopeDataResult           { get; set; } = @"C:\repos\MyProject\ScopeData.svdx";
    public string FakeSetScopeRecordSettingsResult  { get; set; } = "Set record settings: recordTimeS=10.";
    public string FakeCreateScopeTriggerGroupResult { get; set; } = "Created trigger group 'TriggerGroup_StopRecord' with action 'StopRecord'.";
    public string FakeAddScopeTriggerSetResult      { get; set; } = "Added trigger set 'TriggerSet_ChannelSet_bVar' to group 'TriggerGroup_StopRecord': setType=ChannelSet.";
    public List<ScopeTriggerGroupEntry> FakeScopeTriggers { get; set; } = new();

    public string StartScopeRecord(string? projectName)
    {
        CallLog.Add($"StartScopeRecord(project={projectName ?? "<null>"})");
        if (StartScopeRecordThrows is not null) throw StartScopeRecordThrows;
        return FakeStartScopeRecordResult;
    }

    public string StopScopeRecord(string? projectName)
    {
        CallLog.Add($"StopScopeRecord(project={projectName ?? "<null>"})");
        if (StopScopeRecordThrows is not null) throw StopScopeRecordThrows;
        return FakeStopScopeRecordResult;
    }

    public ScopeRecordState GetScopeRecordState(string? projectName)
    {
        CallLog.Add($"GetScopeRecordState(project={projectName ?? "<null>"})");
        if (GetScopeRecordStateThrows is not null) throw GetScopeRecordStateThrows;
        return FakeScopeRecordState;
    }

    public string SaveScopeData(string? path, string? projectName)
    {
        CallLog.Add($"SaveScopeData(path={path ?? "<null>"}, project={projectName ?? "<null>"})");
        if (SaveScopeDataThrows is not null) throw SaveScopeDataThrows;
        return FakeSaveScopeDataResult;
    }

    public string SetScopeRecordSettings(double? recordTimeS, bool? ringbuffer, string? fileStore,
        string? recordMode, string? autoSaveMode, string? autoSavePath, string? filenameMask,
        string? syncMode, string? projectName)
    {
        CallLog.Add($"SetScopeRecordSettings(recordTimeS={recordTimeS?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"}, ringbuffer={ringbuffer?.ToString() ?? "<null>"}, recordMode={recordMode ?? "<null>"})");
        if (SetScopeRecordSettingsThrows is not null) throw SetScopeRecordSettingsThrows;
        return FakeSetScopeRecordSettingsResult;
    }

    public string CreateScopeTriggerGroup(string action, string? projectName)
    {
        CallLog.Add($"CreateScopeTriggerGroup(action={action})");
        if (CreateScopeTriggerGroupThrows is not null) throw CreateScopeTriggerGroupThrows;
        return FakeCreateScopeTriggerGroupResult;
    }

    public string AddScopeTriggerSet(string groupName, string setType, string? symbol,
        double? threshold, string? edge, string? compare, string? projectName)
    {
        CallLog.Add($"AddScopeTriggerSet(group={groupName}, setType={setType}, symbol={symbol ?? "<null>"}, threshold={threshold?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"})");
        if (AddScopeTriggerSetThrows is not null) throw AddScopeTriggerSetThrows;
        return FakeAddScopeTriggerSetResult;
    }

    public IReadOnlyList<ScopeTriggerGroupEntry> ListScopeTriggers(string? projectName)
    {
        CallLog.Add($"ListScopeTriggers(project={projectName ?? "<null>"})");
        if (ListScopeTriggersThrows is not null) throw ListScopeTriggersThrows;
        return FakeScopeTriggers;
    }
}
