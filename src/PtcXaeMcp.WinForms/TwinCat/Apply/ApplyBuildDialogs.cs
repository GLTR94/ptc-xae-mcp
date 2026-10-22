using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace PtcXaeMcp.WinForms.TwinCat.Apply;

/// <summary>
/// One modal the XAE raises while apply_schema builds (issue #155 step 8), and how it is answered.
/// </summary>
/// <param name="Name">Short name used in the result.</param>
/// <param name="Buttons">Captions of the one button to press (first match wins).</param>
/// <param name="FallbackCommandId">WM_COMMAND id when no such button exists; 0 = never fall back
/// (the dialog is then treated as unknown and the run fails naming it).</param>
/// <param name="IsWarning">True when the answer is worth a warning in the apply result, not just a note.</param>
internal sealed record ApplyBuildDialogRule(
    string Name,
    IReadOnlyList<string> Buttons,
    int FallbackCommandId,
    bool IsWarning,
    Func<string, IReadOnlyList<string>, bool> Matches);

/// <summary>
/// The documented answer for every modal known to block <c>SolutionBuild.Build(true)</c> during
/// apply_schema (observed live on TwinCAT XAE 4026.22, German UI, 2026-09-27). Anything else is
/// not answered: the watcher reports it and the build fails naming it (never a blind click).
/// <list type="table">
/// <item><term>"Gerät '&lt;master&gt;' benötigt Sync Master (mindestens eine Verknüpfung zu einer Task notwendig)"</term>
/// <description>information box (OK only). Answered with OK. Harmless: the master gets its task link
/// from the IO links this run creates.</description></item>
/// <item><term>"Double use of the same Ethernet interface not allowed: '&lt;master A&gt;' / '&lt;master B&gt;'"</term>
/// <description>information box (OK only). Answered with OK and reported as a warning; the build then fails
/// with its own error (two enabled masters on one adapter), which the result carries.</description></item>
/// <item><term>"Nicht wiederhergestellte Variablenverknüpfungen gefunden" — "Verknüpfungsinformationen für mögliche
/// spätere Wiederherstellung behalten?" [Ja] [Nein] [Nicht erneut fragen]</term>
/// <description>question. Answered with <b>Ja</b> (keep the link information for a later restore) — the
/// answer that does not silently drop links; "Nicht erneut fragen" is never pressed. Reported as a
/// warning. Without a "Ja"/"Yes" button the dialog is not answered and the run fails naming it.</description></item>
/// <item><term>"Restore variable links succeeded: N links not restored / Open 'Output Window' for details"</term>
/// <description>information box that follows the question. Answered with OK; its text (how many links were
/// not restored) is reported as a warning.</description></item>
/// </list>
/// Matching uses umlaut-free fragments of XAE's own texts.
/// </summary>
internal static class ApplyBuildDialogPolicy
{
    public static readonly IReadOnlyList<ApplyBuildDialogRule> Rules =
    [
        new("Sync Master info", ["OK"], XaeDialogs.IDOK, IsWarning: false,
            (title, texts) => AnyContains(texts, "Sync Master")),
        new("Double use of the Ethernet interface", ["OK"], XaeDialogs.IDOK, IsWarning: true,
            (title, texts) => AnyContains(texts, "Double use of the same Ethernet interface")),
        new("Unrestored variable links (kept for a later restore)", ["Ja", "Yes"], FallbackCommandId: 0, IsWarning: true,
            (title, texts) => title.Contains("Nicht wiederhergestellte Variablenverkn", StringComparison.OrdinalIgnoreCase)
                              || AnyContains(texts, "Wiederherstellung behalten")
                              || title.Contains("Unrestored variable links", StringComparison.OrdinalIgnoreCase)),
        new("Restore variable links result", ["OK"], XaeDialogs.IDOK, IsWarning: true,
            (title, texts) => AnyContains(texts, "Restore variable links")),
    ];

    /// <summary>The rule for a dialog, or null when it is not one apply_schema answers.</summary>
    public static ApplyBuildDialogRule? Classify(string title, IReadOnlyList<string> texts) =>
        Rules.FirstOrDefault(r => r.Matches(title ?? string.Empty, texts));

    /// <summary>"Title: text | text", line breaks folded.</summary>
    public static string Describe(string title, IEnumerable<string> texts)
    {
        var parts = texts
            .Select(t => string.Join(" ", t.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())))
            .Where(t => t.Length > 0)
            .ToList();
        string head = string.IsNullOrWhiteSpace(title) ? "<untitled>" : title.Trim();
        return parts.Count == 0 ? head : $"{head}: {string.Join(" | ", parts)}";
    }

    private static bool AnyContains(IReadOnlyList<string> texts, string needle) =>
        texts.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

/// <summary>What the build waits on: answered dialogs, and a blocker it must not wait for.</summary>
internal interface IApplyBuildDialogWatcher : IDisposable
{
    void Start();

    /// <summary>Stops polling after <paramref name="tail"/> (dialogs raised right after the build).</summary>
    void Stop(TimeSpan tail);

    /// <summary>Every dialog answered, as "name: action — text".</summary>
    IReadOnlyList<string> Answered { get; }

    /// <summary>The answered dialogs whose rule is a warning.</summary>
    IReadOnlyList<string> Warnings { get; }

    /// <summary>A dialog nothing answers (unknown, or still open after its answer), described; null while none.</summary>
    string? Blocker { get; }
}

