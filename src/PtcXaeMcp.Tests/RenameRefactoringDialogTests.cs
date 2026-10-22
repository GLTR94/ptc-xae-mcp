using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// The XAE's "Automatisches Umbenennen über Refactoring" prompt raised by a PLC rename (seen live on TC3
/// 4026.22, German UI): matched by title or message, answered "Nein" by default and "Ja" with
/// updateReferences, verified closed, cancelled when the answer does not close it — only in the XAE's scope.
/// </summary>
public class RenameRefactoringDialogTests
{
    private const uint XaePid = 8768;
    private const uint HostPid = 4304;
    private static readonly IntPtr XaeMain = (IntPtr)0x30236;
    private static readonly XaeWindowScope Scope = new(new HashSet<uint> { XaePid }, XaeMain, HostPid);

    // Classes as captured live from the prompt (WinForms, not a #32770 message box).
    private const string WinFormsWindow = "WindowsForms10.Window.8.app.0.3bf8826_r13_ad1";
    private const string WinFormsStatic = "WindowsForms10.STATIC.app.0.3bf8826_r13_ad1";
    private const string WinFormsButton = "WindowsForms10.BUTTON.app.0.3bf8826_r13_ad1";

    private const string GermanTitle = "Automatisches Umbenennen über Refactoring";
    private const string GermanMessage =
        "Sie haben Objekt TR_Go in TR_Done umbenannt. Möchten Sie automatisch alle Referenzen im Projekt aktualisieren?";

    private static int _next = 0x2000;
    private static XaeControl Ctl(string cls, string text) => new((IntPtr)Interlocked.Increment(ref _next), cls, text);

    private static XaeDialog Prompt(string title = GermanTitle, string message = GermanMessage, uint pid = XaePid,
        params string[] buttons)
    {
        if (buttons.Length == 0) buttons = ["Abbre&chen", "&Nein", "&Ja"];
        var controls = new List<XaeControl> { Ctl(WinFormsStatic, message) };
        controls.AddRange(buttons.Select(b => Ctl(WinFormsButton, b)));
        return new XaeDialog((IntPtr)Interlocked.Increment(ref _next), pid, (IntPtr)0x2D06A8, WinFormsWindow, title, controls);
    }

    private static XaeControl ButtonOf(XaeDialog d, string caption) => d.Controls.Single(c => c.IsButton && c.Caption == caption);

    // ---- matcher (pattern on text) ---------------------------------------------

    [Fact]
    public void Matches_TheLiveGermanPrompt() => Assert.True(RenameRefactoringDialog.Matches(Prompt()));

    [Fact]
    public void Matches_ByTitleAlone_WhenTheMessageIsUnreadable() =>
        Assert.True(RenameRefactoringDialog.Matches(Prompt(message: "")));

    [Fact]
    public void Matches_ByMessageAlone_WhenTheTitleDiffers() =>
        Assert.True(RenameRefactoringDialog.Matches(Prompt(title: "TwinCAT PLC Control")));

    [Theory]
    [InlineData("Automatic Renaming via Refactoring", "")]
    [InlineData("TwinCAT", "You have renamed object TR_Go to TR_Done. Do you want to automatically update all references in the project?")]
    public void Matches_TheEnglishWording(string title, string message) =>
        Assert.True(RenameRefactoringDialog.Matches(Prompt(title, message, buttons: ["Cancel", "&No", "&Yes"])));

    [Theory]
    [InlineData("Refactoring", "Variablen wurden ersetzt.")]                      // refactoring, but no rename
    [InlineData("Umbenennen", "Neuer Name:")]                                      // a rename, but no refactoring
    [InlineData("TwinCAT XAE", "Einige benötigte Laufzeit-Lizenzen fehlen.")]       // the trial-licence prompt
    [InlineData("Nicht wiederhergestellte Variablenverknüpfungen gefunden", "Verknüpfungsinformationen behalten?")]
    public void DoesNotMatch_OtherXaeDialogs(string title, string message) =>
        Assert.False(RenameRefactoringDialog.Matches(Prompt(title, message)));

    [Fact]
    public void DoesNotMatch_TheWordingOnAButtonOnly() =>
        // A button caption is never the prompt's message (e.g. the host GUI's own controls).
        Assert.False(RenameRefactoringDialog.Matches(Prompt(title: "Tools", message: "",
            buttons: ["Objekt umbenannt, Referenzen aktualisieren"])));

    [Fact]
    public void DoesNotMatch_AWindowWithoutButtons()
    {
        var d = Prompt();
        Assert.False(RenameRefactoringDialog.Matches(d with { Controls = d.Controls.Where(c => !c.IsButton).ToList() }));
    }

    [Theory]
    [InlineData(false, "Nein")]
    [InlineData(true, "Ja")]
    public void PlanAnswer_ClicksTheButtonForTheDecision(bool updateReferences, string caption)
    {
        var d = Prompt();
        ClickPlan plan = RenameRefactoringDialog.PlanAnswer(d, updateReferences);
        Assert.True(plan.UsesButton);
        Assert.Equal(ButtonOf(d, caption).Hwnd, plan.Button);
    }

    [Fact]
    public void PlanAnswer_EnglishButtons()
    {
        var d = Prompt("Automatic Renaming via Refactoring", "", buttons: ["Cancel", "&No", "&Yes"]);
        Assert.Equal(ButtonOf(d, "No").Hwnd, RenameRefactoringDialog.PlanAnswer(d, false).Button);
        Assert.Equal(ButtonOf(d, "Yes").Hwnd, RenameRefactoringDialog.PlanAnswer(d, true).Button);
    }

