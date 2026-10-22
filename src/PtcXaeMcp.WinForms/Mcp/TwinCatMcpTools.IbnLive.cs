using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

// ibn-live: the safe, AI-decoupled commissioning control surface.
//
// ASYMMETRIC SURFACE — the AI may read, propose and stop, but NO tool here
// arms the control window or triggers a motion. Arming happens only in the host
// window (Approvals view, issue #272). There is deliberately NO confirm/execute
// tool: a proposal is executed only by the host, after a human confirms the modal popup
// (IbnLiveCoordinator.ConfirmProposal, host-only, no MCP binding). This is a
// gate-relevant invariant guarded by IbnLiveMcpTests.
//
// All tools delegate to the singleton bridge.LiveCoordinator so state (proposal
// registry, arm token, heartbeat) lives in the host process, never in the AI
// context.
//
// STOP PATHS ARE NOT DISPATCHED (issue #1): disarm / abort / release_all bypass the
// McpDispatcher lock and the test gate. While a build or an activation holds the
// dispatcher, a dispatched ABORT could only answer "busy". The coordinator's own I/O
// gate serialises them against the other IBN writes instead, and the NetId was
// resolved at arm time, so they never touch COM. Pinned by IbnArmSurfaceTests.
internal sealed partial class TwinCatMcpTools
{
    // twincat_ibn_arm is NOT part of this surface (decision 2026-10-19, issue #272): IBN Live
    // is armed only by a human in the host window (Approvals view). The tool moved to
    // TwinCatMcpToolsDiagnostics (Mcp/TwinCatMcpTools.Diagnostics.cs) as test support for the
    // live suite and is registered only with --diagnostics / PTC_XAE_MCP_DIAGNOSTICS=1.

    [McpServerTool(Name = "twincat_ibn_disarm", ReadOnly = false, Destructive = true)]
    [Description("Close the IBN control window: stop the heartbeat and command the test PRG home " +
                 "(disarm is a watchdog trigger, so it returns the machine to its home position). " +
                 "Always allowed — it reduces risk. Arming happens in the host window (Approvals view); " +
                 "there is no MCP tool to arm. Returns JSON { kind, armed:false, abortSymbol, result }.")]
    public string IbnDisarm(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_ibn_disarm", new { },
            () => bridge.LiveCoordinator.Disarm(), cancellationToken, bypassGate: true);

    [McpServerTool(Name = "twincat_ibn_propose", ReadOnly = false, Destructive = true)]
    [Description("Propose a step trigger. Moves NOTHING — it creates a PENDING proposal, opens the " +
                 "host's modal confirm popup, and returns JSON { proposalId, status: \"pending\" }. " +
                 "The trigger is executed ONLY if a human confirms the popup; there is no AI path to " +
                 "execute it. Requires an active arm window, which only a human opens in the host " +
                 "window (Approvals view) — there is no MCP tool to arm; without it the proposal is " +
                 "refused with an [Error]. An actuator id must be on the allowlist the operator armed. " +
                 "Poll twincat_ibn_get_proposal_status for the outcome.")]
    public string IbnPropose(
        [Description("Step action: 'start_chain' | 'next_step' | 'hold'.")] string action,
        [Description("Optional id of the test chain this trigger targets.")] string? chainId = null,
        [Description("Optional actuator id; if given it must be on the armed allowlist.")] string? actuatorId = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_ibn_propose", new { action, chainId, actuatorId }, () =>
            bridge.Dispatcher.Run(() => bridge.LiveCoordinator.Propose(action, chainId, actuatorId)), cancellationToken);

    [McpServerTool(Name = "twincat_ibn_get_proposal_status", ReadOnly = true)]
    [Description("Read-only poll of a proposal: returns JSON { proposalId, status, action, chainId, " +
                 "actuatorId, createdUtc, expiresUtc, result }. Status is one of " +
                 "pending | confirmed | executed | rejected | expired. Reads only; moves nothing.")]
    public string IbnGetProposalStatus(
        [Description("The proposalId returned by twincat_ibn_propose.")] string proposalId,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_ibn_get_proposal_status", new { proposalId }, () =>
            bridge.Dispatcher.Run(() => bridge.LiveCoordinator.GetProposalStatus(proposalId)), cancellationToken);

