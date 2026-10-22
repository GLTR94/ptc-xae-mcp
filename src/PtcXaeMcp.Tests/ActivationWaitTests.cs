using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #195: twincat_activate_config(autoConfirmRestart) succeeds only when the target's system state reads Run
/// after the restart. An XAE box ends the wait as an error with its text, and an OK-only box is closed. A deadline
/// without Run is an error with the last state. <see cref="ActivationWait.AwaitRun"/> runs here against a fake clock,
/// a scripted target and synthetic windows. No XAE, no runtime.
/// </summary>
public class ActivationWaitTests
{
    private const uint XaePid = 8768;
    private static readonly IntPtr XaeMain = (IntPtr)0x30236;
    private const string WinFormsWindow = "WindowsForms10.Window.8.app.0.be6c52_r3_ad1";
    private const string WinFormsButton = "WindowsForms10.BUTTON.app.0.be6c52_r3_ad1";

    private static readonly Win32DialogAutomation.WatcherConfig RestartOnly = new(AutoConfirmRestart: true, AutoGenerateLicenses: false);

    private static int _next = 0x5000;
    private static XaeControl Ctl(string cls, string text) => new((IntPtr)Interlocked.Increment(ref _next), cls, text);
    private static XaeControl Button(string text) => Ctl("Button", text);
    private static XaeControl Static(string text) => Ctl("Static", text);

    private static XaeDialog Box(string title, params XaeControl[] controls) =>
        new((IntPtr)Interlocked.Increment(ref _next), XaePid, XaeMain, "#32770", title, controls);

    private static XaeDialog Window(string title, params XaeControl[] controls) =>
        new((IntPtr)Interlocked.Increment(ref _next), XaePid, XaeMain, WinFormsWindow, title, controls);

    private static XaeDialog GermanFatalBox() =>
        Box("TcXaeShell", Static("Zielsystem meldet fatalen Fehler"), Button("OK"));

    private static XaeDialog EnglishFatalBox() =>
        Box("TwinCAT XAE", Static("Target system reports fatal error"), Button("OK"));

    private static XaeDialog RestartConfirmation() =>
        Box("TwinCAT XAE", Static("Neustart TwinCAT System im Run Modus"), Button("OK"), Button("Abbrechen"));

    private static readonly TimeSpan S1 = TimeSpan.FromSeconds(1);

    private static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

    private sealed class FakeClock : IActivationClock
    {
        public TimeSpan Elapsed { get; private set; }

        public void Sleep(TimeSpan duration) => Elapsed += duration;
    }

    /// <summary>A target whose state is a function of (fake) time, windows that open and close, a watcher.</summary>
    private sealed class Harness
    {
        public FakeClock Clock { get; } = new();

        /// <summary>(until, state): the state read before time "until"; the last entry holds for ever.</summary>
        public List<(double Until, TargetStateReading State)> Target { get; } = [];

        public List<(double From, double To, XaeDialog Window)> Windows { get; } = [];
        public bool CloseSucceeds { get; set; } = true;
        public List<XaeDialog> CloseCalls { get; } = [];
        public (double From, string Text)? Problem { get; set; }
        public int Reads { get; private set; }

        public Harness States(params (double Until, string State)[] states)
        {
            foreach (var (until, state) in states)
                Target.Add((until, state.StartsWith('!')
                    ? TargetStateReading.Unreachable(state[1..])
                    : TargetStateReading.Of(state)));
            return this;
        }

        public Harness Open(XaeDialog window, double from, double to = double.MaxValue)
        {
            Windows.Add((from, to, window));
            return this;
        }

        public ActivationProbes Probes => new(
            ReadState: () =>
            {
                Reads++;
                double now = Clock.Elapsed.TotalSeconds;
                return Target.Count == 0
                    ? TargetStateReading.Of("Run")
                    : Target.FirstOrDefault(t => now < t.Until, Target[^1]).State;
            },
            OpenModals: () =>
            {
                double now = Clock.Elapsed.TotalSeconds;
                return Windows
                    .Where(w => now >= w.From && now < w.To && !(CloseSucceeds && CloseCalls.Contains(w.Window)))
                    .Select(w => w.Window)
                    .ToList();
            },
            CloseOkBox: box =>
            {
                CloseCalls.Add(box);
                return CloseSucceeds;
            },
            WatcherProblem: () => Problem is { } p && Clock.Elapsed.TotalSeconds >= p.From ? p.Text : null,
            Clock: Clock);

