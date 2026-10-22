using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    public string SafetyReconDump { get; set; } = "(fake)";
    public string SafetyRecon()
    {
        CallLog.Add("SafetyRecon()");
        return SafetyReconDump;
    }

    public string SafetyProbeDump { get; set; } = "(fake probe)";
    public Exception? SafetyProbeThrows { get; set; }
    public string SafetyProbeCreateAliasDevice(string? targetPath, string? aliasName, string? subTypesCsv, bool attemptWrite)
    {
        CallLog.Add($"SafetyProbeCreateAliasDevice(target={targetPath ?? "<null>"},name={aliasName ?? "<null>"},subTypes={subTypesCsv ?? "<null>"},attemptWrite={attemptWrite})");
        if (SafetyProbeThrows is not null) throw SafetyProbeThrows;
        return SafetyProbeDump;
    }

    // ── production safety tools ───────────────────────────────────────────────
    public string SafetyListDump { get; set; } = "(fake list)";
    public Exception? SafetyListThrows { get; set; }
    public string SafetyList()
    {
        CallLog.Add("SafetyList()");
        if (SafetyListThrows is not null) throw SafetyListThrows;
        return SafetyListDump;
    }

    public string SafetyReadIoMapDump { get; set; } = "(fake io-map)";
    public Exception? SafetyReadIoMapThrows { get; set; }
    public string SafetyReadIoMap()
    {
        CallLog.Add("SafetyReadIoMap()");
        if (SafetyReadIoMapThrows is not null) throw SafetyReadIoMapThrows;
        return SafetyReadIoMapDump;
    }

    public string SafetyReadProgramDump { get; set; } = "(fake program)";
    public Exception? SafetyReadProgramThrows { get; set; }
    public string SafetyReadProgram()
    {
        CallLog.Add("SafetyReadProgram()");
        if (SafetyReadProgramThrows is not null) throw SafetyReadProgramThrows;
        return SafetyReadProgramDump;
    }

    public string SafetyAddAliasDeviceResult { get; set; } = "(fake add)";
    public Exception? SafetyAddAliasDeviceThrows { get; set; }
    public string SafetyAddAliasDevice(string signalName, int subType, string? group)
    {
        CallLog.Add($"SafetyAddAliasDevice(signal={signalName},subType={subType},group={group ?? "<null>"})");
        if (SafetyAddAliasDeviceThrows is not null) throw SafetyAddAliasDeviceThrows;
        return SafetyAddAliasDeviceResult;
    }

    public string SafetyAddAliasDevice(string signalName, int subType)
    {
        CallLog.Add($"SafetyAddAliasDevice(signal={signalName},subType={subType})");
        if (SafetyAddAliasDeviceThrows is not null) throw SafetyAddAliasDeviceThrows;
        return SafetyAddAliasDeviceResult;
    }

    public string SafetyLinkAliasDeviceResult { get; set; } = "(fake link)";
    public Exception? SafetyLinkAliasDeviceThrows { get; set; }
    public string SafetyLinkAliasDevice(string aliasDevicePath, string plcSymbolPath)
    {
        CallLog.Add($"SafetyLinkAliasDevice(alias={aliasDevicePath},plc={plcSymbolPath})");
        if (SafetyLinkAliasDeviceThrows is not null) throw SafetyLinkAliasDeviceThrows;
        return SafetyLinkAliasDeviceResult;
    }
}
