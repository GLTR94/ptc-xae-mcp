using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// The XAE's "Sollen die Änderungen … gespeichert werden?" prompt raised by Solution.Close (reported live,
/// German UI, WPF window): matched by question + save/don't-save buttons, answered "Speichern" with saveFirst
/// and "Nicht speichern" without, verified closed, cancelled when the answer does not close it — only in the
/// XAE's scope.
/// </summary>
public class SaveChangesDialogTests
{
    private const uint XaePid = 8768;
    private const uint HostPid = 4304;
    private static readonly IntPtr XaeMain = (IntPtr)0x30236;
    private static readonly XaeWindowScope Scope = new(new HashSet<uint> { XaePid }, XaeMain, HostPid);

    private const string WpfClass = "HwndWrapper[TcXaeShell.exe;;0b1e7a5c-1111-2222-3333-444455556666]";
    private const string GermanQuestion = "Sollen die Änderungen an den folgenden Elementen gespeichert werden?";

    private static int _next = 0x4000;

    private static PromptWindow Prompt(string title = "TcXaeShell", string question = GermanQuestion,
        uint pid = XaePid, IntPtr? owner = null, string cls = WpfClass, params string[] buttons)
    {
        if (buttons.Length == 0) buttons = ["_Speichern", "_Nicht speichern", "Abbrechen"];
        return new PromptWindow((IntPtr)Interlocked.Increment(ref _next), pid, owner ?? XaeMain, cls, title,
            question.Length == 0 ? [] : [question, @"PtcXaeMcpFixture\PtcXaeMcpFixture.tsproj"],
            buttons.Select(b => new PromptButton(b)).ToList());
    }

    // ---- matcher ------------------------------------------------------------------

    [Fact]
    public void Matches_TheReportedGermanWpfPrompt() => Assert.True(SaveChangesDialog.Matches(Prompt()));

    [Theory]
    [InlineData("Microsoft Visual Studio", "Save changes to the following items?", "Save", "Don't Save", "Cancel")]
    [InlineData("TcXaeShell", GermanQuestion, "&Ja", "&Nein", "Abbrechen")]
    public void Matches_TheEnglishAndYesNoVariants(string title, string question, string a, string b, string c) =>
        Assert.True(SaveChangesDialog.Matches(Prompt(title, question, buttons: [a, b, c])));

    [Fact]
    public void DoesNotMatch_WithoutTheQuestion() =>
        Assert.False(SaveChangesDialog.Matches(Prompt(question: "")));

    [Fact]
    public void DoesNotMatch_WithoutADontSaveButton() =>
        Assert.False(SaveChangesDialog.Matches(Prompt(buttons: ["Speichern", "Abbrechen"])));

    [Theory]
    [InlineData("Die Konfiguration wurde gespeichert.")]        // "gespeichert" alone
    [InlineData("Möchten Sie die Konfiguration aktivieren?")]
    public void DoesNotMatch_OtherQuestions(string question) =>
        Assert.False(SaveChangesDialog.Matches(Prompt(question: question, buttons: ["Ja", "Nein"])));

    // ---- decision -----------------------------------------------------------------

    [Theory]
    [InlineData(true, "Speichern")]
    [InlineData(false, "Nicht speichern")]
    public void PlanAnswer_PicksTheExactButton_NotTheSubstring(bool save, string expected) =>
        Assert.Equal(expected, SaveChangesDialog.PlanAnswer(Prompt(), save)!.Caption);

    [Fact]
    public void PlanAnswer_DontSaveListedFirst_StillSavesWhenAsked() =>
        Assert.Equal("Speichern",
            SaveChangesDialog.PlanAnswer(Prompt(buttons: ["Nicht speichern", "Speichern", "Abbrechen"]), save: true)!.Caption);

    [Fact]
    public void PlanCancel_FindsAbbrechen() => Assert.Equal("Abbrechen", SaveChangesDialog.PlanCancel(Prompt())!.Caption);

    [Fact]
    public void Caption_StripsWin32AndWpfAccelerators()
    {
        Assert.Equal("Speichern", new PromptButton("_Speichern").Caption);
        Assert.Equal("Nicht speichern", new PromptButton("&Nicht speichern").Caption);
    }

    [Fact]
    public void IsWpf_ByHwndWrapperClass()
    {
        Assert.True(Prompt().IsWpf);
        Assert.False(Prompt(cls: "#32770").IsWpf);
    }

    // ---- watcher --------------------------------------------------------------------

    private sealed class Rig
    {
        public readonly List<(IntPtr Hwnd, string Caption)> Presses = [];
        public readonly HashSet<IntPtr> Open = [];
        public readonly List<IntPtr> WmClosed = [];
        public Func<string, bool> Closes = _ => true; // which button captions close the window

