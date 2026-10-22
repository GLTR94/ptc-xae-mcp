namespace PtcXaeMcp.WinForms.TwinCat;

// ---- ibn-live --------------------------------------------------------------
// Types for the safe, AI-decoupled commissioning (Inbetriebnahme) control
// surface. The guiding invariant: the AI may read, propose and stop, but NO
// path here triggers a motion. A proposal is executed only by the host, after
// a human confirms the modal popup — see IbnLiveCoordinator.ConfirmProposal,
// which is deliberately NOT exposed as an MCP tool.

/// <summary>Lifecycle of a step-trigger proposal. Only the host (human) can
/// advance Pending → Executed via ConfirmProposal; the AI can only read it.</summary>
internal enum IbnProposalState
{
    Pending,    // created by twincat_ibn_propose; awaiting human confirm
    Confirmed,  // human clicked OK; command bit about to be written
    Executed,   // command bit written to the test PRG
    Rejected,   // human clicked Cancel, or the proposal was superseded
    Expired,    // the arm window closed before confirmation
}

/// <summary>A pending step trigger. Carries only metadata for the human's
/// decision + the audit trail; it never itself moves anything.</summary>
internal sealed record IbnProposal(
    string ProposalId,
    string Action,          // start_chain | next_step | hold
    string? ChainId,
    string? ActuatorId,
    IbnProposalState State,
    DateTime CreatedUtc,
    DateTime ExpiresUtc,
    string? Result);        // read-back / write message once executed

/// <summary>One line of the host-owned audit trail (arm, propose, confirm,
/// reject, abort, heartbeat-loss, …). Lives in the host process only.</summary>
internal sealed record IbnAuditEntry(
    DateTime TimestampUtc,
    string Kind,
    string Detail);

/// <summary>Result of twincat_measure_signal_transition — elapsed time between
/// two symbol conditions (edge A → edge B), bounded by a timeout.</summary>
internal sealed record SignalTransitionResult(
    bool Success,
    double ElapsedMs,
    bool TimedOut,
    string Message);
