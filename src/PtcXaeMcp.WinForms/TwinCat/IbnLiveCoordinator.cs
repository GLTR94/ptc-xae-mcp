using System.Text.Json;
using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// ibn-live: the host-owned coordinator for the safe, AI-decoupled
/// commissioning control surface. SINGLETON — one instance lives on McpBridge
/// and is shared by the MCP tool layer and the WinForms GUI.
///
/// Asymmetric tool surface (the whole point): the AI may <b>read</b>, <b>propose</b>
/// and <b>stop</b>, but <b>no</b> AI-reachable path triggers a motion.
/// <see cref="Propose"/> only creates a <i>pending</i> proposal and moves nothing;
/// the single benign command bit is written exclusively by
/// <see cref="ConfirmProposal"/>, which the host calls from the modal popup's OK
/// handler. <see cref="ConfirmProposal"/> is deliberately <b>not</b> bound to any
/// MCP tool — there is no AI confirm/execute path.
///
/// Safety state and side-effects live in this host process, never in the AI
/// context. Headless (no GUI subscriber to <see cref="ProposalRaised"/>) ⇒ no
/// confirm path ⇒ read-only by design.
/// </summary>
internal sealed class IbnLiveCoordinator : IDisposable
{
    public const string DefaultGvlPath = "GVL_IBN";
    public const int DefaultAdsPort = 851;
    public const int DefaultArmTtlSeconds = 60;
    public const int MaxArmTtlSeconds = 120;
    public const int HeartbeatPeriodMs = 500;

    /// <summary>Consecutive heartbeat misses (write failed, or threw) before the
    /// watchdog auto-aborts (issue #154). A single transient miss is tolerated —
    /// the PLC-side watchdog window is itself several heartbeat periods wide —
    /// but a run of misses means the connection/PLC is gone and must not be
    /// papered over by an armed-but-silent coordinator.</summary>
    public const int DefaultMaxConsecutiveHeartbeatMisses = 3;

    private static readonly string[] ValidActions = { "start_chain", "next_step", "hold" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ITwinCatSession _session;
    private readonly Func<DateTime> _clock;
    private readonly bool _enableTimers;
    private readonly int _maxConsecutiveHeartbeatMisses;
    private readonly object _gate = new();

    /// <summary>The coordinator-level I/O gate (issue #1). Every IBN write — the arm
    /// enable write, the heartbeat, the confirmed command bit and the stop writes —
    /// runs under it, together with the arm-state check it depends on. A stop path
    /// therefore waits at most for ONE in-flight IBN write (bounded by the ADS
    /// timeout), never for the <see cref="McpDispatcher"/> and the unrelated COM work
    /// (build, activation) it may be serialising, and no write that was decided
    /// before an abort can land after the abort's writes. Lock order: <c>_ioGate</c>
    /// before <see cref="_gate"/>, never the other way round; no event is raised
    /// while it is held.</summary>
    private readonly object _ioGate = new();

    // ── arm state ────────────────────────────────────────────────────────────
    private bool _armed;
    private string? _armToken;
    private DateTime _armExpiresUtc;
    private HashSet<string> _allowlist = new(StringComparer.OrdinalIgnoreCase);
    private string _gvlPath = DefaultGvlPath;
    private string? _amsNetId;
    private int _adsPort = DefaultAdsPort;

    /// <summary>Bumped on every arm/disarm/abort/release. Lets a caller that
    /// released <see cref="_gate"/> to perform a slow ADS call detect — by
    /// re-checking this after re-acquiring the gate — whether the arm/abort
    /// state changed underneath it while it was unlocked (issue #154, E-20).</summary>
    private long _armEpoch;

    /// <summary>Consecutive heartbeat misses since the last success. Reset to 0
    /// on every successful write; on reaching <see cref="_maxConsecutiveHeartbeatMisses"/>
    /// the watchdog auto-aborts exactly like an expired arm window.</summary>
    private int _consecutiveHeartbeatMisses;

    // ── registry + audit ─────────────────────────────────────────────────────
    private readonly Dictionary<string, IbnProposal> _proposals = new(StringComparer.Ordinal);
    private readonly List<IbnAuditEntry> _audit = new();

    // ── heartbeat / watchdog timer (production only) ──────────────────────────
    private System.Threading.Timer? _heartbeatTimer;

    /// <summary>Raised when a proposal becomes pending. The GUI subscribes and
    /// opens the modal confirm popup; its OK handler calls <see cref="ConfirmProposal"/>,
    /// its Cancel handler calls <see cref="RejectProposal"/>. No subscriber ⇒
    /// the proposal simply expires (headless = read-only).</summary>
    public event Action<IbnProposal>? ProposalRaised;

    /// <summary>Raised after any state change (arm/disarm/confirm/abort/expire)
    /// so the GUI can refresh.</summary>
    public event Action? StateChanged;

    /// <summary>Raised for each audit entry so the GUI can append to its log.</summary>
    public event Action<IbnAuditEntry>? Audited;

    public IbnLiveCoordinator(ITwinCatSession session, Func<DateTime>? clock = null, bool enableTimers = false,
        int maxConsecutiveHeartbeatMisses = DefaultMaxConsecutiveHeartbeatMisses)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _clock = clock ?? (() => DateTime.UtcNow);
        _enableTimers = enableTimers;
        _maxConsecutiveHeartbeatMisses = maxConsecutiveHeartbeatMisses <= 0
            ? DefaultMaxConsecutiveHeartbeatMisses : maxConsecutiveHeartbeatMisses;
    }