    [McpServerTool(Name = "twincat_list_active_chains", ReadOnly = true)]
    [Description("Read-only snapshot of the running IBN test chains from the test PRG status " +
                 "struct: returns JSON { success, gvlPath, chain: { running, activeChain, " +
                 "currentStep, fault }, raw: [{ symbol, value, ok, message }] }. Reads the gvlPath/target " +
                 "armed in the host window unless overridden. Moves nothing.")]
    public string ListActiveChains(
        [Description("Base symbol path of the test PRG status struct. Omit to use the armed gvlPath " +
                     "(default 'GVL_IBN').")] string? gvlPath = null,
        [Description("Optional AMS Net ID. Omit for the armed/local target.")] string? amsNetId = null,
        [Description("ADS port of the PLC runtime. Default 851.")] int? adsPort = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_list_active_chains", new { gvlPath, amsNetId, adsPort }, () =>
            bridge.Dispatcher.Run(() => bridge.LiveCoordinator.ListActiveChains(gvlPath, amsNetId, adsPort)), cancellationToken);

    [McpServerTool(Name = "twincat_measure_signal_transition", ReadOnly = true)]
    [Description("Measure the elapsed time between two symbol conditions (edge A → edge B) by " +
                 "polling, bounded by a timeout. A READ-ONLY cross-check / fallback for the test " +
                 "PRG's internal PLC-timer measurement (which is more deterministic). Moves nothing. " +
                 "Returns JSON { success, elapsedMs, timedOut, message } or an [Error] string. " +
                 "Conditions compare the symbol's rendered value to the *Equals string (e.g. 'true').")]
    public string MeasureSignalTransition(
        [Description("Symbol whose condition starts the measurement, e.g. 'GVL_IO.bCyl1Cmd'.")] string startSymbol,
        [Description("Symbol whose condition ends the measurement, e.g. 'GVL_IO.bCyl1Extended'.")] string endSymbol,
        [Description("Value the start symbol must equal to begin timing. Default 'true'.")] string? startEquals = null,
        [Description("Value the end symbol must equal to stop timing. Default 'true'.")] string? endEquals = null,
        [Description("Overall timeout in milliseconds (default 5000, max 60000).")] int? timeoutMs = null,
        [Description("Optional AMS Net ID. Omit for the local TwinCAT runtime.")] string? amsNetId = null,
        [Description("ADS port of the PLC runtime. Default 851.")] int? adsPort = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_measure_signal_transition",
            new { startSymbol, endSymbol, startEquals, endEquals, timeoutMs, amsNetId, adsPort }, () =>
            bridge.Dispatcher.Run(() =>
                new TwinCat.AdsVariableCoordinator(bridge.Dispatcher.Session)
                    .MeasureSignalTransition(startSymbol, startEquals, endSymbol, endEquals, timeoutMs, amsNetId, adsPort)), cancellationToken);

    [McpServerTool(Name = "twincat_ibn_abort", ReadOnly = false, Destructive = true)]
    [Description("Panic abort: command the test PRG to home/abort IMMEDIATELY and disarm. Stopping is " +
                 "NOT gated by the arm token — it always works. Re-arming happens only in the host window " +
                 "(Approvals view). Returns JSON { kind, armed:false, abortSymbol, result }.")]
    public string IbnAbort(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_ibn_abort", new { },
            () => bridge.LiveCoordinator.Abort(), cancellationToken, bypassGate: true);

    [McpServerTool(Name = "twincat_ibn_release_all", ReadOnly = false, Destructive = true)]
    [Description("Panic abort AND release every pending proposal: homes the test PRG, rejects all " +
                 "pending proposals and clears the registry. Not gated by the arm token. Re-arming " +
                 "happens only in the host window (Approvals view). " +
                 "Returns JSON { kind, armed:false, abortSymbol, result }.")]
    public string IbnReleaseAll(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_ibn_release_all", new { },
            () => bridge.LiveCoordinator.ReleaseAll(), cancellationToken, bypassGate: true);
}
