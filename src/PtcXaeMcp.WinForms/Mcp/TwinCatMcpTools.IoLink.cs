using PtcXaeMcp.WinForms.Security;
using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_get_iolink_config", ReadOnly = true)]
    [Description("Read back the IO-Link configuration of a master terminal (e.g. EL6224) by tree path. " +
                 "Read-only. Requires XAE running with a solution open. " +
                 "Returns, per IO-Link port (1..4): the assigned device (name, vendor, product id, IODD file) " +
                 "and its named process data (e.g. 'Valve 1 solenoid 00'). " +
                 "Result body is a one-line summary followed by indented JSON. " +
                 "The fieldbus topology does not descend into IO-Link ports, so pass the EL6224's own tree path here.")]
    public string GetIoLinkConfig(
        [Description("Caret-separated tree path of the IO-Link master under TIID, e.g. " +
                     "'TIID^Device 1 (EtherCAT)^+CAB1-A1 (EK1200)^Term 28 (EL6224)'. " +
                     "Find it via twincat_get_fieldbus_topology.")]
        string treePath,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_get_iolink_config", new { treePath }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, json) = new IoLinkCoordinator(bridge.Dispatcher.Session).GetConfig(treePath);
                if (McpErrors.IsError(summary) ||
                    summary.StartsWith("No IO-Link", StringComparison.OrdinalIgnoreCase))
                    return summary;
                return summary + Environment.NewLine + json;
            }), cancellationToken);

    [McpServerTool(Name = "twincat_import_iodd", ReadOnly = false, Destructive = true)]
    [Description("Register an IO-Link device description (IODD .xml) into TwinCAT's IODD folder " +
                 "(…\\Config\\Io\\IOLink) so an IO-Link master (e.g. EL6224) can break the device down " +
                 "into named process data. Reads the device identity (vendorId/deviceId/productId) from the " +
                 "IODD and reports it. Idempotent (skips an identical already-registered file). " +
                 "Does NOT require an open solution — it writes the TwinCAT IODD library, not the project. " +
                 "TwinCAT picks the file up on its next IODD rescan.")]
    public string ImportIodd(
        [Description("Full path to the IODD .xml file to register, e.g. " +
                     "'C:\\\\downloads\\\\Festo-VABX-A-P-EL-20230815-IODD1.1.xml'.")]
        string sourcePath,
        [Description("Optional override of the target IODD folder. Leave empty to use the TwinCAT default " +
                     "(…\\Config\\Io\\IOLink).")]
        string? targetFolder = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_import_iodd", new { sourcePath, targetFolder }, () =>
            // No open-solution requirement for this tool (it writes the TwinCAT IODD
            // library, not the project), cancellationToken, so there is no solution directory to allow —
            // sourcePath is checked against the fixed roots only (%TEMP%, %LOCALAPPDATA%
            // \ptc-xae-mcp, the TwinCAT install dir, PTC_XAE_MCP_ALLOWED_PATHS).
            McpPathGuard.CheckRequiredFile(sourcePath, solutionDirectory: null, "sourcePath")
            ?? new IoddImportCoordinator().ImportIodd(sourcePath, targetFolder));

    [McpServerTool(Name = "twincat_check_iodd_present", ReadOnly = true)]
    [Description("Check whether the IODD (IO-Link device description) for a device is installed in TwinCAT's " +
                 "IODD folder (…\\Config\\Io\\IOLink) — the IO-Link analogue of checking an EtherCAT ESI is present. " +
                 "Identify the device by productId (e.g. 'VABX-A-P-EL-E12-IOS'), by ioddFileName, or by vendorId+deviceId. " +
                 "Returns 'IODD present: …' or a fail-closed '[Error] Missing IODD — STOP…' report instructing the user " +
                 "to obtain it. Run this before configuring/linking an IO-Link device so the workflow stops instead of " +
                 "configuring blind. Does NOT require an open solution.")]
    public string CheckIoddPresent(
        [Description("Device product id from the IODD's device-variant, e.g. 'VABX-A-P-EL-E12-IOS'. Optional.")]
        string? productId = null,
        [Description("Exact IODD filename, e.g. 'Festo-VABX-A-P-EL-20230815-IODD1.1.xml'. Optional.")]
        string? ioddFileName = null,
        [Description("IO-Link vendor id (use together with deviceId). Optional.")]
        int? vendorId = null,
        [Description("IO-Link device id (use together with vendorId). Optional.")]
        int? deviceId = null,
        [Description("Optional override of the IODD folder. Leave empty for the TwinCAT default (…\\Config\\Io\\IOLink).")]
        string? targetFolder = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_check_iodd_present",
            new { productId, ioddFileName, vendorId, deviceId, targetFolder }, () =>
            new IoddImportCoordinator().CheckPresent(productId, vendorId, deviceId, ioddFileName, targetFolder), cancellationToken);
}
