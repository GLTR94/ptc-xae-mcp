using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_read_event_logger", ReadOnly = true)]
    [Description("Read the TwinCAT 3 RUNTIME EventLogger (logged events + currently active alarms) " +
                 "via the EventLogger ADS proxy. This is NOT the Visual Studio Error List " +
                 "(use twincat_clear_error_list for that) — it is the runtime event/alarm log: " +
                 "message text, severity, source, timestamps, alarm/confirmation state. " +
                 "Returns JSON { success, loggedCount, activeAlarmCount, message, events[], activeAlarms[] } " +
                 "or an [Error] string. Requires the TwinCAT runtime reachable on the target.")]
    public string ReadEventLogger(
        [Description("Max number of logged events to return. 0 or omitted = 100.")] uint maxEvents = 0,
        [Description("Optional minimum severity filter: Verbose | Info | Warning | Error | Critical. " +
                     "Omit for all severities.")] string? minSeverity = null,
        [Description("Optional AMS NetId of the target runtime. Omit/empty = local runtime " +
                     "(single-XAE constraint: local only).")] string? amsNetId = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_event_logger", new { maxEvents, minSeverity, amsNetId }, () =>
            bridge.Dispatcher.Run(() =>
                new EventLoggerCoordinator(bridge.Dispatcher.Session)
                    .ReadEventLogger(maxEvents, minSeverity, amsNetId)), cancellationToken);

    [McpServerTool(Name = "twincat_confirm_event_logger", ReadOnly = false, Destructive = true)]
    [Description("Acknowledge/clear the TwinCAT 3 runtime EventLogger. " +
                 "mode: 'confirm' = ConfirmAllAlarms (acknowledge alarms waiting for confirmation); " +
                 "'clear-alarms' = ClearAllAlarms (clear raised alarms, reset confirmation); " +
                 "'clear-events' = ClearLoggedEvents (empty the event cache); " +
                 "'all' = confirm + clear-alarms + clear-events. " +
                 "Self-verifies via a pre/post active-alarm count delta. " +
                 "Returns JSON { success, mode, activeAlarmsBefore, activeAlarmsAfter, message } " +
                 "or an [Error] string. Requires the TwinCAT runtime reachable.")]
    public string ConfirmEventLogger(
        [Description("One of: confirm | clear-alarms | clear-events | all.")] string mode,
        [Description("Optional AMS NetId of the target runtime. Omit/empty = local runtime.")] string? amsNetId = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_confirm_event_logger", new { mode, amsNetId }, () =>
            bridge.Dispatcher.Run(() =>
                new EventLoggerCoordinator(bridge.Dispatcher.Session)
                    .ConfirmEventLogger(mode, amsNetId)), cancellationToken);
}
