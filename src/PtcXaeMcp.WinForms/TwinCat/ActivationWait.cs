using System.Diagnostics;
using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

// Issue #195: twincat_activate_config(autoConfirmRestart) used to report "Activated" as soon as
// StartRestartTwinCAT() returned. Seven seconds later the target failed ("license not found"), and the
// XAE sat behind a modal "Zielsystem meldet fatalen Fehler" box that blocked every later call. The
// restart is now judged by what the target does afterwards. This file is the pure part: the decision
// loop over injected probes (state reader, XAE modal finder and closer, dialog watcher, clock). It is
// unit-tested without an XAE or a runtime. The Win32 and ADS probes are in ActivationRuntimeProbes.cs.

/// <summary>One ADS ReadState of the activation target's TwinCAT system service (AMS port 10000).</summary>
/// <param name="State">The ADS state name ("Run", "Config", "Reset", …) when the read succeeded.</param>
/// <param name="Error">Why the read failed (ADS error code, no route, …); null when it succeeded.</param>
internal sealed record TargetStateReading(string? State, string? Error)
{
    public bool IsRun => Error is null && string.Equals(State, "Run", StringComparison.OrdinalIgnoreCase);

    public static TargetStateReading Of(string state) => new(state, null);

    public static TargetStateReading Unreachable(string error) => new(null, error);

    public override string ToString() => Error is null ? State ?? "unknown" : $"no ADS state ({Error})";
}

/// <summary>
/// The AMS NetId whose system state proves an activation (issue #195): the project's target NetId, or
/// the local system for a Local target (no target, an empty one or 0.0.0.0.0.0).
/// </summary>
internal sealed record ActivationTarget(string NetId, bool IsLocal)
{
    /// <summary>AMS port of the TwinCAT system service; its ADS state is the state of the runtime.</summary>
    public const int SystemServicePort = 10000;

    /// <summary>Loopback AMS NetId: the local router always serves it as the local system.</summary>
    public const string LoopbackNetId = LocalAmsNetId.LoopbackNetId;

    /// <summary>
    /// The NetId to read for <paramref name="projectTarget"/> (what <c>ITcSysManager.GetTargetNetId()</c>
    /// returns). A Local target (no target, an empty one, 0.0.0.0.0.0 or "local") and a target that names the local
    /// system (the loopback, or the NetId of <paramref name="local"/>) are read at the local AMS router's NetId
    /// (issue #202), and through the loopback only when the router did not answer. The fallback after the router is
    /// the registry's AmsNetId, and that can be stale: on the dev VM (TC 4026) ReadState on it timed out while the
    /// router's NetId and the loopback answered (live, 2026-09-27). So while the router answers, a project target that
    /// only matches the registry is not taken for the local system. Null when the project names a target that is not
    /// an AMS NetId: there is nothing an ADS read could confirm.
    /// </summary>
    public static ActivationTarget? Resolve(string? projectTarget, LocalAmsNetId local)
    {
        ArgumentNullException.ThrowIfNull(local);
        string target = projectTarget?.Trim() ?? string.Empty;
        string readLocalAt = local.IsFromRouter ? local.NetId : LoopbackNetId;
        if (target.Length == 0
            || string.Equals(target, "0.0.0.0.0.0", StringComparison.Ordinal)
            || string.Equals(target, "local", StringComparison.OrdinalIgnoreCase))
            return new ActivationTarget(readLocalAt, IsLocal: true);

        if (!IsLiteralNetId(target)) return null;
        bool isLocal = string.Equals(target, LoopbackNetId, StringComparison.Ordinal)
                       || string.Equals(target, local.NetId, StringComparison.Ordinal);
        return new ActivationTarget(isLocal ? readLocalAt : target, isLocal);
    }

    internal static bool IsLiteralNetId(string? s) => AmsNetIdText.IsLiteral(s);

    public string Describe() => IsLocal ? $"{NetId} (local)" : NetId;
}