    // ── derived symbol names (the test PRG contract) ──────────────────────────
    private string EnableSymbol => $"{_gvlPath}.bEnable";   // heartbeat
    private string RequestSymbol => $"{_gvlPath}.bStepReq";  // the single command bit
    private string AbortSymbol => $"{_gvlPath}.bAbort";     // panic / home

    // ── read-only snapshots for the GUI / tests ───────────────────────────────
    public bool IsArmed { get { lock (_gate) return _armed && _clock() < _armExpiresUtc; } }

    public int RemainingSeconds
    {
        get
        {
            lock (_gate)
            {
                if (!_armed) return 0;
                double s = (_armExpiresUtc - _clock()).TotalSeconds;
                return s <= 0 ? 0 : (int)Math.Ceiling(s);
            }
        }
    }

    public string GvlPath { get { lock (_gate) return _gvlPath; } }

    public IReadOnlyList<IbnAuditEntry> AuditSnapshot()
    {
        lock (_gate) return _audit.ToArray();
    }

    public IReadOnlyList<IbnProposal> ProposalsSnapshot()
    {
        lock (_gate) return _proposals.Values.ToArray();
    }

    // ── arm / disarm ──────────────────────────────────────────────────────────

    /// <summary>Open a time-boxed control window. Loads the actuator allowlist and
    /// starts the heartbeat. Without an active (unexpired) arm token every
    /// <see cref="Propose"/> is refused.</summary>
    public string Arm(int? ttlSeconds, IReadOnlyList<string>? actuatorAllowlist,
        string? gvlPath, string? amsNetId, int? adsPort)
    {
        int ttl = ttlSeconds is null or <= 0 ? DefaultArmTtlSeconds : Math.Min(ttlSeconds.Value, MaxArmTtlSeconds);
        string resolvedGvl = string.IsNullOrWhiteSpace(gvlPath) ? _gvlPath : gvlPath.Trim();
        string? resolvedNet = string.IsNullOrWhiteSpace(amsNetId) ? _amsNetId : amsNetId.Trim();
        int resolvedPort = adsPort ?? _adsPort;
        string enableSymbol = $"{resolvedGvl}.bEnable";

        string? refusal = null;
        IbnAuditEntry audit;
        IbnAuditEntry[] superseded = Array.Empty<IbnAuditEntry>();
        object? payload = null;
        // The enable write and the commit run under _ioGate, so an Abort cannot slip in
        // between them and be undone by the commit (issue #1, race 1): it waits for the
        // arm to finish and then disarms it.
        lock (_ioGate)
        {
            // First enable write happens on the heartbeat tick; do one immediately —
            // synchronously, BEFORE any state is committed — so a failed write can
            // never leave the coordinator reporting "armed" while the PRG never saw
            // bEnable go TRUE (issue #154: "Arm reports armed despite a failed enable
            // write").
            var initialWrite = TryWriteResult(enableSymbol, "true", resolvedNet, resolvedPort);
            if (!initialWrite.Success)
            {
                lock (_gate)
                    audit = Record("arm-refused", $"initial enable write failed: {initialWrite.Message}");
                refusal = McpErrors.EnsureError(
                    $"Arm refused — initial enable write to '{enableSymbol}' failed: {initialWrite.Message}");
            }
            else
            {
                lock (_gate)
                {
                    // A proposal belongs to exactly one arm window (issue #2): re-arming
                    // rejects whatever the previous window left pending.
                    superseded = RejectPending("superseded by re-arm");
                    _armed = true;
                    _armToken = Guid.NewGuid().ToString("N");
                    _armExpiresUtc = _clock().AddSeconds(ttl);
                    _allowlist = new HashSet<string>(
                        (actuatorAllowlist ?? Array.Empty<string>())
                            .Select(a => (a ?? string.Empty).Trim())
                            .Where(a => a.Length > 0),
                        StringComparer.OrdinalIgnoreCase);
                    _gvlPath = resolvedGvl;
                    // Cache the NetId the write actually used, so the heartbeat and the
                    // stop paths never resolve it again ("project" reads the solution
                    // through COM, which a busy XAE rejects — issue #1, race 3).
                    _amsNetId = initialWrite.NetId ?? resolvedNet;
                    _adsPort = resolvedPort;
                    _consecutiveHeartbeatMisses = 0;
                    _armEpoch++;
                    audit = Record("arm", $"ttl={ttl}s gvl={_gvlPath} net={_amsNetId ?? "local"} allowlist=[{string.Join(",", _allowlist)}]");
                    payload = new
                    {
                        armed = true,
                        armToken = _armToken,
                        expiresUtc = _armExpiresUtc,
                        ttlSeconds = ttl,
                        gvlPath = _gvlPath,
                        allowlist = _allowlist.ToArray(),
                    };
                }
                StartHeartbeat();
            }
        }
        foreach (var s in superseded) RaiseAudited(s);
        RaiseAudited(audit);
        if (refusal is not null) return refusal;
        StateChanged?.Invoke();
        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    /// <summary>Close the control window: stop the heartbeat and command the PRG
    /// home (abort). Disarm is one of the watchdog triggers, so it homes the
    /// machine rather than just dropping enable.</summary>
    public string Disarm() => AbortInternal("disarm", clearRegistry: false);

    // ── propose / status ──────────────────────────────────────────────────────

    /// <summary>Create a <i>pending</i> step-trigger proposal. Moves <b>nothing</b>.
    /// Requires an active arm token; an actuator id must be on the allowlist.
    /// Opens the host popup via <see cref="ProposalRaised"/> and returns
    /// { proposalId, status: "pending" }.</summary>
    public string Propose(string? action, string? chainId, string? actuatorId)
    {
        IbnProposal created;
        IbnAuditEntry audit;
        lock (_gate)
        {
            if (!_armedNow())
                return "[Error] Not armed (or arm window expired). Arming happens only in the host window " +
                       "(Approvals view) — ask the operator to arm; there is no MCP tool for it.";

            string act = (action ?? string.Empty).Trim().ToLowerInvariant();
            if (!ValidActions.Contains(act))
                return $"[Error] Unknown action '{action}'. Valid: {string.Join(", ", ValidActions)}.";

            string? actuator = string.IsNullOrWhiteSpace(actuatorId) ? null : actuatorId.Trim();
            if (actuator is not null && !_allowlist.Contains(actuator))
                return $"[Error] Actuator '{actuator}' is not on the armed allowlist — proposal refused.";

            string id = Guid.NewGuid().ToString("N")[..8];
            created = new IbnProposal(id, act,
                string.IsNullOrWhiteSpace(chainId) ? null : chainId.Trim(),
                actuator, IbnProposalState.Pending, _clock(), _armExpiresUtc, null);
            _proposals[id] = created;
            audit = Record("propose", $"id={id} action={act} chain={created.ChainId ?? "-"} actuator={actuator ?? "-"}");
        }
        // Raise OUTSIDE the lock: the GUI handler runs the modal popup and may
        // call back into ConfirmProposal/RejectProposal on another thread.
        RaiseAudited(audit);
        ProposalRaised?.Invoke(created);
        StateChanged?.Invoke();
        return JsonSerializer.Serialize(new { proposalId = created.ProposalId, status = "pending" }, JsonOpts);
    }

    /// <summary>Read-only poll of a proposal's state. Expires a stale pending
    /// proposal as a side effect of reading it.</summary>
    public string GetProposalStatus(string? proposalId)
    {
        lock (_gate)
        {
            string id = (proposalId ?? string.Empty).Trim();
            if (id.Length == 0 || !_proposals.TryGetValue(id, out var p))
                return $"[Error] Unknown proposalId '{proposalId}'.";

            if (p.State == IbnProposalState.Pending && _clock() >= p.ExpiresUtc)
            {
                p = p with { State = IbnProposalState.Expired };
                _proposals[id] = p;
            }

            return JsonSerializer.Serialize(new
            {
                proposalId = p.ProposalId,
                status = StateName(p.State),
                action = p.Action,
                chainId = p.ChainId,
                actuatorId = p.ActuatorId,
                createdUtc = p.CreatedUtc,
                expiresUtc = p.ExpiresUtc,
                result = p.Result,
            }, JsonOpts);
        }
    }

    // ── list active chains (read-only) ────────────────────────────────────────

    /// <summary>Read-only view of the running test chains: snapshots the status
    /// struct (running / active chain / current step / fault) via the batch read.
    /// Moves nothing. Uses the armed gvlPath/target unless overridden.</summary>
    public string ListActiveChains(string? gvlPath = null, string? amsNetId = null, int? adsPort = null)
    {
        string gvl;
        string? net;
        int port;
        lock (_gate)
        {
            gvl = string.IsNullOrWhiteSpace(gvlPath) ? _gvlPath : gvlPath.Trim();
            net = string.IsNullOrWhiteSpace(amsNetId) ? _amsNetId : amsNetId.Trim();
            port = adsPort ?? _adsPort;
        }

        string runningSym = $"{gvl}.bChainRunning";
        string activeSym = $"{gvl}.nActiveChain";
        string stepSym = $"{gvl}.nCurrentStep";
        string faultSym = $"{gvl}.bFault";
        var symbols = new[] { runningSym, activeSym, stepSym, faultSym };

        var r = _session.ReadAdsVariablesBatch(symbols, net, port);
        if (!r.Success)
            return McpErrors.EnsureError(r.Message);

        object? Value(string s) => r.Results.FirstOrDefault(i => i.Success &&
            string.Equals(i.Symbol, s, StringComparison.OrdinalIgnoreCase))?.Value;

        return JsonSerializer.Serialize(new
        {
            success = true,
            gvlPath = gvl,
            chain = new
            {
                running = Value(runningSym),
                activeChain = Value(activeSym),
                currentStep = Value(stepSym),
                fault = Value(faultSym),
            },
            raw = r.Results.Select(i => new { symbol = i.Symbol, value = i.Value, ok = i.Success, message = i.Message }),
        }, JsonOpts);
    }

    // ── host-only confirm / reject (NO MCP binding) ───────────────────────────

    /// <summary>HOST-ONLY. Called by the modal popup's OK handler — never by any
    /// MCP tool. Writes <b>exactly one</b> benign command bit (the step request)
    /// to the test PRG. Refuses unless the proposal is pending and the arm window
    /// is still open.</summary>
    public string ConfirmProposal(string proposalId)
    {
        string requestSymbol = string.Empty;
        string? net = null;
        int port = 0;
        long epochAtDecision = 0;
        IbnAuditEntry audit;
        IbnProposal? confirmed = null;
        string? refusal = null;
        lock (_gate)
        {
            string id = (proposalId ?? string.Empty).Trim();
            if (id.Length == 0 || !_proposals.TryGetValue(id, out var p))
            {
                audit = Record("confirm-refused", $"unknown proposalId '{proposalId}'");
                refusal = $"[Error] Unknown proposalId '{proposalId}'.";
            }
            else if (p.State != IbnProposalState.Pending)
            {
                audit = Record("confirm-refused", $"id={id} state={StateName(p.State)} (not pending)");
                refusal = $"[Error] Proposal '{id}' is {StateName(p.State)}, not pending.";
            }
            // A proposal lives only as long as the arm window it was raised in (issue #2):
            // its own expiry is checked, not just whether some window is open now.
            else if (!_armedNow() || _clock() >= p.ExpiresUtc)
            {
                _proposals[id] = p with { State = IbnProposalState.Expired };
                audit = Record("confirm-refused", $"id={id} arm window closed");
                refusal = $"[Error] Arm window closed — proposal '{id}' expired, nothing written.";
            }
            else if (p.ActuatorId is not null && !_allowlist.Contains(p.ActuatorId))
            {
                audit = Record("confirm-refused", $"id={id} actuator '{p.ActuatorId}' off allowlist");
                refusal = $"[Error] Actuator '{p.ActuatorId}' is no longer on the allowlist.";
            }
            else
            {
                requestSymbol = RequestSymbol;
                net = _amsNetId;
                port = _adsPort;
                epochAtDecision = _armEpoch;
                confirmed = p with { State = IbnProposalState.Confirmed };
                _proposals[id] = confirmed;
                audit = Record("confirm", $"id={id} writing single bit {requestSymbol}:=TRUE");
            }
        }

        RaiseAudited(audit);
        if (refusal is not null) return refusal;
        IbnProposal decided = confirmed!; // set on the only branch that leaves refusal null

        AdsVariableResult? wr = null;
        IbnAuditEntry? lateRefusal = null;
        // E-20 / issue #1 race 2: a subscriber to Audited (the GUI) runs synchronously
        // on this call and could itself invoke Abort/Disarm right here, and another
        // thread can abort at any moment. The epoch re-check and the write run under
        // _ioGate, which every stop path also takes: either the abort comes first and
        // the re-check refuses, or the write completes first and the abort's
        // bStepReq:=FALSE follows it. A decided write can never land after an abort.
        lock (_ioGate)
        {
            lock (_gate)
            {
                if (_armEpoch != epochAtDecision || !_armedNow())
                {
                    if (_proposals.TryGetValue(decided.ProposalId, out var cur) && cur.State == IbnProposalState.Confirmed)
                        _proposals[decided.ProposalId] = cur with { State = IbnProposalState.Rejected, Result = "aborted before write" };
                    lateRefusal = Record("confirm-refused", $"id={decided.ProposalId} arm/abort state changed just before write — not writing");
                }
            }

            // The ONE and ONLY write triggered by a proposal — a normal symbolic
            // write to a benign control bit, not a force.
            if (lateRefusal is null)
                wr = WriteNoThrow(requestSymbol, "true", net, port);
        }

        if (lateRefusal is not null)
        {
            RaiseAudited(lateRefusal);
            return $"[Error] Arm/abort state changed just before writing — proposal '{decided.ProposalId}' not executed.";
        }
        string resultMsg = wr!.Success ? "OK" : McpErrors.EnsureError(wr.Message);

        IbnAuditEntry done;
        lock (_gate)
        {
            if (_proposals.TryGetValue(decided.ProposalId, out var cur))
                _proposals[decided.ProposalId] = cur with { State = IbnProposalState.Executed, Result = resultMsg };
            done = Record("executed", $"id={decided.ProposalId} result={resultMsg}");
        }
        RaiseAudited(done);
        StateChanged?.Invoke();
        return wr.Success
            ? JsonSerializer.Serialize(new { proposalId = decided.ProposalId, status = "executed", result = resultMsg }, JsonOpts)
            : resultMsg;
    }

    /// <summary>HOST-ONLY. Called by the popup's Cancel handler. Marks the
    /// proposal rejected; writes nothing.</summary>
    public string RejectProposal(string proposalId, string? reason = null)
    {
        IbnAuditEntry audit;
        string? refusal = null;
        lock (_gate)
        {
            string id = (proposalId ?? string.Empty).Trim();
            if (id.Length == 0 || !_proposals.TryGetValue(id, out var p))
                return $"[Error] Unknown proposalId '{proposalId}'.";
            // E-27: only a still-pending proposal can be rejected — an already
            // executed/confirmed/expired/rejected proposal must not silently
            // flip state (e.g. rejecting a proposal the host already executed).
            if (p.State != IbnProposalState.Pending)
            {
                audit = Record("reject-refused", $"id={id} state={StateName(p.State)} (not pending)");
                refusal = $"[Error] Proposal '{id}' is {StateName(p.State)}, not pending — cannot reject.";
            }
            else
            {
                _proposals[id] = p with { State = IbnProposalState.Rejected, Result = reason };
                audit = Record("reject", $"id={id} reason={reason ?? "-"}");
            }
        }
        RaiseAudited(audit);
        if (refusal is not null) return refusal;
        StateChanged?.Invoke();
        return JsonSerializer.Serialize(new { proposalId, status = "rejected" }, JsonOpts);
    }

    // ── panic abort (NOT gated by arm) ────────────────────────────────────────

    /// <summary>Panic abort: command the test PRG home/abort immediately. Not
    /// gated by the arm token — stopping is always allowed.</summary>
    public string Abort() => AbortInternal("abort", clearRegistry: false);

    /// <summary>Panic abort + release every pending proposal.</summary>
    public string ReleaseAll() => AbortInternal("release_all", clearRegistry: true);

    private string AbortInternal(string kind, bool clearRegistry)
    {
        string abortSymbol;
        string enableSymbol;
        string requestSymbol;
        IbnAuditEntry audit;
        AdsVariableResult wr, requestOff, enableOff;
        // Issue #1: the stop paths no longer go through the McpDispatcher; they take only
        // _ioGate, so they wait at most for one in-flight IBN write, never for a build or
        // an activation. The NetId was resolved at arm time, so nothing here touches COM.
        lock (_ioGate)
        {
            string? net;
            int port;
            lock (_gate)
            {
                abortSymbol = AbortSymbol;
                enableSymbol = EnableSymbol;
                requestSymbol = RequestSymbol;
                net = _amsNetId;
                port = _adsPort;
                // Reject anything still pending.
                foreach (var key in _proposals.Keys.ToArray())
                {
                    var p = _proposals[key];
                    if (p.State is IbnProposalState.Pending or IbnProposalState.Confirmed)
                        _proposals[key] = p with { State = IbnProposalState.Rejected, Result = kind };
                }
                if (clearRegistry) _proposals.Clear();
                _armed = false;
                _armToken = null;
                _allowlist.Clear();
                _consecutiveHeartbeatMisses = 0;
                _armEpoch++;
                audit = Record(kind, $"writing {abortSymbol}:=TRUE, {requestSymbol}:=FALSE, {enableSymbol}:=FALSE; heartbeat stopped");
            }
            StopHeartbeat();
            wr = WriteNoThrow(abortSymbol, "true", net, port);

            // A step request confirmed just before the abort must not stay latched
            // (issue #1, race 2) — and bEnable must go FALSE on every disarm/abort
            // (issue #154): the PRG's own watchdog reacts to enable dropping. Both are
            // best-effort: a failure is recorded but does not change the abort's own
            // reported result (the abort bit carries the safety-relevant intent).
            requestOff = WriteNoThrow(requestSymbol, "false", net, port);
            enableOff = WriteNoThrow(enableSymbol, "false", net, port);
        }

        RaiseAudited(audit);
        foreach (var (failed, suffix) in new[] { (requestOff, "steprequest-off-failed"), (enableOff, "enable-off-failed") })
        {
            if (failed.Success) continue;
            IbnAuditEntry a;
            lock (_gate) a = Record($"{kind}-{suffix}", failed.Message);
            RaiseAudited(a);
        }

        string resultMsg = wr.Success ? "OK" : McpErrors.EnsureError(wr.Message);
        StateChanged?.Invoke();
        return wr.Success
            ? JsonSerializer.Serialize(new { kind, armed = false, abortSymbol, result = resultMsg }, JsonOpts)
            : resultMsg;
    }

    // ── heartbeat / watchdog ──────────────────────────────────────────────────

    /// <summary>One heartbeat tick. While armed and not expired, writes
    /// <c>bEnable:=TRUE</c>. On expiry it acts as the watchdog and aborts (PRG
    /// homes). Exposed internally so unit tests can drive ticks deterministically
    /// without real timers.</summary>
    internal void HeartbeatTick()
    {
        try
        {
            HeartbeatTickCore();
        }
        finally
        {
            // Single-flight (issue #154, E-7): re-arm the one-shot timer only
            // AFTER this tick fully finished (including any write/abort it
            // triggered), so a slow write can never overlap with the next
            // tick — no reentrant timer, no unbounded parallel ADS calls.
            // Skipped once disarmed/aborted (StopHeartbeat already nulled it).
            // Under _ioGate, like every Start/StopHeartbeat, so a concurrent abort
            // cannot dispose the timer between the null check and Change().
            lock (_ioGate)
                _heartbeatTimer?.Change(HeartbeatPeriodMs, Timeout.Infinite);
        }
    }

    private void HeartbeatTickCore()
    {
        bool expired;
        AdsVariableResult? wr = null;
        // The armed check and the bEnable:=TRUE write run under _ioGate (issue #1):
        // otherwise a tick that saw "armed" could write enable TRUE after an
        // abort had already written it FALSE.
        lock (_ioGate)
        {
            string enableSymbol;
            string? net;
            int port;
            lock (_gate)
            {
                if (!_armed) return;
                expired = _clock() >= _armExpiresUtc;
                enableSymbol = EnableSymbol;
                net = _amsNetId;
                port = _adsPort;
            }
            // Never throws — this runs on a timer thread.
            if (!expired)
                wr = WriteNoThrow(enableSymbol, "true", net, port);
        }

        if (expired)
        {
            AbortInternal("heartbeat-watchdog (arm expired)", clearRegistry: false);
            return;
        }

        bool success = wr!.Success;
        string message = wr.Message;

        if (success)
        {
            lock (_gate) _consecutiveHeartbeatMisses = 0;
            return;
        }

        // Heartbeat write failed — either it threw, or (the common real-world
        // case, issue #154) WriteAdsVariable returned Success=false without
        // throwing at all, which the old exception-only detection missed
        // entirely (silent heartbeat failure). Count consecutive misses and
        // auto-abort once the threshold is reached instead of leaving the
        // coordinator reporting "armed" against a PRG that stopped seeing
        // enable long ago.
        int misses;
        IbnAuditEntry lossEntry;
        lock (_gate)
        {
            misses = ++_consecutiveHeartbeatMisses;
            lossEntry = Record("heartbeat-loss", $"{message} (consecutive misses: {misses}/{_maxConsecutiveHeartbeatMisses})");
        }
        RaiseAudited(lossEntry);

        if (misses >= _maxConsecutiveHeartbeatMisses)
        {
            AbortInternal($"heartbeat-watchdog ({misses} consecutive misses)", clearRegistry: false);
        }
    }

    private void StartHeartbeat()
    {
        if (!_enableTimers) return;
        StopHeartbeat();
        // One-shot, re-armed by HeartbeatTick itself after each tick completes
        // (issue #154, E-7) — NOT a reentrant periodic timer. period=Infinite
        // means "fire once at dueTime, then wait for an explicit Change()".
        _heartbeatTimer = new System.Threading.Timer(_ => HeartbeatTick(), null,
            HeartbeatPeriodMs, Timeout.Infinite);
    }

    private void StopHeartbeat()
    {
        var t = _heartbeatTimer;
        _heartbeatTimer = null;
        t?.Dispose();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private bool _armedNow() => _armed && _clock() < _armExpiresUtc;

    /// <summary>Writes a symbol against explicit net/port (not the instance
    /// fields) and never throws — used by <see cref="Arm"/> for the initial
    /// enable write, which must be evaluated honestly BEFORE any arm state is
    /// committed (issue #154). <c>NetId</c> is the NetId the write resolved and
    /// used, null when unknown.</summary>
    private (bool Success, string Message, string? NetId) TryWriteResult(string symbol, string value, string? net, int port)
    {
        var r = WriteNoThrow(symbol, value, net, port);
        return (r.Success, r.Success ? "OK" : r.Message,
            r.Success && !string.IsNullOrWhiteSpace(r.AmsNetId) ? r.AmsNetId : null);
    }

    /// <summary>An IBN write that reports a thrown exception as a failed result,
    /// so a stop path always gets to its next write.</summary>
    private AdsVariableResult WriteNoThrow(string symbol, string value, string? net, int port)
    {
        try
        {
            return _session.WriteAdsVariable(symbol, value, net, port);
        }
        catch (Exception ex)
        {
            return new AdsVariableResult(false, symbol, null, null, net ?? string.Empty, port, ex.Message);
        }
    }

    /// <summary>Rejects every pending proposal (issue #2: a proposal belongs to
    /// exactly one arm window). Caller holds <see cref="_gate"/> and raises the
    /// returned audit entries after releasing it.</summary>
    private IbnAuditEntry[] RejectPending(string reason)
    {
        var entries = new List<IbnAuditEntry>();
        foreach (var key in _proposals.Keys.ToArray())
        {
            var p = _proposals[key];
            if (p.State != IbnProposalState.Pending) continue;
            _proposals[key] = p with { State = IbnProposalState.Rejected, Result = reason };
            entries.Add(Record("reject", $"id={key} reason={reason}"));
        }
        return entries.ToArray();
    }

    private const int MaxAuditEntries = 2000;

    /// <summary>Append an audit entry. Caller must hold <see cref="_gate"/>. The
    /// trail is capped so a long-armed session (heartbeat-loss spam against a
    /// missing runtime) cannot grow it without bound.</summary>
    private IbnAuditEntry Record(string kind, string detail)
    {
        var e = new IbnAuditEntry(_clock(), kind, detail);
        _audit.Add(e);
        if (_audit.Count > MaxAuditEntries)
            _audit.RemoveRange(0, _audit.Count - MaxAuditEntries);
        return e;
    }

    /// <summary>Raise <see cref="Audited"/>. Never called while holding
    /// <see cref="_gate"/> or <see cref="_ioGate"/> (issue #2).</summary>
    private void RaiseAudited(IbnAuditEntry e) => Audited?.Invoke(e);

    private static string StateName(IbnProposalState s) => s switch
    {
        IbnProposalState.Pending => "pending",
        IbnProposalState.Confirmed => "confirmed",
        IbnProposalState.Executed => "executed",
        IbnProposalState.Rejected => "rejected",
        IbnProposalState.Expired => "expired",
        _ => s.ToString().ToLowerInvariant(),
    };

    public void Dispose()
    {
        lock (_ioGate) StopHeartbeat();
    }
}
