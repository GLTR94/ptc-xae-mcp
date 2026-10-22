using System.Globalization;
#if TWINCAT
using TcEventLoggerAdsProxyLib;
#endif

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // ---- xae-event-logger ----------------------------------------------------
    //
    // Reads / acknowledges the TC3 *runtime* EventLogger (logged events + active
    // alarms) via the managed ADS-proxy assembly shipped with TwinCAT
    // (Beckhoff.TwinCAT.TcEventLoggerAdsProxy.Native.Interop — see the csproj
    // reference). This is NOT the VS Error List (that is xae-error-list /
    // ClearErrorList) — completely different mechanism: ADS to the runtime,
    // not DTE automation.
    //
    // Verified API surface (reflected on 4026.22, see the
    // reference_tc3_4026_22_quirks memory):
    //   TcEventLogger : ITcEventLogger (+2..5), IDisposable
    //     Connect(string Address)  // "" => local runtime
    //     Disconnect()
    //     bool IsConnected
    //     ITcLoggedEventCollection GetLoggedEvents(uint nMaxEntiries)
    //     ITcAlarmCollection ActiveAlarms { get; }
    //     ConfirmAllAlarms() / ClearAllAlarms(bool) / ClearLoggedEvents()
    //   ITcLoggedEvent: EventClass, EventId, SeverityLevel, EventType,
    //     SourceId, SourceName, TimeRaised/Confirmed/Cleared,
    //     WithConfirmation, GetText(langId); (3) IsActive; (4) ConfirmationState
    //
    // langId pinned to 1033 (en) for stable output (box is German).

#if !TWINCAT
    // ci-twincat-compile-seams (#47): built without TwinCAT. Keep the same
    // public signatures so callers (EventLoggerCoordinator / MCP tools / unit
    // tests) compile and run on hosted CI; report honestly that the COM path
    // is not in this build instead of faking success.
    public EventLoggerReadResult ReadEventLogger(uint maxEvents, string? minSeverity, string? amsNetId)
        => new(false, -1, -1, Array.Empty<EventLogEntry>(), Array.Empty<EventLogEntry>(), TwinCatNotBuilt.Message);

    public EventLoggerActionResult ConfirmEventLogger(string mode, string? amsNetId)
        => new(false, -1, -1, TwinCatNotBuilt.Message);