/// <summary>Time source of the activation wait: the real one sleeps, a test one only advances.</summary>
internal interface IActivationClock
{
    TimeSpan Elapsed { get; }

    void Sleep(TimeSpan duration);
}

internal sealed class StopwatchActivationClock : IActivationClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void Sleep(TimeSpan duration) => Thread.Sleep(duration);
}

/// <summary>What <see cref="ActivationWait.AwaitRun"/> observes and does. Injected, so the decision is testable.</summary>
/// <param name="ReadState">ADS ReadState of the target's system service. Should not throw; if it does, the read counts as failed.</param>
/// <param name="OpenModals">The attached XAE's modal windows right now (<see cref="ActivationWindows.OpenModals"/>).</param>
/// <param name="CloseOkBox">Presses OK on an OK-only box and reports whether it closed (<see cref="ActivationWindows.CloseOkBox"/>).</param>
/// <param name="WatcherProblem">Why the activation's dialog watcher could not complete the activation (licence prompt declined or
/// cancelled, login modal refused), or null.</param>
/// <param name="Clock">Elapsed time and sleeping.</param>
internal sealed record ActivationProbes(
    Func<TargetStateReading> ReadState,
    Func<IReadOnlyList<XaeDialog>> OpenModals,
    Func<XaeDialog, bool> CloseOkBox,
    Func<string?> WatcherProblem,
    IActivationClock Clock);

/// <summary>How <see cref="ActivationWait.AwaitRun"/> judges what it observes.</summary>
/// <param name="Deadline">How long to wait for Run (<see cref="ActivationWait.ClampDeadline"/>).</param>
/// <param name="AnsweredByWatcher">True for a dialog the activation's watcher answers itself (restart confirmation,
/// licence prompts, login modal). Such dialogs are not the outcome of the activation.</param>
/// <param name="OpenBeforeActivation">Modal windows that were already open before the activation. The activation
/// did not cause them.</param>
internal sealed record ActivationWaitOptions(
    TimeSpan Deadline,
    Func<XaeDialog, bool> AnsweredByWatcher,
    IReadOnlySet<IntPtr> OpenBeforeActivation)
{
    /// <summary>
    /// True when the target is already known to have left Run since the restart was triggered, or was not in Run
    /// before it. Without that, a Run read could still be the state from before the restart.
    /// </summary>
    public bool RestartObserved { get; init; }

    public TimeSpan PollInterval { get; init; } = ActivationWait.DefaultPollInterval;

    /// <summary>How long Run has to be read without a break before it counts.</summary>
    public TimeSpan RunStableFor { get; init; } = ActivationWait.DefaultRunStableFor;

    /// <summary>How long a modal window that is neither a message box nor a known error box must stay open to count.</summary>
    public TimeSpan WindowGrace { get; init; } = ActivationWait.DefaultWindowGrace;

    /// <summary>
    /// Ends the wait early (the restart call failed). The verdict is then <see cref="ActivationOutcomeKind.Cancelled"/>,
    /// unless a box is open that ends the wait anyway (the box that made the call fail).
    /// </summary>
    public CancellationToken Cancellation { get; init; }
}

internal enum ActivationOutcomeKind
{
    /// <summary>(a) The target's system state read Run, after the restart and stable.</summary>
    Run,

    /// <summary>(b) An XAE modal box appeared that the activation's watcher does not answer.</summary>
    Box,

    /// <summary>
    /// The target reached Run, but the activation's dialog watcher had to decline or cancel a dialog on the way
    /// (licence prompt, login modal): the activation is not what the call asked for, so it is no success.
    /// </summary>
    WatcherProblem,

    /// <summary>(c) The deadline passed without Run.</summary>
    Timeout,

    /// <summary>The wait was cancelled, because the restart call itself failed, and no box explains why.</summary>
    Cancelled,
}