        public SaveChangesWatcher Watcher(bool save) => new(Scope, save,
            snapshot: _ => [],
            press: (w, b) => { Presses.Add((w.Hwnd, b.Caption)); if (Closes(b.Caption)) Open.Remove(w.Hwnd); },
            waitClosed: (h, _) => !Open.Contains(h),
            close: h => WmClosed.Add(h));

        public PromptWindow Add(PromptWindow w) { Open.Add(w.Hwnd); return w; }
    }

    [Theory]
    [InlineData(true, "Speichern", "saved")]
    [InlineData(false, "Nicht speichern", "discarded")]
    public void Watcher_AnswersAsAskedAndVerifiesClosed(bool save, string expected, string outcomeWord)
    {
        var rig = new Rig();
        var w = rig.Add(Prompt());
        var watcher = rig.Watcher(save);

        watcher.Tick([w]);

        Assert.Equal([(w.Hwnd, expected)], rig.Presses);
        Assert.False(watcher.Failed);
        Assert.Contains($"answered '{expected}'", watcher.Outcome);
        Assert.Contains("UIA Invoke", watcher.Outcome);
        Assert.Contains(outcomeWord, watcher.Outcome);
    }

    [Fact]
    public void Watcher_AnswersEachWindowOnlyOnce()
    {
        var rig = new Rig { Closes = _ => false };
        var w = rig.Add(Prompt());
        var watcher = rig.Watcher(save: true);
        rig.Closes = c => c == "Speichern";

        watcher.Tick([w]);
        rig.Open.Add(w.Hwnd); // still reported by a later snapshot
        watcher.Tick([w]);

        Assert.Single(rig.Presses);
    }

    [Fact]
    public void Watcher_NoPrompt_NoOutcome()
    {
        var rig = new Rig();
        var watcher = rig.Watcher(save: true);

        watcher.Tick([rig.Add(Prompt(question: "Etwas anderes", buttons: ["OK"]))]);

        Assert.Empty(rig.Presses);
        Assert.Null(watcher.Outcome);
        Assert.False(watcher.Failed);
    }

    [Theory]
    [InlineData(HostPid)]   // the host's own GUI
    [InlineData(1234u)]     // another process, not owned by the XAE main window
    public void Watcher_IgnoresWindowsOutsideTheXaeScope(uint pid)
    {
        var rig = new Rig();
        var watcher = rig.Watcher(save: true);

        watcher.Tick([rig.Add(Prompt(pid: pid, owner: (IntPtr)0x999))]);

        Assert.Empty(rig.Presses);
        Assert.Null(watcher.Outcome);
    }

    [Fact]
    public void Watcher_AnswerDoesNotClose_CancelsAndReportsFailure()
    {
        var rig = new Rig { Closes = c => c == "Abbrechen" };
        var w = rig.Add(Prompt());
        var watcher = rig.Watcher(save: true);

        watcher.Tick([w]);

        Assert.Equal(["Speichern", "Abbrechen"], rig.Presses.Select(p => p.Caption));
        Assert.True(watcher.Failed);
        Assert.Contains("could NOT be answered 'Speichern'", watcher.Outcome);
        Assert.Empty(rig.WmClosed);
    }

    [Fact]
    public void Watcher_NothingCloses_FallsBackToWmCloseAndSaysSo()
    {
        var rig = new Rig { Closes = _ => false };
        var w = rig.Add(Prompt());
        var watcher = rig.Watcher(save: false);

        watcher.Tick([w]);

        Assert.True(watcher.Failed);
        Assert.Equal([w.Hwnd], rig.WmClosed);
        Assert.Contains("nor cancelled", watcher.Outcome);
    }

    [Fact]
    public void Watcher_PressThrows_IsTreatedAsNotClosed()
    {
        var rig = new Rig();
        var w = rig.Add(Prompt());
        var watcher = new SaveChangesWatcher(Scope, save: true,
            snapshot: _ => [],
            press: (_, b) => { if (b.Caption == "Speichern") throw new InvalidOperationException("UIA gone"); rig.Open.Remove(w.Hwnd); },
            waitClosed: (h, _) => !rig.Open.Contains(h),
            close: _ => { });

        watcher.Tick([w]);

        Assert.True(watcher.Failed);
        Assert.Contains("cancelled instead", watcher.Outcome);
    }

    [Fact]
    public void Watcher_Win32Prompt_ReportsBmClick()
    {
        var rig = new Rig();
        var w = rig.Add(Prompt(cls: "#32770", buttons: ["&Ja", "&Nein", "Abbrechen"]));
        var watcher = rig.Watcher(save: true);

        watcher.Tick([w]);

        Assert.Contains("answered 'Ja' (BM_CLICK)", watcher.Outcome);
    }
}