        public ActivationVerdict Run(Win32DialogAutomation.WatcherConfig? watcher = null,
            IReadOnlySet<IntPtr>? openBefore = null, double deadline = 30,
            Func<ActivationWaitOptions, ActivationWaitOptions>? tweak = null)
        {
            var cfg = watcher ?? RestartOnly;
            var options = new ActivationWaitOptions(Sec(deadline), d => Win32DialogAutomation.Answers(cfg, d),
                openBefore ?? new HashSet<IntPtr>());
            return ActivationWait.AwaitRun(Probes, tweak is null ? options : tweak(options));
        }
    }

    // ---- (a) Run --------------------------------------------------------------------------------

    [Fact]
    public void Run_AfterTheTargetLeftRun_IsTheOnlySuccess()
    {
        // Old runtime in Run, the restart takes it down at 2 s, back in Run at 6 s.
        var h = new Harness().States((2, "Run"), (4, "Reset"), (6, "Config"), (99, "Run"));

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.True(v.Success);
        Assert.True(v.RestartObserved);
        // Not before the target came back (6 s) and held Run for 1 s.
        Assert.InRange(v.Waited.TotalSeconds, 7.0, 7.5);
    }

    [Fact]
    public void Run_ReadBeforeTheRestartWasSeen_DoesNotCount()
    {
        // The old Run never goes away: the restart is not confirmed, whatever the state says.
        var h = new Harness().States((99, "Run"));

        var v = h.Run(deadline: 10);

        Assert.Equal(ActivationOutcomeKind.Timeout, v.Kind);
        Assert.False(v.Success);
        Assert.False(v.RestartObserved);
        Assert.True(v.LastState.IsRun);
        Assert.True(h.Reads > 30, "the target is polled until the deadline");
    }

    [Fact]
    public void Run_WhenTheTargetWasNotInRun_CountsOnceStable()
    {
        var h = new Harness().States((3, "Config"), (99, "Run"));

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.InRange(v.Waited.TotalSeconds, 4.0, 4.5);
    }

    [Fact]
    public void Run_RestartSeenBeforeTheWait_CountsTheFirstStableRun()
    {
        var h = new Harness().States((99, "Run"));

        var v = h.Run(tweak: o => o with { RestartObserved = true });

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.InRange(v.Waited.TotalSeconds, 1.0, 1.5);
    }

    [Fact]
    public void Run_ThatFlapsBackOut_RestartsTheStableWindow()
    {
        // Run for half a second, out again, then Run for good at 5 s.
        var h = new Harness().States((1, "Config"), (1.5, "Run"), (5, "Config"), (99, "Run"));

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.InRange(v.Waited.TotalSeconds, 6.0, 6.5);
    }

    [Fact]
    public void Run_StartedJustBeforeTheDeadline_MayFinishItsStableWindow()
    {
        var h = new Harness().States((9.9, "Config"), (99, "Run"));

        var v = h.Run(deadline: 10);

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.InRange(v.Waited.TotalSeconds, 10.9, 11.2);
    }

    [Fact]
    public void Run_UnreachableTargetCountsAsLeavingRun()
    {
        var h = new Harness().States((2, "Run"), (4, "!ADS error TargetMachineNotFound"), (99, "Run"));

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.True(v.RestartObserved);
    }

    // ---- (b) a box ------------------------------------------------------------------------------

    [Fact]
    public void GermanFatalErrorBox_OkOnly_IsClosedAndEndsTheWait()
    {
        var box = GermanFatalBox();
        var h = new Harness().States((2, "Run"), (99, "Config")).Open(box, from: 7);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.False(v.Success);
        Assert.True(v.BoxOkOnly);
        Assert.True(v.BoxClosed);
        Assert.Equal(box.Hwnd, Assert.Single(h.CloseCalls).Hwnd);
        Assert.Equal("TcXaeShell: Zielsystem meldet fatalen Fehler", v.BoxText);
        Assert.InRange(v.Waited.TotalSeconds, 7.0, 7.3);
        Assert.Equal("Config", v.LastState.State);
    }