/// <summary>
/// What the activation wait decided, and what it saw on the way. <paramref name="Problem"/> is the dialog watcher's
/// problem (declined or cancelled dialog), recorded whatever the outcome.
/// </summary>
internal sealed record ActivationVerdict(
    ActivationOutcomeKind Kind,
    TargetStateReading LastState,
    TimeSpan Waited,
    bool RestartObserved,
    string? BoxText = null,
    bool BoxOkOnly = false,
    bool BoxClosed = false,
    string? Problem = null,
    string? OpenDialog = null)
{
    public bool Success => Kind == ActivationOutcomeKind.Run;
}

/// <summary>
/// The truth of <c>twincat_activate_config(autoConfirmRestart)</c> (issue #195). After the restart has been
/// triggered, the wait ends with one of three outcomes:
/// <list type="number">
/// <item>(a) the target's system state reads Run: success, and the only success;</item>
/// <item>(b) an XAE modal box appears that the activation's dialog watcher does not answer. Its text is captured.
/// An OK-only box has nothing to decide and is closed (BM_CLICK on its OK button), so the XAE is never left modal
/// (#150). A question box is never answered blindly;</item>
/// <item>(c) the deadline passes. The last observed state is reported.</item>
/// </list>
/// A Run read only counts once the restart has been seen: either the target was not in Run when the wait began,
/// or it left Run afterwards. Otherwise the Run read could still be the old runtime. Run must also hold for
/// <see cref="ActivationWaitOptions.RunStableFor"/>.
/// A problem of the dialog watcher (a declined licence prompt, a refused login modal) does not end the wait: the
/// restart it follows can still fail into a box, which must be caught and closed. It is recorded, and it turns a
/// Run into <see cref="ActivationOutcomeKind.WatcherProblem"/>.
/// </summary>
internal static class ActivationWait
{
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(60);
    public const int MinDeadlineSeconds = 5;
    public const int MaxDeadlineSeconds = 300;
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan DefaultRunStableFor = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultWindowGrace = TimeSpan.FromSeconds(5);

    /// <summary>Box texts are reported, not dumped: this bounds a box that carries a long log.</summary>
    internal const int MaxBoxTextLength = 600;

    /// <summary>
    /// The deadline for <paramref name="seconds"/>: the default (60 s) when it is null or not positive,
    /// otherwise clamped to <see cref="MinDeadlineSeconds"/>..<see cref="MaxDeadlineSeconds"/>.
    /// </summary>
    public static TimeSpan ClampDeadline(int? seconds) =>
        seconds is null or <= 0
            ? DefaultDeadline
            : TimeSpan.FromSeconds(Math.Clamp(seconds.Value, MinDeadlineSeconds, MaxDeadlineSeconds));

    /// <summary>Waits for one of the outcomes described on <see cref="ActivationWait"/>. Never throws for a failing probe.</summary>
    public static ActivationVerdict AwaitRun(ActivationProbes probes, ActivationWaitOptions options)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(options);

        IActivationClock clock = probes.Clock;
        TimeSpan start = clock.Elapsed;
        bool restartObserved = options.RestartObserved;
        TimeSpan? runSince = null;
        var last = TargetStateReading.Unreachable("not read yet");
        var firstSeen = new Dictionary<IntPtr, TimeSpan>();
        string? problem = null;

