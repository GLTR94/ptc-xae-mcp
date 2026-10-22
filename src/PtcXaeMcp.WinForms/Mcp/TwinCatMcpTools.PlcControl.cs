using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_plc_start", ReadOnly = false, Destructive = true)]
    [Description("Start the PLC runtime — the 'Start' / RUN action (F5 in the PLC context), via the ADS " +
                 "state machine. Use this after twincat_login with mode=download, which leaves the PLC in " +
                 "STOP: unlike twincat_restart_runtime, this keeps the freshly downloaded program instead " +
                 "of reloading the old boot project. Reads the state back and reports what the runtime " +
                 "actually is, so a command that is accepted but has no effect is reported as a failure.")]
    public string PlcStart(
        [Description("Target AmsNetId. Omit to use the local AMS router's NetId.")] string? amsNetId = null,
        [Description("ADS port of the PLC runtime. Default 851 (first PLC runtime).")] int adsPort = 851,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_plc_start", new { amsNetId, adsPort }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcControlCoordinator(bridge.Dispatcher.Session).PlcStart(amsNetId, adsPort)), cancellationToken);

    [McpServerTool(Name = "twincat_plc_stop", ReadOnly = false, Destructive = true)]
    [Description("Stop the PLC runtime — the 'Stop' action, via the ADS state machine. Counterpart to " +
                 "twincat_plc_start. Reads the state back and reports what the runtime actually is. " +
                 "This halts the program on the target: on a real machine that stops the process.")]
    public string PlcStop(
        [Description("Target AmsNetId. Omit to use the local AMS router's NetId.")] string? amsNetId = null,
        [Description("ADS port of the PLC runtime. Default 851 (first PLC runtime).")] int adsPort = 851,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_plc_stop", new { amsNetId, adsPort }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcControlCoordinator(bridge.Dispatcher.Session).PlcStop(amsNetId, adsPort)), cancellationToken);

    [McpServerTool(Name = "twincat_activate_boot_project", ReadOnly = false, Destructive = true)]
    [Description("Write the current program as the boot project — the 'Activate Boot Project' action in the " +
                 "XAE PLC menu. Without it a download lives in RAM only and the target falls back to the " +
                 "previous boot project on the next power cycle. " +
                 "Uses ITcPlcProject.GenerateBootProject on the PLC project node, which works whether or not " +
                 "the XAE is logged in (the Solution-Explorer menu action is greyed out while offline; this " +
                 "is not). dryRun defaults to TRUE and changes nothing — it reports which member this XAE " +
                 "exposes, so it is safe to run against a running plant. " +
                 "With dryRun=false the effect is PROVEN from the _Boot artifacts: the result lists which " +
                 "files were rewritten and fails if no PLC boot application (*.app) was produced. " +
                 "Requires XAE running with a solution open.")]
    public string ActivateBootProject(
        [Description("TRUE (default) only probes and changes nothing. FALSE actually writes the boot project.")] bool dryRun = true,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_activate_boot_project", new { dryRun }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcControlCoordinator(bridge.Dispatcher.Session).ActivateBootProject(dryRun)), cancellationToken);
}