#else

    private const int EventTextLangId = 1033;
    private static readonly TimeSpan EventLoggerTimeout = TimeSpan.FromSeconds(30);

    public EventLoggerReadResult ReadEventLogger(uint maxEvents, string? minSeverity, string? amsNetId) =>
        ComBusyRetry.Run(() =>
        {
            SeverityLevelEnum? minSev = ParseSeverity(minSeverity);
            if (minSeverity is { Length: > 0 } && minSev is null)
                return new EventLoggerReadResult(false, -1, -1,
                    Array.Empty<EventLogEntry>(), Array.Empty<EventLogEntry>(),
                    $"Unknown minSeverity '{minSeverity}'. Use one of: Verbose, Info, Warning, Error, Critical.");

            TcEventLogger? logger = null;
            try
            {
                logger = new TcEventLogger();
                ITcEventLogger el = logger;
                el.Connect(amsNetId ?? string.Empty);
                if (!el.IsConnected)
                    return new EventLoggerReadResult(false, -1, -1,
                        Array.Empty<EventLogEntry>(), Array.Empty<EventLogEntry>(),
                        "Could not connect to the TC3 EventLogger (runtime not reachable on this target).");

                var events = new List<EventLogEntry>();
                int logged = -1;
                try
                {
                    dynamic coll = el.GetLoggedEvents(maxEvents);
                    logged = (int)coll.Count;
                    foreach (var e in EnumerateCom(coll, logged))
                        AddIfSevOk(events, MapEvent(e), minSev);
                }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* logged stays -1: unreadable, reported honestly below */ }

                var alarms = new List<EventLogEntry>();
                int activeCount = -1;
                try
                {
                    dynamic ac = el.ActiveAlarms;
                    activeCount = (int)ac.Count;
                    foreach (var a in EnumerateCom(ac, activeCount))
                        AddIfSevOk(alarms, MapEvent(a), minSev);
                }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* activeCount stays -1 */ }

                if (logged < 0 && activeCount < 0)
                    return new EventLoggerReadResult(false, -1, -1,
                        Array.Empty<EventLogEntry>(), Array.Empty<EventLogEntry>(),
                        "Connected to the EventLogger but neither the event cache nor the active-alarm list could be read.");

                return new EventLoggerReadResult(true, logged, activeCount, events, alarms,
                    $"EventLogger read: {events.Count} event(s){(minSev is null ? "" : $" ≥ {minSev}")}, {alarms.Count} active alarm(s).");
            }
            catch (Exception ex) when (ComCall.NotBusy(ex)) {
                return new EventLoggerReadResult(false, -1, -1,
                    Array.Empty<EventLogEntry>(), Array.Empty<EventLogEntry>(),
                    $"ReadEventLogger failed: {DescribeEventLoggerError(ex)}");
            }
            finally
            {
                SafeDisconnect(logger);
            }
        }, EventLoggerTimeout);

    public EventLoggerActionResult ConfirmEventLogger(string mode, string? amsNetId) =>
        ComBusyRetry.Run(() =>
        {
            TcEventLogger? logger = null;
            try
            {
                logger = new TcEventLogger();
                ITcEventLogger el = logger;
                el.Connect(amsNetId ?? string.Empty);
                if (!el.IsConnected)
                    return new EventLoggerActionResult(false, -1, -1,
                        "Could not connect to the TC3 EventLogger (runtime not reachable on this target).");

                int before = TryActiveCount(el);

                bool doConfirm = mode is "confirm" or "all";
                bool doClearAlarms = mode is "clear-alarms" or "all";
                bool doClearEvents = mode is "clear-events" or "all";

                if (doConfirm) el.ConfirmAllAlarms();
                if (doClearAlarms) el.ClearAllAlarms(true);
                if (doClearEvents) el.ClearLoggedEvents();

                int after = TryActiveCount(el);

                string did = mode switch
                {
                    "confirm" => "Confirmed all alarms awaiting confirmation",
                    "clear-alarms" => "Cleared all raised alarms",
                    "clear-events" => "Cleared the logged-event cache",
                    _ => "Confirmed + cleared alarms + cleared the event cache",
                };
                string delta = before < 0 || after < 0
                    ? " (active-alarm count unreadable for verification)"
                    : $" — active alarms {before} → {after}.";
                return new EventLoggerActionResult(true, before, after, did + delta);
            }
            catch (Exception ex) when (ComCall.NotBusy(ex)) {
                return new EventLoggerActionResult(false, -1, -1,
                    $"ConfirmEventLogger failed: {DescribeEventLoggerError(ex)}");
            }
            finally
            {
                SafeDisconnect(logger);
            }
        }, EventLoggerTimeout);

    // ---- helpers -------------------------------------------------------------

    private static int TryActiveCount(ITcEventLogger el)
    {
        try { dynamic ac = el.ActiveAlarms; return (int)ac.Count; }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return -1; }
    }

    // 0x8007007E (ERROR_MOD_NOT_FOUND): the native TcEventLoggerAdsProxy.dll
    // (or one of its deps) could not be loaded. Build now copies it next to the
    // host exe (csproj, issue #43); if it still fails, give the operator the
    // actionable cause instead of the raw HRESULT.
    private const int ErrorModNotFound = unchecked((int)0x8007007E);

    private static string DescribeEventLoggerError(Exception ex)
    {
        bool nativeMissing =
            ex is DllNotFoundException
            || ex.HResult == ErrorModNotFound
            || ex.Message.Contains("TcEventLoggerAdsProxy", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("0x8007007E", StringComparison.OrdinalIgnoreCase);

        if (nativeMissing)
            return "the native TcEventLoggerAdsProxy.dll could not be loaded. It is " +
                   "shipped with TwinCAT under …\\3.1\\Components\\TcEventloggerV2\\v1xx " +
                   "and the build copies it next to the host exe — rebuild the WinForms " +
                   "host so the DLL lands beside it, or add that TwinCAT folder to PATH. " +
                   $"(original: {ex.Message})";

        return ex.Message;
    }

    private static void SafeDisconnect(TcEventLogger? logger)
    {
        if (logger is null) return;
        try { ((ITcEventLogger)logger).Disconnect(); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        try { logger.Dispose(); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
    }

    // COM collections (ITcLoggedEventCollection / ITcAlarmCollection) expose
    // Count + an Item indexer. Indexing base differs across COM collections;
    // try 0-based first, fall back to 1-based, all late-bound.
    private static IEnumerable<dynamic> EnumerateCom(dynamic coll, int count)
    {
        if (count <= 0) yield break;

        bool oneBased = false;
        dynamic? first = null;
        try { first = coll.Item[0]; }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { oneBased = true; }

        if (!oneBased)
        {
            yield return first!;
            for (int i = 1; i < count; i++)
            {
                dynamic? item = null;
                try { item = coll.Item[i]; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                if (item is not null) yield return item;
            }
        }
        else
        {
            for (int i = 1; i <= count; i++)
            {
                dynamic? item = null;
                try { item = coll.Item[i]; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                if (item is not null) yield return item;
            }
        }
    }

    private static void AddIfSevOk(List<EventLogEntry> list, EventLogEntry e, SeverityLevelEnum? min)
    {
        if (min is null || ParseSeverity(e.Severity) is { } s && (int)s >= (int)min)
            list.Add(e);
    }

    private static SeverityLevelEnum? ParseSeverity(string? s) =>
        Enum.TryParse<SeverityLevelEnum>((s ?? string.Empty).Trim(), ignoreCase: true, out var v)
            ? v : null;

    private static string? IsoOrNull(DateTime dt, long fileTime) =>
        fileTime == 0 || dt == default
            ? null
            : dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static EventLogEntry MapEvent(dynamic e)
    {
        // e is ITcLoggedEvent (events) or ITcAlarm whose concrete TcAlarm also
        // implements ITcLoggedEvent. Access late-bound and defend every field.
        string Get(Func<string> f) { try { return f() ?? string.Empty; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return string.Empty; } }
        T GetV<T>(Func<T> f, T dflt) { try { return f(); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return dflt; } }

        string text = Get(() => (string)e.GetText(EventTextLangId));
        string sev = GetV(() => ((SeverityLevelEnum)e.SeverityLevel).ToString(), "Info");
        string type = GetV(() => ((EventTypeEnum)e.EventType).ToString(), "Message");
        string conf = GetV(() => ((ConfirmationStateEnum)e.ConfirmationState).ToString(), "NotSupported");
        bool isActive = GetV(() => (bool)e.IsActive, false);
        bool withConf = GetV(() => (bool)e.WithConfirmation, false);
        uint srcId = GetV(() => (uint)e.SourceId, 0u);
        uint evId = GetV(() => (uint)e.EventId, 0u);
        string src = Get(() => (string)e.SourceName);
        string evClass = GetV(() => e.EventClass.ToString(), string.Empty);

        string? tRaised = GetV<string?>(() => IsoOrNull((DateTime)e.TimeRaised, (long)e.FileTimeRaised), null);
        string? tConf = GetV<string?>(() => IsoOrNull((DateTime)e.TimeConfirmed, (long)e.FileTimeConfirmed), null);
        string? tClr = GetV<string?>(() => IsoOrNull((DateTime)e.TimeCleared, (long)e.FileTimeCleared), null);

        return new EventLogEntry(type, sev, src, srcId, evClass, evId, text,
            tRaised, tConf, tClr, withConf, conf, isActive);
    }
#endif
}