    // ---- watcher (one pass, synthetic windows, injected Win32) -------------------

    private sealed class Recorder
    {
        public List<IntPtr> Pressed { get; } = [];
        public List<IntPtr> Closed { get; } = [];
        public HashSet<IntPtr> ClosesOn { get; } = [];
        public bool AlwaysClose { get; set; } = true;

        public RenameRefactoringWatcher Watcher(bool updateReferences) => new(Scope, updateReferences,
            snapshot: _ => [],
            press: (_, plan) => Pressed.Add(plan.Button),
            waitClosed: (_, _) => AlwaysClose || Pressed.Any(ClosesOn.Contains),
            close: h => Closed.Add(h));
    }

    [Fact]
    public void Watcher_NoPrompt_ReportsNothing()
    {
        var rec = new Recorder();
        var w = rec.Watcher(false);
        w.Tick([]);
        Assert.Null(w.Outcome);
        Assert.False(w.Failed);
        Assert.Empty(rec.Pressed);
    }

    [Theory]
    [InlineData(false, "Nein", "NOT updated")]
    [InlineData(true, "Ja", "are updated by TwinCAT")]
    public void Watcher_AnswersAsAsked_AndReportsIt(bool updateReferences, string caption, string effect)
    {
        var rec = new Recorder();
        var w = rec.Watcher(updateReferences);
        var d = Prompt();

        w.Tick([d]);

        Assert.Equal([ButtonOf(d, caption).Hwnd], rec.Pressed);
        Assert.False(w.Failed);
        Assert.Contains($"answered '{caption}'", w.Outcome);
        Assert.Contains(GermanTitle, w.Outcome);
        Assert.Contains(effect, w.Outcome);
    }

    // ---- the refactoring preview that "Ja" opens (seen live) ---------------------

    private static XaeDialog Preview(string text = "Umbenennen von 'PRG_RenameVerify.TR_Done' in 'TR_Ja'.") =>
        new((IntPtr)Interlocked.Increment(ref _next), XaePid, (IntPtr)0x2D06A8, WinFormsWindow, "Refactoring",
        [
            Ctl(WinFormsWindow, ""), Ctl("SysHeader32", ""), Ctl(WinFormsButton, "100 %"), Ctl(WinFormsButton, ""),
            Ctl(WinFormsButton, "&Abbrechen"), Ctl(WinFormsButton, "&OK"), Ctl(WinFormsStatic, text),
        ]);

    [Fact]
    public void Preview_IsRecognised_AndIsNotThePrompt()
    {
        Assert.True(RenameRefactoringDialog.IsPreview(Preview()));
        Assert.False(RenameRefactoringDialog.Matches(Preview()));
        Assert.False(RenameRefactoringDialog.IsPreview(Prompt()));
    }

    [Fact]
    public void Preview_OtherRefactoringWindows_AreNotRecognised() =>
        Assert.False(RenameRefactoringDialog.IsPreview(Preview("Variable 'x' in 'y' ersetzen.")));

    [Theory]
    [InlineData(true, "OK", "applied")]
    [InlineData(false, "Abbrechen", "NOT applied")]
    public void Watcher_AnswersThePreview(bool updateReferences, string caption, string effect)
    {
        var rec = new Recorder();
        var w = rec.Watcher(updateReferences);
        var p = Prompt();
        var v = Preview();

        w.Tick([p]);
        w.Tick([v]);

        Assert.Equal([ButtonOf(p, updateReferences ? "Ja" : "Nein").Hwnd, ButtonOf(v, caption).Hwnd], rec.Pressed);
        Assert.False(w.Failed);
        Assert.Contains("preview 'Refactoring'", w.Outcome);
        Assert.Contains(effect, w.Outcome);
    }

    [Fact]
    public void Watcher_AnswersEachPromptWindowOnce()
    {
        var rec = new Recorder();
        var w = rec.Watcher(false);
        var d = Prompt();

        w.Tick([d]);
        w.Tick([d]); // still enumerated while it closes

        Assert.Single(rec.Pressed);
    }

    [Fact]
    public void Watcher_IgnoresPromptsOutsideTheXaeScope()
    {
        var rec = new Recorder();
        var w = rec.Watcher(false);

        w.Tick([Prompt(pid: HostPid)]);

        Assert.Empty(rec.Pressed);
        Assert.Null(w.Outcome);
    }

    [Fact]
    public void Watcher_AnswerDoesNotClose_CancelsAndFails()
    {
        var d = Prompt();
        var rec = new Recorder { AlwaysClose = false };
        rec.ClosesOn.Add(ButtonOf(d, "Abbrechen").Hwnd);
        var w = rec.Watcher(true);

        w.Tick([d]);

        Assert.Equal([ButtonOf(d, "Ja").Hwnd, ButtonOf(d, "Abbrechen").Hwnd], rec.Pressed);
        Assert.True(w.Failed);
        Assert.Contains("could NOT be answered 'Ja'", w.Outcome);
        Assert.Contains("cancelled", w.Outcome);
        Assert.Empty(rec.Closed);
    }

    [Fact]
    public void Watcher_NeitherAnswerNorCancelCloses_SendsCloseAndSaysSo()
    {
        var d = Prompt();
        var rec = new Recorder { AlwaysClose = false };
        var w = rec.Watcher(false);

        w.Tick([d]);

        Assert.True(w.Failed);
        Assert.Equal([d.Hwnd], rec.Closed);
        Assert.Contains("close it in the XAE", w.Outcome);
    }
}
