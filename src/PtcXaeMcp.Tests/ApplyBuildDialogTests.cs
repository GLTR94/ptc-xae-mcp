using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;
using PtcXaeMcp.WinForms.TwinCat.Apply;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #155 step 8: the XAE modals that block SolutionBuild.Build(true) during apply_schema
/// are answered by an explicit policy, every answer is reported, and an unknown modal fails the
/// build naming it instead of hanging. Dialog texts are the ones observed live (TC 4026.22).
/// </summary>
public class ApplyBuildDialogTests
{
    private static readonly XaeWindowScope NoScope = new(new HashSet<uint>(), IntPtr.Zero, 0);

    private static XaeDialog Box(int hwnd, string title, string text, params string[] buttons)
    {
        var controls = new List<XaeControl>();
        int id = hwnd * 10;
        foreach (string b in buttons) controls.Add(new XaeControl(id++, "Button", b));
        controls.Add(new XaeControl(id, "Static", text));
        return new XaeDialog(hwnd, 1, IntPtr.Zero, "#32770", title, controls);
    }

    private static XaeDialog SyncMaster(int hwnd = 1) =>
        Box(hwnd, "TcXaeShell", "Gerät 'DevD (EtherCAT)' benötigt Sync Master (mindestens eine Verknüpfung zu einer Task notwendig)", "OK");

    private static XaeDialog UnrestoredLinks(int hwnd = 2) =>
        Box(hwnd, "Nicht wiederhergestellte Variablenverknüpfungen gefunden",
            "Vernküpfungsinformationen für mögliche spätere Wiederherstellung behalten?",
            "Ja", "Nein", "Nicht erneut fragen");

    private static XaeDialog RestoreResult(int hwnd = 3) =>
        Box(hwnd, "TcXaeShell", "Restore variable links succeeded: 3 links not restored\nOpen 'Output Window' for details", "OK");

    private static XaeDialog DoubleUse(int hwnd = 4) =>
        Box(hwnd, "TcXaeShell", "Double use of the same Ethernet interface not allowed:\n'DevA (EtherCAT)'\n'DevB (EtherCAT)'", "OK");

    private static (ApplyBuildDialogWatcher Watcher, List<string> Pressed) Watcher()
    {
        var pressed = new List<string>();
        var watcher = new ApplyBuildDialogWatcher(NoScope, (d, plan) => pressed.Add($"{d.Hwnd}:{plan.Description}"), _ => true);
        return (watcher, pressed);
    }

    [Theory]
    [InlineData(1, "Sync Master info")]
    [InlineData(2, "Unrestored variable links (kept for a later restore)")]
    [InlineData(3, "Restore variable links result")]
    [InlineData(4, "Double use of the Ethernet interface")]
    public void Policy_ClassifiesTheObservedDialogs(int which, string expectedRule)
    {
        XaeDialog d = which switch { 1 => SyncMaster(), 2 => UnrestoredLinks(), 3 => RestoreResult(), _ => DoubleUse() };
        ApplyBuildDialogPolicy.Classify(d.Title, d.Texts.ToList())!.Name.Should().Be(expectedRule);
    }

    [Fact]
    public void KnownInfoBoxes_AreAnsweredWithOk_AndReported()
    {
        var (watcher, pressed) = Watcher();
        var now = DateTime.UtcNow;

        watcher.Tick([SyncMaster(), RestoreResult()], now);

        pressed.Should().Equal("1:BM_CLICK 'OK'", "3:BM_CLICK 'OK'");
        watcher.Answered.Should().HaveCount(2);
        watcher.Answered[0].Should().StartWith("Sync Master info: BM_CLICK 'OK'").And.Contain("Sync Master");
        watcher.Warnings.Should().ContainSingle().Which.Should().Contain("3 links not restored");
        watcher.Blocker.Should().BeNull();
    }

    [Fact]
    public void UnrestoredLinksQuestion_IsAnsweredJa_NeverNichtErneutFragen()
    {
        var (watcher, pressed) = Watcher();

        watcher.Tick([UnrestoredLinks()], DateTime.UtcNow);

        pressed.Should().Equal("2:BM_CLICK 'Ja'");
        watcher.Warnings.Should().ContainSingle().Which.Should().Contain("kept for a later restore");
    }

    [Fact]
    public void UnrestoredLinksQuestion_WithoutAJaButton_IsNotClickedBlindly()
    {
        var (watcher, pressed) = Watcher();
        var d = Box(5, "Nicht wiederhergestellte Variablenverknüpfungen gefunden", "Wiederherstellung behalten?", "Nein");
        var now = DateTime.UtcNow;

        watcher.Tick([d], now);
        watcher.Tick([d], now + ApplyBuildDialogWatcher.UnknownGrace);

        pressed.Should().BeEmpty();
        watcher.Blocker.Should().Contain("Nicht wiederhergestellte Variablenverkn");
    }