/// <summary>
/// Win32 watcher (issue #155 step 8, on the dialog automation of #177): polls the attached XAE's
/// windows, answers the dialogs of <see cref="ApplyBuildDialogPolicy"/> with BM_CLICK on the named
/// button, and reports any other modal that stays up for <see cref="UnknownGrace"/> as
/// <see cref="Blocker"/> so the build can fail naming it instead of hanging. No COM on this thread.
/// </summary>
internal sealed class ApplyBuildDialogWatcher : IApplyBuildDialogWatcher
{
    /// <summary>How long an unanswered message box may stay up before it counts as blocking.</summary>
    internal static readonly TimeSpan UnknownGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The same for another window whose owner is disabled (a WinForms/WPF dialog, or a progress
    /// window of a long build): longer, so a busy-but-working XAE is not mistaken for a blocked one.
    /// </summary>
    internal static readonly TimeSpan UnknownWindowGrace = TimeSpan.FromSeconds(60);

    /// <summary>How long an answered dialog may stay open before it counts as blocking.</summary>
    internal static readonly TimeSpan AnsweredGrace = TimeSpan.FromSeconds(5);

    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);

    private readonly XaeWindowScope _scope;
    private readonly Action<XaeDialog, ClickPlan> _press;
    private readonly Func<XaeDialog, bool> _isModal;
    private readonly ConcurrentQueue<string> _answered = new();
    private readonly ConcurrentQueue<string> _warnings = new();
    private readonly Dictionary<IntPtr, DateTime> _answeredAt = [];
    private readonly Dictionary<IntPtr, DateTime> _unknownSince = [];
    private volatile string? _blocker;
    private volatile bool _stop;
    private Thread? _thread;

    public ApplyBuildDialogWatcher(XaeWindowScope scope,
        Action<XaeDialog, ClickPlan>? press = null,
        Func<XaeDialog, bool>? isModal = null)
    {
        _scope = scope;
        _press = press ?? XaeDialogs.Execute;
        _isModal = isModal ?? IsModalWindow;
    }

    public IReadOnlyList<string> Answered => _answered.ToArray();
    public IReadOnlyList<string> Warnings => _warnings.ToArray();
    public string? Blocker => _blocker;

    public void Start()
    {
        if (_thread is not null) return; // a second build of the same run keeps the one watcher
        _thread = new Thread(Loop) { IsBackground = true, Name = "ApplyBuildDialogWatcher" };
        _thread.Start();
    }

    public void Stop(TimeSpan tail)
    {
        if (_stop) return;
        if (tail > TimeSpan.Zero && _thread is not null) Thread.Sleep(tail);
        _stop = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop(TimeSpan.Zero);

    private void Loop()
    {
        while (!_stop)
        {
            try { Tick(XaeDialogs.Snapshot(_scope), DateTime.UtcNow); }
            catch (Exception) { /* a Win32 hiccup never ends the watcher */ } // com-allow: Win32-only watcher thread
            Thread.Sleep(150);
        }
    }

    /// <summary>One pass over a snapshot (unit-tested with synthetic windows).</summary>
    internal void Tick(IReadOnlyList<XaeDialog> windows, DateTime nowUtc)
    {
        var open = new HashSet<IntPtr>();
        string? blockingBox = null, blockingWindow = null;
        foreach (XaeDialog d in windows)
        {
            if (!_isModal(d)) continue;
            open.Add(d.Hwnd);
            IReadOnlyList<string> texts = d.Texts.ToList();
            string description = ApplyBuildDialogPolicy.Describe(d.Title, texts.Where(t => !IsButtonCaption(d, t)));

            if (_answeredAt.TryGetValue(d.Hwnd, out DateTime answeredAt))
            {
                if (nowUtc - answeredAt >= AnsweredGrace)
                    _blocker ??= $"{description} (still open after it was answered)";
                continue;
            }

            ApplyBuildDialogRule? rule = ApplyBuildDialogPolicy.Classify(d.Title, texts);
            ClickPlan? plan = rule is null ? null : XaeDialogs.PlanClick(d, rule.FallbackCommandId, [.. rule.Buttons]);
            if (rule is null || plan is null || (!plan.UsesButton && rule.FallbackCommandId == 0))
            {
                if (!_unknownSince.TryGetValue(d.Hwnd, out DateTime since)) _unknownSince[d.Hwnd] = since = nowUtc;
                if (d.IsMessageBox && nowUtc - since >= UnknownGrace) blockingBox ??= description;
                else if (!d.IsMessageBox && nowUtc - since >= UnknownWindowGrace) blockingWindow ??= description;
                continue;
            }

            _press(d, plan);
            _answeredAt[d.Hwnd] = nowUtc;
            string line = $"{rule.Name}: {plan.Description} — '{description}'";
            _answered.Enqueue(line);
            if (rule.IsWarning) _warnings.Enqueue(line);
        }

        // A message box names the blocker better than a disabled-owner tool window next to it.
        _blocker ??= blockingBox ?? blockingWindow;
        foreach (IntPtr gone in _unknownSince.Keys.Where(h => !open.Contains(h)).ToList()) _unknownSince.Remove(gone);
    }

    private static bool IsButtonCaption(XaeDialog d, string text) =>
        d.Controls.Any(c => c.IsButton && string.Equals(c.Text, text, StringComparison.Ordinal));

    // A message box, or any window whose owner is disabled (how Win32 modality looks from outside).
    private static bool IsModalWindow(XaeDialog d) =>
        d.IsMessageBox || (d.Owner != IntPtr.Zero && !IsWindowEnabled(d.Owner));
}
