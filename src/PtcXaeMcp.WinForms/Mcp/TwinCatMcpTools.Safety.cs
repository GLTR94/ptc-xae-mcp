using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    // twincat_safety_recon moved to TwinCatMcpToolsDiagnostics (Mcp/TwinCatMcpTools.Diagnostics.cs) —
    // a read-only research spike, registered only with --diagnostics / PTC_XAE_MCP_DIAGNOSTICS=1.

    // ── Production safety tools ───────────────────────────────────────────────

    [McpServerTool(Name = "twincat_safety_list", ReadOnly = true)]
    [Description("Enumerate the TwinCAT Safety project structure: alias devices with SDSID, AliasDeviceType " +
                 "Type/SubType, IO name and LinkingMode as written in each .sds (the device name is the file name), " +
                 "the folder they live in, groups (.sal) and user FBs (.ufb). Read-only — reads .splcproj and .sds " +
                 "files from disk.")]
    public string SafetyList(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_safety_list", null, () =>
            bridge.Dispatcher.Run(() => new SafetyCoordinator(bridge.Dispatcher.Session).List()), cancellationToken);

    [McpServerTool(Name = "twincat_safety_read_io_map", ReadOnly = true)]
    [Description("Read the safe-I/O assignment as three separate fact lists: the alias devices (.sds: SDSID, " +
                 "type, IO); the channel usage of each group (.sal aliasDeviceIoUsage: variable, source/target, " +
                 "sdsId, channel), joined to a device ONLY by sdsId = the device's SDSID; and Mappings.xml rows as " +
                 "found, naming a device only when the row carries its own SDSID. Nothing is joined by position or " +
                 "guessed. Read-only.")]
    public string SafetyReadIoMap(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_safety_read_io_map", null, () =>
            bridge.Dispatcher.Run(() => new SafetyCoordinator(bridge.Dispatcher.Session).ReadIoMap()), cancellationToken);

    [McpServerTool(Name = "twincat_safety_read_program", ReadOnly = true)]
    [Description("Structured dump of the FBD safety logic per group/network: FB instances " +
                 "(type + name), input/output port sources/targets (connection monikers), and " +
                 "FB parameters (discrepancy, Tof off-delays, EDM, restart). Flags [inferred] wiring. " +
                 "Read-only — parses .sal group files.")]
    public string SafetyReadProgram(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_safety_read_program", null, () =>
            bridge.Dispatcher.Run(() => new SafetyCoordinator(bridge.Dispatcher.Session).ReadProgram()), cancellationToken);

    [McpServerTool(Name = "twincat_safety_add_alias_device", ReadOnly = false, Destructive = true)]
    [Description("EXPERIMENTAL (the XAE reload step is not yet verified against a live TwinSAFE project). Create a " +
                 "standard digital alias device (a BOOL exchanged between the safety logic and the standard PLC): " +
                 "validates the name (IEC identifier with a configured signal prefix, see PTC_XAE_MCP_SAFETY_PREFIXES), " +
                 "allocates an SDSID no existing .sds uses, writes the .sds in the TwinSAFE editor's own format " +
                 "(Type 1, SubType 1 = digital input / 2 = digital output, Crc 0) next to the group's existing alias " +
                 "devices, registers it in .splcproj (temp files; the .sds is rolled back if the project file cannot be " +
                 "updated), reads it back, then reloads the project in XAE. Refuses an existing name. " +
                 "The CRC is left at 0 — run 'Verify Complete Safety Project' in the TwinSAFE editor before download. " +
                 "Never forges or computes the CRC. Never downloads/activates the safety project.")]
    public string SafetyAddAliasDevice(
        [Description("Signal name — becomes the .sds file name. Must be an IEC identifier starting with a configured " +
                     "prefix (default bSI_, bSQ_, SI, SQ; override with PTC_XAE_MCP_SAFETY_PREFIXES).")]
        string signalName,
        [Description("2 = '1 Digital Output (Standard)': safety → standard PLC (e.g. bSI_ feedback). " +
                     "1 = '1 Digital Input (Standard)': standard PLC → safety (e.g. bSQ_ request). Default 2.")]
        int subType = 2,
        [Description("Group (.sal name without extension) the alias device belongs to. Required when the project has " +
                     "more than one group; omit otherwise.")]
        string? group = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_safety_add_alias_device",
            new { signalName, subType, group }, () =>
            bridge.Dispatcher.Run(() =>
                new SafetyCoordinator(bridge.Dispatcher.Session).AddAliasDevice(signalName, subType, group)), cancellationToken);

    [McpServerTool(Name = "twincat_safety_link_alias_device", ReadOnly = false, Destructive = true)]
    [Description("Link an alias device's EL1918 connection-image symbol to a standard-PLC symbol " +
                 "(e.g. GVL_Safety.bSI_DriveReleased) via ITcSysManager.LinkVariables. " +
                 "Requires the safety project to be mapped/built so the connection symbol exists; " +
                 "returns a clear precondition error if not yet mapped.")]
    public string SafetyLinkAliasDevice(
        [Description("Caret-path of the alias device connection-image symbol in the safety sysmanager " +
                     "(e.g. 'TISC^TwinSAFE_PLC^Alias Devices^bSI_DriveReleased').")]
        string aliasDevicePath,
        [Description("Dot-path of the standard-PLC symbol to link to (e.g. 'GVL_Safety.bSI_DriveReleased').")]
        string plcSymbolPath,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_safety_link_alias_device",
            new { aliasDevicePath, plcSymbolPath }, () =>
            bridge.Dispatcher.Run(() =>
                bridge.Dispatcher.Session.SafetyLinkAliasDevice(aliasDevicePath, plcSymbolPath)), cancellationToken);

    // twincat_safety_probe_create_alias moved to TwinCatMcpToolsDiagnostics
    // (Mcp/TwinCatMcpTools.Diagnostics.cs) — a write-probe research tool, registered only
    // with --diagnostics / PTC_XAE_MCP_DIAGNOSTICS=1.
}
