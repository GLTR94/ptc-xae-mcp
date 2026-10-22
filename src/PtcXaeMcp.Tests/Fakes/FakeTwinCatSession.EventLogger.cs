using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- xae-event-logger ----
    public EventLoggerReadResult FakeReadEventLoggerResult { get; set; } =
        new EventLoggerReadResult(true, 0, 0,
            Array.Empty<EventLogEntry>(), Array.Empty<EventLogEntry>(),
            "EventLogger read: 0 events, 0 active alarms.");
    public EventLoggerActionResult FakeConfirmEventLoggerResult { get; set; } =
        new EventLoggerActionResult(true, 0, 0, "Nothing to confirm — no active alarms.");
    public Exception? ReadEventLoggerThrows { get; set; }
    public Exception? ConfirmEventLoggerThrows { get; set; }

    public EventLoggerReadResult ReadEventLogger(uint maxEvents, string? minSeverity, string? amsNetId)
    {
        CallLog.Add($"ReadEventLogger(max={maxEvents}, minSev={minSeverity ?? "<null>"}, net={amsNetId ?? "<null>"})");
        if (ReadEventLoggerThrows is not null) throw ReadEventLoggerThrows;
        return FakeReadEventLoggerResult;
    }

    public EventLoggerActionResult ConfirmEventLogger(string mode, string? amsNetId)
    {
        CallLog.Add($"ConfirmEventLogger(mode={mode}, net={amsNetId ?? "<null>"})");
        if (ConfirmEventLoggerThrows is not null) throw ConfirmEventLoggerThrows;
        return FakeConfirmEventLoggerResult;
    }
}
