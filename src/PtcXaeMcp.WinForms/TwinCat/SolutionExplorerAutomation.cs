using System.Windows.Automation;

namespace PtcXaeMcp.WinForms.TwinCat;

// ---- open-object: sysman node selection via UIAutomation --------------------
//
// System-manager nodes (EtherCAT/IO, Motion/NC, TcCOM, Scope) are NOT VS documents
// and share ONE editor — proven live: the VS shell
// document-open service returns null/E_NOINTERFACE for them, OpenStandardEditor
// crashes out-of-process, and the DTE Solution-Explorer object model
// (UIHierarchyItem.Select) is absent on TcXaeShell. The only route that surfaces
// such a node is to drive the actual Solution-Explorer tree CONTROL via
// UIAutomation: select the node, and the single shared sysman editor follows.
//
// UIAutomation is inherently cross-process, so this attaches by the XAE main-window
// handle (from dte.MainWindow.HWnd) and walks the tree named "SolutionExplorer" along the
// node's path (SegmentTreeWalker), expanding only the ancestors on that path so the
// (virtualized) target row realizes. Validated live against TcXaeShell 3.1.4026.22.
internal static class SolutionExplorerAutomation
{
    // AutomationId of the Solution-Explorer tree control inside TcXaeShell / VS.
    private const string SolutionExplorerAutomationId = "SolutionExplorer";

    /// <summary>
    /// Selects the Solution-Explorer node at <paramref name="segments"/> (display names, root first)
    /// in the XAE identified by <paramref name="xaeMainWindow"/>. The path is walked segment by
    /// segment (issue #150, C-9) — a leaf name that exists in several places selects the one under
    /// the given parents — and only nodes on the way are expanded; nodes the walk expanded are
    /// collapsed again on a miss. The shared system-manager editor follows the selection. Returns
    /// false (with a reason in <paramref name="note"/>) on any miss; never throws.
    /// </summary>
    public static bool TrySelectPath(IntPtr xaeMainWindow, IReadOnlyList<string> segments, out string note) =>
        TrySelect(xaeMainWindow, segments, out _, out note);

    /// <summary>
    /// Selects the node like <see cref="TrySelectPath"/>, then opens its editor: focuses the row, raises the
    /// XAE and presses Enter (only when the XAE is the foreground window, see
    /// <see cref="GoToDialogAutomation.SendEnterToXae"/>). Selection alone does not open the shared
    /// system-manager editor while none is open (live, 2026-10-08, SYSTEM &gt; License). Never throws.
    /// </summary>
    public static bool TryActivatePath(IntPtr xaeMainWindow, IReadOnlyList<string> segments, out string note)
    {
        if (!TrySelect(xaeMainWindow, segments, out var match, out note)) return false;

        Win32DialogAutomation.TryRaiseXaeToForeground(xaeMainWindow);
        try { match!.SetFocus(); }
        catch (Exception ex) { note += $"; SetFocus failed: {ex.Message}"; return false; }

        if (!GoToDialogAutomation.SendEnterToXae(xaeMainWindow))
        {
            note += "; the XAE is not the foreground window, so Enter was not sent (the editor did not open)";
            return false;
        }
        note += "; Enter sent to open its editor";
        return true;
    }

    private static bool TrySelect(IntPtr xaeMainWindow, IReadOnlyList<string> segments, out AutomationElement? selected, out string note)
    {
        selected = null;
        note = string.Empty;
        if (xaeMainWindow == IntPtr.Zero) { note = "no XAE main-window handle"; return false; }
        if (segments.Count == 0 || segments.Any(string.IsNullOrWhiteSpace)) { note = "empty node path"; return false; }

        AutomationElement? root;
        try { root = AutomationElement.FromHandle(xaeMainWindow); }
        catch (Exception ex) { note = $"FromHandle failed: {ex.Message}"; return false; }
        if (root is null) { note = "XAE window element not found"; return false; }

        AutomationElement? tree;
        try
        {
            tree = root.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, SolutionExplorerAutomationId));
        }
        catch (Exception ex) { note = $"locating SE tree failed: {ex.Message}"; return false; }
        if (tree is null) { note = "Solution Explorer tree not found (is it open?)"; return false; }

        SegmentTreeWalker.WalkResult walk;
        try { walk = SegmentTreeWalker.Find(new UiaNode(tree), segments); }
        catch (Exception ex) { note = $"tree walk failed: {ex.Message}"; return false; }
        if (walk.Match is not UiaNode { Element: var match }) { note = walk.Note; return false; }

        try { (match.GetCurrentPattern(ScrollItemPattern.Pattern) as ScrollItemPattern)?.ScrollIntoView(); }
        catch (Exception) { /* scroll is best-effort */ }

        try
        {
            if (match.GetCurrentPattern(SelectionItemPattern.Pattern) is not SelectionItemPattern sel)
            {
                note = $"node '{segments[^1]}' is not selectable (no SelectionItem pattern)";
                return false;
            }
            sel.Select();
            note = $"selected '{match.Current.Name}' in the Solution Explorer ({walk.Note})";
            selected = match;
            return true;
        }
        catch (Exception ex) { note = $"Select failed: {ex.Message}"; return false; }
    }

    /// <summary>
    /// Selects a node known only by its display name — the shallowest match (breadth-first, see
    /// <see cref="SegmentTreeWalker.MaxAnchorDepth"/>). Prefer <see cref="TrySelectPath"/> whenever
    /// the parents are known.
    /// </summary>
    public static bool TrySelectNode(IntPtr xaeMainWindow, string leafName, out string note) =>
        TrySelectPath(xaeMainWindow, new[] { leafName }, out note);

    // UIAutomation adapter for the segment walk. Children are read after a short settle when the
    // node was just expanded (virtualized rows realize asynchronously).
    private sealed class UiaNode(AutomationElement element) : IWalkNode
    {
        private bool _justExpanded;

        public AutomationElement Element { get; } = element;

        public string Name
        {
            get { try { return Element.Current.Name ?? ""; } catch (Exception) { return ""; } }
        }

        public bool IsItem
        {
            get { try { return Element.Current.ControlType == ControlType.TreeItem; } catch (Exception) { return false; } }
        }

        public bool IsCollapsed => Pattern()?.Current.ExpandCollapseState == ExpandCollapseState.Collapsed;

        public void Expand()
        {
            try { Pattern()?.Expand(); _justExpanded = true; } catch (Exception) { /* leaf / not expandable */ }
        }

        public void Collapse()
        {
            try { Pattern()?.Collapse(); } catch (Exception) { /* already gone */ }
        }

        public IReadOnlyList<IWalkNode> Children()
        {
            if (_justExpanded) { Thread.Sleep(60); _justExpanded = false; }
            var list = new List<IWalkNode>();
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? child;
            try { child = walker.GetFirstChild(Element); } catch (Exception) { return list; }
            while (child is not null)
            {
                list.Add(new UiaNode(child));
                try { child = walker.GetNextSibling(child); } catch (Exception) { break; }
            }
            return list;
        }

        private ExpandCollapsePattern? Pattern()
        {
            try { return Element.GetCurrentPattern(ExpandCollapsePattern.Pattern) as ExpandCollapsePattern; }
            catch (Exception) { return null; }
        }
    }
}
