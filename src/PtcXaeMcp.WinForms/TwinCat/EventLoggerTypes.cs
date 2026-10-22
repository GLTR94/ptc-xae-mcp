namespace PtcXaeMcp.WinForms.TwinCat;

// ---- xae-event-logger -------------------------------------------------------
// Result shapes for the TC3 runtime EventLogger read/confirm tools.
// Mirrors the honest-result style of ErrorListClearResult (see BuildTypes.cs):
// every path reports the truth, success is never faked.

internal sealed record EventLogEntry(
    string Type,               // EventTypeEnum: "Message" | "Alarm"
    string Severity,           // SeverityLevelEnum: Verbose|Info|Warning|Error|Critical
    string Source,             // ITcLoggedEvent.SourceName
    uint SourceId,             // ITcLoggedEvent.SourceId
    string EventClass,         // ITcLoggedEvent.EventClass (GUID)
    uint EventId,              // ITcLoggedEvent.EventId
    string Text,               // ITcLoggedEvent.GetText(langId)
    string? TimeRaised,        // ISO-8601 or null when default
    string? TimeConfirmed,
    string? TimeCleared,
    bool WithConfirmation,
    string ConfirmationState,  // ConfirmationStateEnum
    bool IsActive);

internal sealed record EventLoggerReadResult(
    bool Success,
    int LoggedCount,           // entries returned (best-effort; -1 if unreadable)
    int ActiveAlarmCount,      // ActiveAlarms.Count (best-effort; -1 if unreadable)
    IReadOnlyList<EventLogEntry> Events,
    IReadOnlyList<EventLogEntry> ActiveAlarms,
    string Message);

internal sealed record EventLoggerActionResult(
    bool Success,
    int ActiveAlarmsBefore,    // best-effort; -1 if unreadable
    int ActiveAlarmsAfter,     // re-read after the action; -1 if unreadable
    string Message);
