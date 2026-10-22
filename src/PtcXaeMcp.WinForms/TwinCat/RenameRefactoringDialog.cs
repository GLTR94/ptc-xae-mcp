namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>Result of a PLC rename through the Automation Interface.</summary>
/// <param name="NewPath">Tree path of the renamed node.</param>
/// <param name="RefactoringPrompt">What happened to the XAE's "rename via refactoring" prompt during the
/// rename (see <see cref="RenameRefactoringDialog"/>); null when the XAE raised none.</param>
/// <param name="RefactoringPromptFailed">True when the prompt appeared but could not be answered as asked
/// (it was cancelled or is still open).</param>
public sealed record PlcRenameResult(string NewPath, string? RefactoringPrompt = null, bool RefactoringPromptFailed = false);

/// <summary>
/// The modal the XAE raises when a PLC object is renamed (setting <c>ITcSmTreeItem.Name</c>), seen live on
/// TC3 4026.22 with a German UI, e.g. for a TRANSITION, a METHOD or an ACTION:
/// <code>
/// Automatisches Umbenennen über Refactoring                     (WinForms window, NOT a #32770)
///   Sie haben Objekt TR_Go in TR_Done umbenannt. Möchten Sie automatisch alle Referenzen im Projekt aktualisieren?
///   [&amp;Ja] [&amp;Nein] [Abbre&amp;chen]
/// </code>
/// The rename call does not return while it is open, and every later call fails with "operation in
/// progress". It is answered deterministically: "Nein" by default (only the object is renamed, as the
/// tools always documented), "Ja" when the caller asked for <c>updateReferences</c>.
/// <para>
/// "Ja" opens a second modal, the refactoring preview (also seen live, the call still blocks):
/// <code>
/// Refactoring                                                   (WinForms window: change tree, list view)
///   Umbenennen von 'PRG_X.TR_Done' in 'TR_Ja'.
///   [&amp;OK] [&amp;Abbrechen]
/// </code>
/// It is confirmed with OK when <c>updateReferences</c> is true and cancelled otherwise.
/// </para>
/// <para>
/// Matching (umlaut-free fragments, case-insensitive): the title contains "Refactoring" together with
/// "Umbenenn" or "Renam", or a non-button text contains "umbenannt" + "Referenzen" or "renamed" +
/// "references". The English wording is not observed live — it is the cheap counterpart of the German
/// one; an unmatched variant is reported by the dispatcher's blocking-dialog message instead.
/// </para>
/// </summary>
internal static class RenameRefactoringDialog
{
    private const int IDNO = 7;

    /// <summary>True when the title or a (non-button) text is the refactoring prompt's wording.</summary>
    internal static bool IsPromptText(string? title, IEnumerable<string> texts)
    {
        string t = title ?? string.Empty;
        if (Has(t, "Refactoring") && (Has(t, "Umbenenn") || Has(t, "Renam"))) return true;
        return texts.Any(x =>
            (Has(x, "umbenannt") && Has(x, "Referenzen")) ||
            (Has(x, "renamed") && Has(x, "references")));
    }

    /// <summary>The prompt itself: its wording plus at least one button to answer it with.</summary>
    internal static bool Matches(XaeDialog d) =>
        d.Controls.Any(c => c.IsButton) &&
        IsPromptText(d.Title, d.Controls.Where(c => !c.IsButton).Select(c => c.Text));

    /// <summary>The button that answers the prompt for <paramref name="updateReferences"/>.</summary>
    internal static ClickPlan PlanAnswer(XaeDialog d, bool updateReferences) =>
        updateReferences
            ? XaeDialogs.PlanClick(d, XaeDialogs.IDYES, "Ja", "Yes")
            : XaeDialogs.PlanClick(d, IDNO, "Nein", "No");

    internal static string AnswerName(bool updateReferences) => updateReferences ? "Ja" : "Nein";

    /// <summary>
    /// The refactoring preview that follows "Ja": title "Refactoring" (without the rename wording of the
    /// prompt), an OK button, and a text "Umbenennen von 'a' in 'b'" (English: "Renam…").
    /// </summary>
    internal static bool IsPreview(XaeDialog d) =>
        Has(d.Title, "Refactoring") && !Matches(d) &&
        d.Controls.Any(c => c.IsButton && string.Equals(c.Caption, "OK", StringComparison.OrdinalIgnoreCase)) &&
        d.Controls.Where(c => !c.IsButton).Any(c => Has(c.Text, "Umbenennen von") || Has(c.Text, "Renam"));

    /// <summary>OK (apply the reference changes) when <paramref name="updateReferences"/>, else cancel.</summary>
    internal static ClickPlan PlanPreview(XaeDialog d, bool updateReferences) =>
        updateReferences
            ? XaeDialogs.PlanClick(d, XaeDialogs.IDOK, "OK")
            : XaeDialogs.PlanClick(d, XaeDialogs.IDCANCEL, "Abbrechen", "Cancel");

