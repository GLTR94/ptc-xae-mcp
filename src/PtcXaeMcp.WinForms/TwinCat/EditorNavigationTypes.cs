namespace PtcXaeMcp.WinForms.TwinCat;

// ---- editor-navigation ---------------------
//
// Domain language (kept identical to the spec so code, tool descriptions and tests
// say the same thing):
//   * Pane            — one of the two SynEdControl editors inside a POU document
//                       (declaration pane / implementation pane), each with its OWN
//                       1-based line numbering. Not "the editor", not "the file".
//   * Implementation
//     line            — 1-based index into ITcPlcImplementation.ImplementationText
//                       split on LF; identical to the implementation pane's gutter
//                       number (proven live: label `1010:` = line 59 in both).
//   * Pane focus      — UIAutomation SetFocus() on the target pane; the precondition
//                       for the VS `Edit.GoTo` command to be available. A third,
//                       distinct concept next to tab activation (DTE, reliable) and
//                       OS foreground (Win32, best-effort).
//   * Go-To dialog    — the modal WinForms "Gehe zu Zeile" window `Edit.GoTo` opens;
//                       identified by STRUCTURE (owner + one EDIT + OK button), never
//                       by its localized title.
//   * Cursor confirmed— STRUCTURAL confirmation: the Go-To dialog was seen, accepted
//                       the line and closed on OK. There is no caret read-back (the
//                       TwinCAT ST editor exposes no EnvDTE TextSelection).

/// <summary>Which pane of a POU document a line number refers to.</summary>
internal enum EditorSection { Implementation, Declaration }

/// <summary>
/// Result of the headless CASE-branch search (twincat_locate_step). <see cref="Line"/>
/// / <see cref="EndLine"/> are implementation lines (1-based, LF-split). <see cref="Label"/>
/// is the label text exactly as written in front of the colon (e.g. "100, 1110").
/// </summary>
internal sealed record StepLocation(
    string TreePath,
    int Step,
    string StepVariable,
    int Line,
    string Label,
    int EndLine,
    string Snippet);

/// <summary>
/// Result of the visible cursor move (twincat_goto_line). The three visibility concepts
/// are reported separately so a denied OS focus-steal or a swallowed dialog never reads
/// as "the tab did not open":
///   Opened / AlreadyOpen  — tab activation (DTE, reliable), as OpenPlcObject.
///   PaneFocused           — UIA SetFocus on the requested pane succeeded.
///   DialogSeen            — the Go-To dialog appeared after the trigger.
///   CursorConfirmed       — the dialog accepted the line and closed on OK.
///   ForegroundConfirmed   — OS foreground raise confirmed (best-effort).
///   Trigger               — which trigger produced the dialog ("Edit.GoTo command",
///                           "Ctrl+G hotkey") or null when none did.
/// </summary>
internal sealed record GotoLineResult(
    string TreePath,
    EditorSection Section,
    int Line,
    bool Opened,
    bool AlreadyOpen,
    bool PaneFocused,
    bool DialogSeen,
    bool CursorConfirmed,
    bool ForegroundRequested,
    bool ForegroundConfirmed,
    string? Trigger,
    string? Note);