    [Fact]
    public void EnglishFatalErrorBox_OkOnly_IsClosedAndEndsTheWait()
    {
        var h = new Harness().States((99, "Config")).Open(EnglishFatalBox(), from: 3);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.True(v.BoxClosed);
        Assert.Contains("Target system reports fatal error", v.BoxText, StringComparison.Ordinal);
    }

    [Fact]
    public void FatalErrorWindow_OfAnyClass_EndsTheWaitAtOnce()
    {
        // Not a #32770: without the matcher it would have to stay up for the grace period first.
        var window = Window("Zielsystem meldet fatalen Fehler", Ctl(WinFormsButton, "OK"));
        var h = new Harness().States((99, "Config")).Open(window, from: 2);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.InRange(v.Waited.TotalSeconds, 2.0, 2.3);
        Assert.True(v.BoxClosed);
    }

    [Fact]
    public void UnknownOkOnlyMessageBox_IsClosedToo()
    {
        var box = Box("TwinCAT XAE", Static("Unexpected target answer 0x745"), Button("OK"));
        var h = new Harness().States((99, "Config")).Open(box, from: 1);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.True(v.BoxClosed);
        Assert.Equal("TwinCAT XAE: Unexpected target answer 0x745", v.BoxText);
    }

    [Fact]
    public void QuestionBox_IsNeverClicked_AndReportedAsStillOpen()
    {
        var box = Box("TwinCAT XAE", Static("Konfiguration im Konfig-Modus neu starten?"), Button("Ja"), Button("Nein"));
        var h = new Harness().States((99, "Config")).Open(box, from: 2);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.False(v.BoxOkOnly);
        Assert.False(v.BoxClosed);
        Assert.Empty(h.CloseCalls);
    }

    [Fact]
    public void OkBoxWithACheckBox_IsAQuestion_AndNotClicked()
    {
        var box = Box("TwinCAT XAE", Static("Zielsystem meldet fatalen Fehler"), Button("OK"), Button("Nicht erneut anzeigen"));
        var h = new Harness().States((99, "Config")).Open(box, from: 2);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.False(v.BoxOkOnly);
        Assert.Empty(h.CloseCalls);
    }

    [Fact]
    public void OkOnlyBox_ThatDoesNotClose_IsReportedAsNotClosed()
    {
        var h = new Harness { CloseSucceeds = false }.States((99, "Config")).Open(GermanFatalBox(), from: 1);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.True(v.BoxOkOnly);
        Assert.False(v.BoxClosed);
        Assert.Single(h.CloseCalls);
    }

    [Fact]
    public void Box_WinsOverARunReadInTheSamePoll()
    {
        var h = new Harness().States((99, "Run")).Open(GermanFatalBox(), from: 0);

        var v = h.Run(tweak: o => o with { RestartObserved = true });

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
    }

    [Fact]
    public void Box_DuringTheStableWindow_StillFailsTheActivation()
    {
        var h = new Harness().States((2, "Config"), (99, "Run")).Open(EnglishFatalBox(), from: 2.6);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
    }

    [Fact]
    public void DialogTheWatcherAnswers_IsNotTheOutcome()
    {
        // The restart confirmation is the watcher's (autoConfirmRestart); it stays up for a moment while it closes.
        var h = new Harness().States((1, "Run"), (3, "Reset"), (99, "Run")).Open(RestartConfirmation(), from: 0, to: 2);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.Empty(h.CloseCalls);
    }

    [Fact]
    public void RestartConfirmation_IsABox_WhenTheWatcherDoesNotAnswerIt()
    {
        var noRestart = new Win32DialogAutomation.WatcherConfig(AutoConfirmRestart: false, AutoGenerateLicenses: false);
        var h = new Harness().States((99, "Config")).Open(RestartConfirmation(), from: 1);

        var v = h.Run(noRestart);

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.False(v.BoxOkOnly);
    }

    [Fact]
    public void ModalOpenBeforeTheActivation_IsNotBlamedOnIt()
    {
        var old = Box("TwinCAT XAE", Static("Hinweis"), Button("OK"));
        var h = new Harness().States((1, "Config"), (99, "Run")).Open(old, from: 0);

        var v = h.Run(openBefore: new HashSet<IntPtr> { old.Hwnd });

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
        Assert.Empty(h.CloseCalls);
    }