    private static bool Has(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Background Win32 watcher (no COM) that answers <see cref="RenameRefactoringDialog"/> while a rename call
/// blocks, scoped to the attached XAE (issue #150). Each prompt window is answered once with BM_CLICK
/// (SendMessageTimeout, synchronous) and then verified gone by window state; a prompt that stays open is
/// cancelled so the rename call cannot hang, and that is reported.
/// </summary>
internal sealed class RenameRefactoringWatcher : IDisposable
{
    internal static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(3);

    private readonly XaeWindowScope _scope;
    private readonly bool _updateReferences;
    private readonly Func<XaeWindowScope, List<XaeDialog>> _snapshot;
    private readonly Action<XaeDialog, ClickPlan> _press;
    private readonly Func<IntPtr, TimeSpan, bool> _waitClosed;
    private readonly Action<IntPtr> _close;
    private readonly HashSet<IntPtr> _handled = [];
    private readonly object _gate = new();
    private readonly List<string> _outcomes = [];
    private volatile bool _stop;
    private Thread? _thread;

    internal RenameRefactoringWatcher(XaeWindowScope scope, bool updateReferences,
        Func<XaeWindowScope, List<XaeDialog>>? snapshot = null,
        Action<XaeDialog, ClickPlan>? press = null,
        Func<IntPtr, TimeSpan, bool>? waitClosed = null,
        Action<IntPtr>? close = null)
    {
        _scope = scope;
        _updateReferences = updateReferences;
        _snapshot = snapshot ?? (s => XaeDialogs.Snapshot(s));
        _press = press ?? XaeDialogs.Execute;
        _waitClosed = waitClosed ?? XaeDialogs.WaitClosed;
        _close = close ?? XaeDialogs.Close;
    }

    /// <summary>What was done with the prompt(s), "; "-joined; null while none appeared.</summary>
    internal string? Outcome
    {
        get { lock (_gate) return _outcomes.Count == 0 ? null : string.Join("; ", _outcomes); }
    }

    /// <summary>True when a prompt appeared but could not be answered as asked.</summary>
    internal bool Failed { get; private set; }

    internal void Start()
    {
        if (_scope.IsEmpty || _thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "RenameRefactoringWatcher" };
        _thread.Start();
    }

    /// <summary>Stops polling, after one last pass for a prompt raised as the call returned.</summary>
    internal void Stop()
    {
        if (_thread is null) return;
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(10));
        _thread = null;
        try { Tick(_snapshot(_scope)); } catch (Exception) { /* never throw from Stop */ }
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        while (!_stop)
        {
            try { Tick(_snapshot(_scope)); }
            catch (Exception) { /* never crash the watcher */ }
            Thread.Sleep(100);
        }
    }

    /// <summary>One pass over one snapshot (unit-tested with synthetic windows).</summary>
    internal void Tick(IEnumerable<XaeDialog> windows)
    {
        // The polling thread and Stop()'s last pass never answer the same window twice.
        lock (_gate)
        {
            foreach (XaeDialog d in windows)
            {
                if (!_scope.Includes(d)) continue;
                if (RenameRefactoringDialog.Matches(d))
                {
                    if (_handled.Add(d.Hwnd)) _outcomes.Add(Answer(d));
                }
                else if (RenameRefactoringDialog.IsPreview(d) && _handled.Add(d.Hwnd))
                    _outcomes.Add(AnswerPreview(d));
            }
        }
    }

    private string Answer(XaeDialog d)
    {
        string title = string.IsNullOrWhiteSpace(d.Title) ? "refactoring prompt" : d.Title.Trim();
        string want = RenameRefactoringDialog.AnswerName(_updateReferences);
        ClickPlan plan = RenameRefactoringDialog.PlanAnswer(d, _updateReferences);
        _press(d, plan);
        if (_waitClosed(d.Hwnd, CloseWait))
            return _updateReferences
                ? $"'{title}' answered '{want}' ({plan.Description}) — references to the old name are updated by TwinCAT"
                : $"'{title}' answered '{want}' ({plan.Description}) — references to the old name were NOT updated";
        return CancelAfterFailedAnswer(d, title, want, plan);
    }

    private string AnswerPreview(XaeDialog d)
    {
        string title = string.IsNullOrWhiteSpace(d.Title) ? "refactoring preview" : d.Title.Trim();
        string want = _updateReferences ? "OK" : "Abbrechen";
        ClickPlan plan = RenameRefactoringDialog.PlanPreview(d, _updateReferences);
        _press(d, plan);
        if (_waitClosed(d.Hwnd, CloseWait))
            return _updateReferences
                ? $"preview '{title}' confirmed '{want}' ({plan.Description}) — reference changes applied"
                : $"preview '{title}' cancelled ({plan.Description}) — reference changes NOT applied";
        return CancelAfterFailedAnswer(d, title, want, plan);
    }

    private string CancelAfterFailedAnswer(XaeDialog d, string title, string want, ClickPlan plan)
    {
        // The answer did not close it (no such button, or the click was not taken): never leave the rename
        // call hanging — cancel, and report that the prompt was not answered as asked.
        Failed = true;
        _press(d, XaeDialogs.PlanClick(d, XaeDialogs.IDCANCEL, "Abbrechen", "Cancel"));
        bool cancelled = _waitClosed(d.Hwnd, CloseWait);
        if (!cancelled)
        {
            _close(d.Hwnd);
            cancelled = _waitClosed(d.Hwnd, CloseWait);
        }
        return cancelled
            ? $"'{title}' could NOT be answered '{want}' ({plan.Description}, still open) — cancelled instead; the rename may not have happened"
            : $"'{title}' could NOT be answered '{want}' nor cancelled — close it in the XAE";
    }
}