        while (true)
        {
            TimeSpan now = clock.Elapsed - start;
            bool cancelled = options.Cancellation.IsCancellationRequested;

            // Recorded, not an end: after a declined licence prompt the restart can still fail into a box.
            if (!cancelled) problem ??= WatcherProblemOf(probes);

            // Cancelled because the restart call failed: one last look for the box that made it fail, so that box
            // is still reported (and an OK-only one closed) instead of being left for the next call to trip over.
            IReadOnlyList<XaeDialog> modals = ModalsOf(probes);
            XaeDialog? box = PickBox(modals, options, firstSeen, now);
            if (box is not null)
            {
                bool okOnly = IsOkOnly(box);
                bool closed = okOnly && Close(probes, box);
                return new(ActivationOutcomeKind.Box, last, now, restartObserved, DescribeBox(box), okOnly, closed, problem);
            }
            if (cancelled)
                return new(ActivationOutcomeKind.Cancelled, last, now, restartObserved, Problem: problem,
                    OpenDialog: modals.Count == 0 ? null : DescribeBox(modals[0]));

            last = ReadStateOf(probes);
            now = clock.Elapsed - start;
            if (!last.IsRun)
            {
                restartObserved = true;
                runSince = null;
            }
            else if (restartObserved)
            {
                runSince ??= now;
                if (now - runSince.Value >= options.RunStableFor)
                    return new(problem is null ? ActivationOutcomeKind.Run : ActivationOutcomeKind.WatcherProblem,
                        last, now, restartObserved, Problem: problem);
            }

            // A Run that is still proving itself may finish its stable window after the deadline.
            bool stabilising = runSince is not null && now < options.Deadline + options.RunStableFor;
            if (now >= options.Deadline && !stabilising)
                return new(ActivationOutcomeKind.Timeout, last, now, restartObserved, Problem: problem,
                    OpenDialog: modals.Count == 0 ? null : DescribeBox(modals[0]));

            clock.Sleep(options.PollInterval);
        }
    }

    /// <summary>
    /// The box that ends the wait, or null. Candidates are modal windows that were not open before the activation
    /// and that the watcher does not answer. A known target error box (any window class) and any message box count
    /// at once. Another modal window counts only once it has been open for <see cref="ActivationWaitOptions.WindowGrace"/>
    /// (a progress window of the restart closes by itself). <paramref name="firstSeen"/> carries when each window was
    /// first seen, from one poll to the next.
    /// </summary>
    internal static XaeDialog? PickBox(IReadOnlyList<XaeDialog> modals, ActivationWaitOptions options,
        Dictionary<IntPtr, TimeSpan> firstSeen, TimeSpan now)
    {
        var candidates = modals
            .Where(d => !options.OpenBeforeActivation.Contains(d.Hwnd) && !AnsweredByWatcher(options, d))
            .ToList();

        foreach (IntPtr gone in firstSeen.Keys.Where(h => candidates.TrueForAll(c => c.Hwnd != h)).ToList())
            firstSeen.Remove(gone);
        foreach (XaeDialog d in candidates)
            firstSeen.TryAdd(d.Hwnd, now);

        return candidates.Find(Win32DialogAutomation.IsTargetFatalErrorBox)
            ?? candidates.Find(d => d.IsMessageBox)
            ?? candidates.Find(d => now - firstSeen[d.Hwnd] >= options.WindowGrace);
    }

    /// <summary>
    /// True when the box's only button is OK: an error or information box with nothing to decide, so it is safe to
    /// close. A second button, or a check box (also a "Button" control), makes it a question.
    /// </summary>
    internal static bool IsOkOnly(XaeDialog d)
    {
        var buttons = d.Controls.Where(c => c.IsButton).ToList();
        return buttons.Count == 1 && string.Equals(buttons[0].Caption, "OK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"Title: text | text": the box's own texts without button captions, line breaks folded, bounded.</summary>
    internal static string DescribeBox(XaeDialog d)
    {
        string title = string.IsNullOrWhiteSpace(d.Title) ? $"<untitled {d.ClassName}>" : Fold(d.Title);
        var texts = d.Controls
            .Where(c => !c.IsButton)
            .Select(c => Fold(c.Text))
            .Where(t => t.Length > 0 && !string.Equals(t, title, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        string text = texts.Count == 0 ? title : $"{title}: {string.Join(" | ", texts)}";
        return text.Length <= MaxBoxTextLength ? text : string.Concat(text.AsSpan(0, MaxBoxTextLength), "…");
    }

    /// <summary>
    /// The activation result text for <paramref name="verdict"/>. A closed box reads
    /// "&lt;box text&gt; (the XAE box was closed). …"; the caller prefixes "[Error] Activation failed: ".
    /// The watcher's <see cref="ActivationVerdict.Problem"/> is not repeated here: the caller states it first.
    /// </summary>
    internal static string Describe(ActivationVerdict verdict, ActivationTarget target, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(target);
        string lastRead = $"Target {target.Describe()} last read: {verdict.LastState}.";
        return verdict.Kind switch
        {
            ActivationOutcomeKind.Run =>
                $"target {target.Describe()} reached Run {Seconds(verdict.Waited)} s after the restart was triggered " +
                $"(ADS ReadState, port {ActivationTarget.SystemServicePort})",
            ActivationOutcomeKind.Box when verdict.BoxClosed =>
                $"{verdict.BoxText} (the XAE box was closed). {lastRead}",
            ActivationOutcomeKind.Box when verdict.BoxOkOnly =>
                $"{verdict.BoxText} (the XAE box could NOT be closed: close it in the XAE). {lastRead}",
            ActivationOutcomeKind.Box =>
                $"{verdict.BoxText} (the XAE box is still open: it asks a question, which is never answered " +
                $"automatically. Answer it in the XAE). {lastRead}",
            ActivationOutcomeKind.WatcherProblem =>
                $"target {target.Describe()} reached Run {Seconds(verdict.Waited)} s after the restart was triggered, but " +
                "the dialog watcher had to decline or cancel a dialog on the way, so the configuration is not the one " +
                "that was asked for",
            ActivationOutcomeKind.Cancelled =>
                $"the wait for target {target.Describe()} was cancelled after {Seconds(verdict.Waited)} s. {lastRead}",
            _ => TimeoutText(verdict, target, deadline),
        };
    }

    private static string TimeoutText(ActivationVerdict verdict, ActivationTarget target, TimeSpan deadline)
    {
        string what = verdict.LastState.IsRun && !verdict.RestartObserved
            ? $"target {target.Describe()} kept reading Run but was never seen leaving it within {Seconds(deadline)} s " +
              "after the restart was triggered, so the restart into the new configuration is not confirmed. A target " +
              "that is already running may ignore the restart request (seen with a TwinCAT user-mode runtime) and " +
              "keeps its previous configuration: switch it to Config mode, then activate again"
            : $"target {target.Describe()} did not reach Run within {Seconds(deadline)} s after the restart was triggered " +
              $"(last state: {verdict.LastState})";
        return verdict.OpenDialog is null ? $"{what}." : $"{what}. XAE dialog still open: '{verdict.OpenDialog}'.";
    }

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Fold(string s) =>
        string.Join(" ", s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0));

    // ---- probes, contained: a failing probe never ends the wait with an exception ----------------

    private static TargetStateReading ReadStateOf(ActivationProbes probes)
    {
        try { return probes.ReadState() ?? TargetStateReading.Unreachable("no reading"); }
        catch (Exception ex) { return TargetStateReading.Unreachable(ex.Message); }
    }

    private static IReadOnlyList<XaeDialog> ModalsOf(ActivationProbes probes)
    {
        try { return probes.OpenModals() ?? []; }
        catch (Exception) { return []; } // a Win32 enumeration hiccup: no box seen this poll
    }

    private static bool Close(ActivationProbes probes, XaeDialog box)
    {
        try { return probes.CloseOkBox(box); }
        catch (Exception) { return false; } // reported as "could NOT be closed"
    }

    private static string? WatcherProblemOf(ActivationProbes probes)
    {
        try { return probes.WatcherProblem(); }
        catch (Exception) { return null; }
    }

    private static bool AnsweredByWatcher(ActivationWaitOptions options, XaeDialog d)
    {
        try { return options.AnsweredByWatcher(d); }
        catch (Exception) { return false; }
    }
}