    [Fact]
    public void UnknownMessageBox_BecomesTheBlockerAfterTheGrace_AndIsNeverClicked()
    {
        var (watcher, pressed) = Watcher();
        var unknown = Box(9, "TcXaeShell", "Something unexpected happened. Continue?", "Ja", "Nein");
        var now = DateTime.UtcNow;

        watcher.Tick([unknown], now);
        watcher.Blocker.Should().BeNull();
        watcher.Tick([unknown], now + ApplyBuildDialogWatcher.UnknownGrace);

        pressed.Should().BeEmpty();
        watcher.Blocker.Should().Be("TcXaeShell: Something unexpected happened. Continue?");
    }

    [Fact]
    public void AnsweredDialogThatStaysOpen_BecomesTheBlocker()
    {
        var (watcher, pressed) = Watcher();
        var now = DateTime.UtcNow;

        watcher.Tick([SyncMaster()], now);
        watcher.Tick([SyncMaster()], now + ApplyBuildDialogWatcher.AnsweredGrace);

        pressed.Should().ContainSingle();
        watcher.Blocker.Should().Contain("still open after it was answered");
    }

    // ---- BuildCoordinator.BuildSolutionForApply ---------------------------------------

    private sealed class ScriptedWatcher : IApplyBuildDialogWatcher
    {
        public bool Started { get; private set; }
        public string? Blocker { get; set; }
        public List<string> AnsweredList { get; } = [];
        public IReadOnlyList<string> Answered => AnsweredList;
        public IReadOnlyList<string> Warnings => [];
        public void Start() => Started = true;
        public void Stop(TimeSpan tail) { }
        public void Dispose() { }
    }

    private static FakeTwinCatSession Session() => new() { IsConnected = true, SolutionOpen = true };

    [Fact]
    public void BuildForApply_UsesTheSessionBuild_AndReportsTheAnsweredDialogs()
    {
        var fake = Session();
        var watcher = new ScriptedWatcher();
        fake.BuildSolutionHook = () => watcher.AnsweredList.Add("Sync Master info: BM_CLICK 'OK'");

        ApplyBuildOutcome outcome = new BuildCoordinator(fake).BuildSolutionForApply(watcher, TimeSpan.FromSeconds(10), CancellationToken.None);

        outcome.Error.Should().BeNull();
        outcome.Result!.Success.Should().BeTrue();
        outcome.DialogsAnswered.Should().Equal("Sync Master info: BM_CLICK 'OK'");
        watcher.Started.Should().BeTrue();
        fake.CallLog.Should().Contain("BuildSolution()");
    }

    [Fact]
    public void BuildForApply_BlockedByAnUnknownDialog_FailsNamingIt_WithoutWaitingForTheBuild()
    {
        var fake = Session();
        using var release = new ManualResetEventSlim(false);
        fake.BuildSolutionHook = () => release.Wait(TimeSpan.FromSeconds(30));
        var watcher = new ScriptedWatcher { Blocker = "TcXaeShell: Something unexpected" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        ApplyBuildOutcome outcome = new BuildCoordinator(fake).BuildSolutionForApply(watcher, TimeSpan.FromMinutes(5), CancellationToken.None);
        release.Set();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        outcome.Result.Should().BeNull();
        outcome.Error.Should().StartWith("[Error]").And.Contain("'TcXaeShell: Something unexpected'");
    }

    [Fact]
    public void BuildForApply_Timeout_IsAnError()
    {
        var fake = Session();
        using var release = new ManualResetEventSlim(false);
        fake.BuildSolutionHook = () => release.Wait(TimeSpan.FromSeconds(30));

        ApplyBuildOutcome outcome = new BuildCoordinator(fake).BuildSolutionForApply(new ScriptedWatcher(), TimeSpan.FromMilliseconds(300), CancellationToken.None);
        release.Set();

        outcome.Error.Should().Contain("did not finish within");
    }

    [Fact]
    public void BuildForApply_NoSolution_DoesNotBuild()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };

        ApplyBuildOutcome outcome = new BuildCoordinator(fake).BuildSolutionForApply(new ScriptedWatcher(), TimeSpan.FromSeconds(5), CancellationToken.None);

        outcome.Error.Should().StartWith("[Error]");
        fake.CallLog.Should().NotContain("BuildSolution()");
    }

    [Fact]
    public void DescribeForApply_NamesTheFirstErrors()
    {
        var result = new BuildResult(false, 1,
            [new BuildError("error", "Double use of the same Ethernet interface", null, null), new BuildError("warning", "w", null, null)],
            CompilerSummary: "1 errors, 1 warnings");

        string text = BuildCoordinator.DescribeForApply(result);

        text.Should().StartWith("Build failed (failedProjects=1).").And.Contain("Compiler: 1 errors, 1 warnings")
            .And.Contain("[ERROR] Double use of the same Ethernet interface").And.NotContain("[ERROR] w");
    }
}