    [Fact]
    public void OtherModalWindow_ThatClosesWithinTheGrace_IsIgnored()
    {
        // A progress window of the restart: not a message box, gone after 3 s.
        var progress = Window("TwinCAT System wird gestartet", Ctl(WinFormsButton, "Abbrechen"));
        var h = new Harness().States((1, "Run"), (4, "Init"), (99, "Run")).Open(progress, from: 1, to: 4);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Run, v.Kind);
    }

    [Fact]
    public void OtherModalWindow_ThatStaysUp_CountsAfterTheGrace()
    {
        var modal = Window("TwinCAT PLC Control", Ctl(WinFormsButton, "OK"), Ctl(WinFormsButton, "Abbrechen"));
        var h = new Harness().States((99, "Config")).Open(modal, from: 1);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.InRange(v.Waited.TotalSeconds, 6.0, 6.3);
        Assert.False(v.BoxClosed);
    }

    // ---- watcher problem, (c) deadline, cancellation ---------------------------------------------

    [Fact]
    public void WatcherProblem_DoesNotEndTheWait_TheFatalBoxThatFollowsIsStillClosed()
    {
        // Live-shaped: a declined licence prompt, then the restart fails into "license not found".
        var h = new Harness { Problem = (2, "XAE asked to generate trial licences; declined.") }
            .States((99, "Config")).Open(GermanFatalBox(), from: 7);

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.True(v.BoxClosed);
        Assert.Equal("XAE asked to generate trial licences; declined.", v.Problem);
        Assert.InRange(v.Waited.TotalSeconds, 7.0, 7.3);
    }

    [Fact]
    public void WatcherProblem_ThenRun_IsNoSuccess()
    {
        var h = new Harness { Problem = (1, "login modal refused") }.States((2, "Config"), (99, "Run"));

        var v = h.Run();

        Assert.Equal(ActivationOutcomeKind.WatcherProblem, v.Kind);
        Assert.False(v.Success);
        Assert.Equal("login modal refused", v.Problem);
        Assert.True(v.LastState.IsRun);
    }

    [Fact]
    public void WatcherProblem_ThenDeadline_CarriesTheProblem()
    {
        var h = new Harness { Problem = (1, "XAE asked to generate trial licences; declined.") }.States((99, "Config"));

        var v = h.Run(deadline: 6);

        Assert.Equal(ActivationOutcomeKind.Timeout, v.Kind);
        Assert.Equal("XAE asked to generate trial licences; declined.", v.Problem);
    }

    [Fact]
    public void Describe_RunWithAWatcherProblem_SaysItIsNotTheRequestedConfiguration()
    {
        var v = new ActivationVerdict(ActivationOutcomeKind.WatcherProblem, TargetStateReading.Of("Run"), Sec(4), true,
            Problem: "login modal refused");

        string text = ActivationWait.Describe(v, Umr, Sec(30));

        Assert.StartsWith("target 192.168.250.20.1.1 reached Run 4.0 s after the restart was triggered, but", text, StringComparison.Ordinal);
        Assert.Contains("not the one that was asked for", text, StringComparison.Ordinal);
        Assert.DoesNotContain("login modal refused", text, StringComparison.Ordinal); // the caller states the problem itself
    }

    [Fact]
    public void Deadline_WithoutRun_ReportsTheLastState()
    {
        var h = new Harness().States((2, "Run"), (99, "Config"));

        var v = h.Run(deadline: 12);

        Assert.Equal(ActivationOutcomeKind.Timeout, v.Kind);
        Assert.Equal("Config", v.LastState.State);
        Assert.True(v.RestartObserved);
        Assert.InRange(v.Waited.TotalSeconds, 12.0, 12.3);
        Assert.Null(v.OpenDialog);
    }

    [Fact]
    public void Deadline_UnreachableTarget_ReportsTheAdsError()
    {
        var h = new Harness().States((99, "!ADS error TargetPortNotFound"));

        var v = h.Run(deadline: 5);

        Assert.Equal(ActivationOutcomeKind.Timeout, v.Kind);
        Assert.Equal("no ADS state (ADS error TargetPortNotFound)", v.LastState.ToString());
    }

    [Fact]
    public void Deadline_NamesAnXaeDialogThatIsStillOpen()
    {
        // An answered restart confirmation that never closed: not the outcome, but worth naming.
        var h = new Harness().States((99, "Config")).Open(RestartConfirmation(), from: 0);

        var v = h.Run(deadline: 5);

        Assert.Equal(ActivationOutcomeKind.Timeout, v.Kind);
        Assert.Equal("TwinCAT XAE: Neustart TwinCAT System im Run Modus", v.OpenDialog);
    }

    [Fact]
    public void Cancellation_EndsTheWait()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new Harness().States((99, "Config"));

        var v = h.Run(tweak: o => o with { Cancellation = cts.Token });

        Assert.Equal(ActivationOutcomeKind.Cancelled, v.Kind);
        Assert.Equal(0, h.Reads);
    }

    [Fact]
    public void Cancellation_StillReportsAndClosesTheBoxThatMadeTheRestartCallFail()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new Harness().States((99, "Config")).Open(GermanFatalBox(), from: 0);

        var v = h.Run(tweak: o => o with { Cancellation = cts.Token });

        Assert.Equal(ActivationOutcomeKind.Box, v.Kind);
        Assert.True(v.BoxClosed);
        Assert.Single(h.CloseCalls);
    }

    [Fact]
    public void Cancellation_NamesAnOpenDialogItDoesNotCount()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new Harness().States((99, "Config")).Open(RestartConfirmation(), from: 0);

        var v = h.Run(tweak: o => o with { Cancellation = cts.Token });

        Assert.Equal(ActivationOutcomeKind.Cancelled, v.Kind);
        Assert.Equal("TwinCAT XAE: Neustart TwinCAT System im Run Modus", v.OpenDialog);
    }

    [Fact]
    public void FailingProbes_AreContained()
    {
        var clock = new FakeClock();
        var probes = new ActivationProbes(
            () => throw new InvalidOperationException("router down"),
            () => throw new InvalidOperationException("EnumWindows failed"),
            _ => throw new InvalidOperationException("not reached"),
            () => throw new InvalidOperationException("watcher gone"),
            clock);
        var options = new ActivationWaitOptions(Sec(5), _ => throw new InvalidOperationException("matcher"), new HashSet<IntPtr>());

        var v = ActivationWait.AwaitRun(probes, options);

        Assert.Equal(ActivationOutcomeKind.Timeout, v.Kind);
        Assert.Equal("no ADS state (router down)", v.LastState.ToString());
    }

    [Fact]
    public void PollsAtTheConfiguredInterval()
    {
        var h = new Harness().States((99, "Config"));

        h.Run(deadline: 10, tweak: o => o with { PollInterval = S1 });

        Assert.Equal(11, h.Reads); // t = 0, 1, …, 10
    }

    // ---- result text --------------------------------------------------------------------------------

    private static readonly ActivationTarget Umr = new("192.168.250.20.1.1", IsLocal: false);

    [Fact]
    public void Describe_ClosedBox_ReadsAsTheIssueAsks()
    {
        var v = new ActivationVerdict(ActivationOutcomeKind.Box, TargetStateReading.Of("Config"), Sec(7), true,
            BoxText: "TcXaeShell: Zielsystem meldet fatalen Fehler", BoxOkOnly: true, BoxClosed: true);

        string text = ActivationWait.Describe(v, Umr, Sec(30));

        Assert.StartsWith("TcXaeShell: Zielsystem meldet fatalen Fehler (the XAE box was closed).", text, StringComparison.Ordinal);
        Assert.Contains("192.168.250.20.1.1 last read: Config", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_BoxThatCouldNotBeClosed_SaysSo()
    {
        var v = new ActivationVerdict(ActivationOutcomeKind.Box, TargetStateReading.Of("Config"), Sec(7), true,
            BoxText: "TcXaeShell: Zielsystem meldet fatalen Fehler", BoxOkOnly: true, BoxClosed: false);

        Assert.Contains("(the XAE box could NOT be closed", ActivationWait.Describe(v, Umr, Sec(30)), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_QuestionBox_SaysItIsStillOpen()
    {
        var v = new ActivationVerdict(ActivationOutcomeKind.Box, TargetStateReading.Of("Config"), Sec(7), true,
            BoxText: "TwinCAT XAE: Neu starten?");

        string text = ActivationWait.Describe(v, Umr, Sec(30));

        Assert.StartsWith("TwinCAT XAE: Neu starten? (the XAE box is still open", text, StringComparison.Ordinal);
        Assert.DoesNotContain("was closed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Run_NamesTheTargetAndTheTime()
    {
        var v = new ActivationVerdict(ActivationOutcomeKind.Run, TargetStateReading.Of("Run"), Sec(6.2), true);

        Assert.Equal("target 192.168.250.20.1.1 reached Run 6.2 s after the restart was triggered (ADS ReadState, port 10000)",
            ActivationWait.Describe(v, Umr, Sec(30)));
    }

    [Fact]
    public void Describe_Timeout_GivesTheLastStateAndAnOpenDialog()
    {
        var v = new ActivationVerdict(ActivationOutcomeKind.Timeout, TargetStateReading.Of("Config"), Sec(30), true,
            OpenDialog: "TwinCAT XAE: Neustart TwinCAT System im Run Modus");

        Assert.Equal("target 192.168.250.20.1.1 did not reach Run within 30.0 s after the restart was triggered " +
                     "(last state: Config). XAE dialog still open: 'TwinCAT XAE: Neustart TwinCAT System im Run Modus'.",
            ActivationWait.Describe(v, Umr, Sec(30)));
    }

    [Fact]
    public void Describe_TimeoutInTheOldRun_SaysTheRestartIsNotConfirmed()
    {
        var v = new ActivationVerdict(ActivationOutcomeKind.Timeout, TargetStateReading.Of("Run"), Sec(30), RestartObserved: false);

        string text = ActivationWait.Describe(v, new ActivationTarget("10.1.2.3.1.1", IsLocal: true), Sec(30));

        Assert.Contains("10.1.2.3.1.1 (local) kept reading Run but was never seen leaving it", text, StringComparison.Ordinal);
        Assert.Contains("not confirmed", text, StringComparison.Ordinal);
        // Live 2026-09-27: a running user-mode runtime ignored StartRestartTwinCAT(); the text says what to do.
        Assert.Contains("switch it to Config mode, then activate again", text, StringComparison.Ordinal);
    }

    // ---- boxes: OK-only and text ----------------------------------------------------------------

    [Fact]
    public void IsOkOnly_SingleOkButton()
    {
        Assert.True(ActivationWait.IsOkOnly(GermanFatalBox()));
        Assert.True(ActivationWait.IsOkOnly(Box("x", Static("t"), Button("&OK"))));
        Assert.True(ActivationWait.IsOkOnly(Window("x", Ctl(WinFormsButton, "OK"))));
    }

    [Fact]
    public void IsOkOnly_AnythingElse_IsAQuestion()
    {
        Assert.False(ActivationWait.IsOkOnly(RestartConfirmation()));
        Assert.False(ActivationWait.IsOkOnly(Box("x", Static("t"))));
        Assert.False(ActivationWait.IsOkOnly(Box("x", Static("t"), Button("Ja"))));
        Assert.False(ActivationWait.IsOkOnly(Box("x", Static("t"), Button("OK"), Button("Hilfe"))));
    }

    [Fact]
    public void DescribeBox_FoldsLinesAndDropsButtonsAndTheRepeatedTitle()
    {
        var box = Box("TcXaeShell", Static("TcXaeShell"), Static("Zielsystem meldet\r\nfatalen Fehler"),
            Static("  "), Static("'TwinCAT System' (10000): Error"), Button("OK"));

        Assert.Equal("TcXaeShell: Zielsystem meldet fatalen Fehler | 'TwinCAT System' (10000): Error",
            ActivationWait.DescribeBox(box));
    }

    [Fact]
    public void DescribeBox_UntitledAndTextless_NamesTheClass()
    {
        Assert.Equal("<untitled #32770>", ActivationWait.DescribeBox(Box("", Button("OK"))));
    }

    [Fact]
    public void DescribeBox_IsBounded()
    {
        string text = ActivationWait.DescribeBox(Box("T", Static(new string('x', 5000)), Button("OK")));

        Assert.Equal(ActivationWait.MaxBoxTextLength + 1, text.Length);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }

    // ---- matchers: fatal-error box, and what the watcher answers ---------------------------------------

    [Theory]
    [InlineData("Zielsystem meldet fatalen Fehler")]
    [InlineData("Das Zielsystem meldet fatalen Fehler!")]
    [InlineData("Target system reports fatal error")]
    [InlineData("target reports fatal error")]
    [InlineData("FATAL ERROR")]
    public void FatalErrorText_GermanAndEnglish(string text)
    {
        Assert.True(Win32DialogAutomation.IsTargetFatalErrorText(text));
        Assert.True(Win32DialogAutomation.IsTargetFatalErrorBox(Box("TwinCAT XAE", Static(text), Button("OK"))));
        Assert.True(Win32DialogAutomation.IsTargetFatalErrorBox(Box(text, Button("OK"))));
    }

    [Theory]
    [InlineData("Neustart TwinCAT System im Run Modus")]
    [InlineData("Einige benötigte Laufzeit-Lizenzen fehlen. Sollen Testlizenzen erzeugt werden?")]
    [InlineData("Fehler beim Speichern")]
    [InlineData("")]
    public void FatalErrorText_OtherTexts_DoNotMatch(string text)
    {
        Assert.False(Win32DialogAutomation.IsTargetFatalErrorText(text));
    }

    [Fact]
    public void FatalErrorBox_MentioningARestart_IsNotARestartConfirmation()
    {
        // Pressing its OK as "restart confirmation" would hide the failure.
        var box = Box("TcXaeShell", Static("Zielsystem meldet fatalen Fehler. Neustart erforderlich."), Button("OK"));

        Assert.False(Win32DialogAutomation.IsRestartDialog(box));
        Assert.False(Win32DialogAutomation.Answers(RestartOnly, box));
    }

    [Fact]
    public void FatalErrorBox_MentioningLicences_IsNotALicenceDialog()
    {
        var trial = Box("TcXaeShell", Static("Zielsystem meldet fatalen Fehler: Testlizenz abgelaufen"), Button("OK"));
        var file = Box("TcXaeShell", Static("Target system reports fatal error: license file missing"), Button("OK"));

        Assert.False(Win32DialogAutomation.IsTrialLicensePrompt(trial));
        Assert.False(Win32DialogAutomation.IsLicenseCreatedDialog(file));
        var all = new Win32DialogAutomation.WatcherConfig(true, true, AutoConfirmLogin: true);
        Assert.False(Win32DialogAutomation.Answers(all, trial));
        Assert.False(Win32DialogAutomation.Answers(all, file));
    }

    [Fact]
    public void RestartConfirmation_StillMatches()
    {
        Assert.True(Win32DialogAutomation.IsRestartDialog(RestartConfirmation()));
    }

    [Fact]
    public void Answers_FollowsTheWatcherConfiguration()
    {
        var none = new Win32DialogAutomation.WatcherConfig(false, false);
        var licences = new Win32DialogAutomation.WatcherConfig(false, true);
        var login = new Win32DialogAutomation.WatcherConfig(false, false, AutoConfirmLogin: true);

        var trialPrompt = Box("TwinCAT XAE", Static("Einige benötigte Laufzeit-Lizenzen fehlen. Sollen Testlizenzen erzeugt werden?"),
            Button("Ja"), Button("Nein"));
        var securityCode = Box("Sicherheitscode eingeben", Static("Code:"), Button("OK"));
        var created = Box("TwinCAT XAE", Static("7 Tages Lizenzdatei erzeugt"), Button("OK"));
        var loginModal = Window("TwinCAT PLC Control",
            Static("Die Applikation wurde seit dem letzten Download geändert. Was möchten Sie tun?"), Ctl(WinFormsButton, "OK"));

        Assert.True(Win32DialogAutomation.Answers(RestartOnly, RestartConfirmation()));
        Assert.False(Win32DialogAutomation.Answers(none, RestartConfirmation()));

        // The trial-licence prompt is always answered: "Ja" when asked to, else "Nein".
        Assert.True(Win32DialogAutomation.Answers(none, trialPrompt));

        Assert.True(Win32DialogAutomation.Answers(licences, securityCode));
        Assert.True(Win32DialogAutomation.Answers(licences, created));
        Assert.False(Win32DialogAutomation.Answers(none, securityCode));
        Assert.False(Win32DialogAutomation.Answers(none, created));

        Assert.True(Win32DialogAutomation.Answers(login, loginModal));
        Assert.False(Win32DialogAutomation.Answers(none, loginModal));

        Assert.False(Win32DialogAutomation.Answers(RestartOnly, GermanFatalBox()));
    }

    // ---- target NetId, deadline, restart -------------------------------------------------------------

    // Issue #202: what LocalAmsNetIdResolver gives when the local AMS router answers.
    private static readonly LocalAmsNetId RouterLocal = new("10.1.2.3.1.1", LocalNetIdSource.Router);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.0.0.0.0.0")]
    [InlineData("local")]
    public void Target_Local_IsReadAtTheLocalRoutersNetId(string? projectTarget)
    {
        // Not the registry AmsNetId: on the dev VM it was stale and ReadState on it timed out (live, 2026-09-27).
        var target = ActivationTarget.Resolve(projectTarget, RouterLocal);

        Assert.Equal(new ActivationTarget("10.1.2.3.1.1", IsLocal: true), target);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Target_Local_WithoutARouterAnswer_IsReadThroughTheLoopback(bool registryFallback)
    {
        // The registry's NetId can be stale; the loopback is the local system to a router that comes back.
        var local = registryFallback
            ? new LocalAmsNetId("172.16.0.9.1.1", LocalNetIdSource.Registry, RouterError: "no router")
            : new LocalAmsNetId(LocalAmsNetId.LoopbackNetId, LocalNetIdSource.Loopback, RouterError: "no router");

        Assert.Equal(new ActivationTarget(ActivationTarget.LoopbackNetId, IsLocal: true),
            ActivationTarget.Resolve(null, local));
    }

    [Fact]
    public void Target_Remote_IsTheProjectTarget()
    {
        Assert.Equal(new ActivationTarget("192.168.250.20.1.1", IsLocal: false),
            ActivationTarget.Resolve(" 192.168.250.20.1.1 ", RouterLocal));
    }

    [Fact]
    public void Target_LoopbackOrTheRoutersNetId_IsLocal_AndReadAtTheRoutersNetId()
    {
        Assert.Equal(new ActivationTarget("10.1.2.3.1.1", IsLocal: true),
            ActivationTarget.Resolve("127.0.0.1.1.1", RouterLocal));
        Assert.Equal(new ActivationTarget("10.1.2.3.1.1", IsLocal: true),
            ActivationTarget.Resolve("10.1.2.3.1.1", RouterLocal));
    }

    [Fact]
    public void Target_TheRegistrysNetId_WhenTheRouterDidNotAnswer_IsLocal_AndReadThroughTheLoopback()
    {
        var registry = new LocalAmsNetId("172.16.0.9.1.1", LocalNetIdSource.Registry, RouterError: "no router");

        Assert.Equal(new ActivationTarget(ActivationTarget.LoopbackNetId, IsLocal: true),
            ActivationTarget.Resolve("172.16.0.9.1.1", registry));
    }

    [Fact]
    public void Target_AStaleRegistryNetId_IsNotTheLocalSystem_WhileTheRouterAnswers()
    {
        // The resolver gave the router's NetId and ignored the registry's, so a project target that only matches
        // the registry is read at itself: its ReadState timing out is the truth about that target.
        Assert.Equal(new ActivationTarget("172.16.0.9.1.1", IsLocal: false),
            ActivationTarget.Resolve("172.16.0.9.1.1", RouterLocal));
    }

    [Fact]
    public void Target_NotAnAmsNetId_CannotBeConfirmed()
    {
        Assert.Null(ActivationTarget.Resolve("CX-12345", RouterLocal));
        Assert.Null(ActivationTarget.Resolve("1.2.3.4", RouterLocal));
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(0, 60)]
    [InlineData(-4, 60)]
    [InlineData(1, 5)]
    [InlineData(45, 45)]
    [InlineData(500, 300)]
    public void ClampDeadline_DefaultsAndBounds(int? seconds, int expected)
    {
        Assert.Equal(Sec(expected), ActivationWait.ClampDeadline(seconds));
    }

    [Fact]
    public void Reading_RunIsCaseInsensitive_AndAFailedReadIsNeverRun()
    {
        Assert.True(TargetStateReading.Of("RUN").IsRun);
        Assert.False(TargetStateReading.Of("Config").IsRun);
        Assert.False(new TargetStateReading("Run", "stale").IsRun);
        Assert.Equal("Config", TargetStateReading.Of("Config").ToString());
    }
}
