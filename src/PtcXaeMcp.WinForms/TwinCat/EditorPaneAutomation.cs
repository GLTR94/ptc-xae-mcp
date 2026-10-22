using System.Windows.Automation;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// UIAutomation access to the two panes of a TwinCAT POU document.
/// Each pane is a <c>SynEdControl</c>
/// element (<c>AutomationId = _synEdControl</c>, <c>ControlType.Custom</c>) whose
/// <c>Name</c> carries the pane's full text; there is no TextPattern, so the only
/// thing we can do — and need — is <c>SetFocus()</c>: pane focus is the precondition
/// for the VS <c>Edit.GoTo</c> command to be available.
///
/// While Online the declaration pane is replaced by the monitoring grid (plain
/// <c>node N</c> elements, no SynEdControl) — then only the implementation pane exists.
/// </summary>
internal static class EditorPaneAutomation
{
    private const string PaneClassName = "SynEdControl";
    private const int HeadLength = 60;

    internal sealed record PaneFocusOutcome(bool PaneFound, bool Focused, int PanesFound, string Note);

    /// <summary>
    /// Focus the requested pane under the document window <paramref name="documentHwnd"/>.
    /// <paramref name="expectedText"/> (the pane's cleartext from the Automation Interface)
    /// picks the pane by content; without it — or without a content match — the panes are
    /// taken in order (first = declaration, last = implementation). Never throws.
    /// </summary>
    public static PaneFocusOutcome TryFocusPane(IntPtr documentHwnd, EditorSection section, string? expectedText)
    {
        if (documentHwnd == IntPtr.Zero)
            return new PaneFocusOutcome(false, false, 0, "no document window handle");

        List<AutomationElement> panes;
        try
        {
            var root = AutomationElement.FromHandle(documentHwnd);
            panes = root.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ClassNameProperty, PaneClassName))
                .Cast<AutomationElement>().ToList();
        }
        catch (Exception ex)
        {
            return new PaneFocusOutcome(false, false, 0, $"UIAutomation walk failed: {ex.Message}");
        }

        if (panes.Count == 0)
            return new PaneFocusOutcome(false, false, 0, "no SynEdControl pane under the active document window");

        AutomationElement? target = null;
        string how = "by order";
        string? head = Head(expectedText);
        if (head is not null)
        {
            foreach (var p in panes)
            {
                string? name = null;
                try { name = p.Current.Name; } catch { }
                if (Head(name) is { } h && h.StartsWith(head, StringComparison.Ordinal)) { target = p; how = "by content"; break; }
            }
        }

        if (target is null)
        {
            target = section switch
            {
                EditorSection.Declaration => panes.Count >= 2 ? panes[0] : null,
                _ => panes[^1],
            };
        }

        if (target is null)
            return new PaneFocusOutcome(false, false, panes.Count,
                $"{section.ToString().ToLowerInvariant()} pane not present ({panes.Count} pane found — while Online the declaration pane is the monitoring grid)");

        try
        {
            target.SetFocus();
            return new PaneFocusOutcome(true, true, panes.Count, $"{section.ToString().ToLowerInvariant()} pane focused ({how}, {panes.Count} pane(s))");
        }
        catch (Exception ex)
        {
            return new PaneFocusOutcome(true, false, panes.Count, $"SetFocus on the {section.ToString().ToLowerInvariant()} pane failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Number of lines the pane shows for <paramref name="text"/> (a trailing line break opens one
    /// more, empty line). Issue #150: the XAE Go-To command crashes on a line beyond the pane —
    /// an error box, a .NET assertion ("Abbrechen" there terminates the XAE) and a NULL
    /// synEdControl — so the caller refuses such a line up front.
    /// </summary>
    internal static int LineCount(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Length;

    // First HeadLength chars of the line-ending-normalised, trimmed text — enough to
    // tell the two panes apart without comparing whole bodies.
    internal static string? Head(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string n = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return n.Length <= HeadLength ? n : n[..HeadLength];
    }
}
