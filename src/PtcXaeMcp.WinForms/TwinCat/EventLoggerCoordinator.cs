using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// xae-event-logger: validates input and shapes the TC3 EventLogger
/// read/confirm results into JSON for the MCP layer (mirrors
/// ActiveProjectCoordinator). Honest errors are returned as "[Error] ..."
/// strings, never a faked success.
/// </summary>
internal sealed class EventLoggerCoordinator(ITwinCatSession session)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static readonly string[] ValidModes =
        { "confirm", "clear-alarms", "clear-events", "all" };

    private const uint DefaultMaxEvents = 100;

    public string ReadEventLogger(uint maxEvents, string? minSeverity, string? amsNetId)
    {
        if (maxEvents == 0) maxEvents = DefaultMaxEvents;

        var r = session.ReadEventLogger(maxEvents, minSeverity, amsNetId);
        if (!r.Success)
            return $"[Error] {r.Message}";

        return JsonSerializer.Serialize(new
        {
            success = true,
            loggedCount = r.LoggedCount,
            activeAlarmCount = r.ActiveAlarmCount,
            message = r.Message,
            events = r.Events,
            activeAlarms = r.ActiveAlarms,
        }, JsonOpts);
    }

    public string ConfirmEventLogger(string mode, string? amsNetId)
    {
        mode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (Array.IndexOf(ValidModes, mode) < 0)
            return $"[Error] mode must be one of: {string.Join(", ", ValidModes)}.";

        var r = session.ConfirmEventLogger(mode, amsNetId);
        if (!r.Success)
            return $"[Error] {r.Message}";

        return JsonSerializer.Serialize(new
        {
            success = true,
            mode,
            activeAlarmsBefore = r.ActiveAlarmsBefore,
            activeAlarmsAfter = r.ActiveAlarmsAfter,
            message = r.Message,
        }, JsonOpts);
    }
}
