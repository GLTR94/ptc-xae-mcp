namespace PtcXaeMcp.WinForms.TwinCat;

// ---- visible-for-video -------------------------------------------------------
// Result shape for OpenPlcObject(treePath): opening a PLC source object's editor
// document in the *visible* XAE so a live demo / promo video shows which object
// is being worked on. The two visibility concepts are kept distinct:
//   * Opened / AlreadyOpen   — the document tab inside XAE (reliable, DTE-driven).
//   * ForegroundRequested /
//     ForegroundConfirmed     — raising the whole XAE top-level window above other
//                               apps via Win32. Best-effort: Windows may refuse a
//                               foreground steal from a background process, in which
//                               case Requested=true but Confirmed=false (NOT an error).
// Note carries any human-readable qualifier (which open strategy won, why
// foreground was/ wasn't confirmed).
internal sealed record OpenPlcObjectResult(
    string TreePath,
    bool Opened,
    bool AlreadyOpen,
    bool ForegroundRequested,
    bool ForegroundConfirmed,
    string? Note);

// Result shape for ClosePlcObject(treePath): the counterpart that tidies up a
// demo by closing an object's editor tab. WasOpen=false means there was nothing
// to close (already shut, or never opened) — reported, not an error. Saved
// reflects whether the document was saved before closing.
internal sealed record ClosePlcObjectResult(
    string TreePath,
    bool Closed,
    bool WasOpen,
    bool Saved,
    string? Note);

// ---- open-object (generalization) -------------------------------------------
// Two fundamentally different "show me this node" mechanisms, because XAE treats
// the two node families differently (proven live):
//   * PlcObject  — a PLC source object (POU/GVL/DUT/ITF). Has its OWN editor
//                  document tab; opened via the file-open fast path. Opened /
//                  AlreadyOpen behave exactly as OpenPlcObject.
//   * SysmanNode — a system-manager node (EtherCAT/IO, Motion/NC, TcCOM, Scope…).
//                  These SHARE one single sysman editor — there is no per-node tab.
//                  "Opening" means SELECTING the node (via UIAutomation) so the one
//                  shared editor re-renders to show it. Opened == node was selected;
//                  AlreadyOpen is always false (no tab concept).
internal enum OpenObjectKind { PlcObject, SysmanNode }

internal sealed record OpenObjectResult(
    string TreePath,
    OpenObjectKind Kind,
    bool Opened,
    bool AlreadyOpen,
    bool ForegroundRequested,
    bool ForegroundConfirmed,
    string? Note);

// Counterpart to OpenObjectResult. For PlcObject this closes the document tab
// (Closed/WasOpen/Saved as ClosePlcObject). For SysmanNode there is no per-node
// tab to close (one shared editor), so WasOpen=false and Note explains it — a
// reported no-op, never an error.
internal sealed record CloseObjectResult(
    string TreePath,
    OpenObjectKind Kind,
    bool Closed,
    bool WasOpen,
    bool Saved,
    string? Note);
