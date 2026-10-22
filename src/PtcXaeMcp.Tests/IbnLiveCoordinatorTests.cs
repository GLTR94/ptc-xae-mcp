using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// ibn-live: the safety logic must be provable WITHOUT hardware. These tests
// drive a FakeTwinCatSession + an injectable clock and assert the asymmetric
// invariant: read/propose/stop are allowed, but no AI-reachable path moves the
// machine — only the host-only ConfirmProposal writes the single command bit.
public class IbnLiveCoordinatorTests
{
    private sealed class Clock(DateTime start)
    {
        public DateTime Now { get; set; } = start;
        public DateTime Read() => Now;
    }

    private static readonly DateTime T0 = new(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

    private static (IbnLiveCoordinator coord, FakeTwinCatSession fake, Clock clock) NewCoord()
    {
        var fake = new FakeTwinCatSession();
        var clock = new Clock(T0);
        // enableTimers:false ⇒ no background heartbeat; tests drive ticks explicitly.
        var coord = new IbnLiveCoordinator(fake, clock.Read);
        return (coord, fake, clock);
    }

    private static int Writes(FakeTwinCatSession f, string contains) =>
        f.CallLog.Count(c => c.StartsWith("WriteAdsVariable", StringComparison.Ordinal) && c.Contains(contains));

    private static string ProposalId(string proposeJson) =>
        JsonDocument.Parse(proposeJson).RootElement.GetProperty("proposalId").GetString()!;

    // ── arm / disarm ──────────────────────────────────────────────────────────

    [Fact]
    public void Arm_ReturnsTokenAndExpiry_AndWritesEnableOnce()
    {
        var (coord, fake, _) = NewCoord();

        var json = coord.Arm(90, new[] { "Cyl1", "Cyl2" }, "GVL_IBN", null, null);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("armed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("armToken").GetString()));
        Assert.Equal(90, root.GetProperty("ttlSeconds").GetInt32());
        Assert.True(coord.IsArmed);
        // arm primes the heartbeat with one immediate bEnable write — and nothing else.
        Assert.Equal(1, Writes(fake, "GVL_IBN.bEnable"));
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
        Assert.Equal(0, Writes(fake, "bAbort"));
    }

    [Fact]
    public void Arm_ClampsTtlToMax120()
    {
        var (coord, _, _) = NewCoord();

        var json = coord.Arm(9999, null, null, null, null);

        Assert.Equal(IbnLiveCoordinator.MaxArmTtlSeconds,
            JsonDocument.Parse(json).RootElement.GetProperty("ttlSeconds").GetInt32());
    }

    [Fact]
    public void Disarm_AbortsAndStopsHeartbeat()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);

        var json = coord.Disarm();

        Assert.DoesNotContain("[Error]", json);
        Assert.False(coord.IsArmed);
        Assert.Equal(1, Writes(fake, "GVL_IBN.bAbort")); // disarm is a watchdog trigger → home
    }

    // ── propose: moves nothing ─────────────────────────────────────────────────

    [Fact]
    public void Propose_WithoutArm_Refused_AndWritesNothing()
    {
        var (coord, fake, _) = NewCoord();

        var json = coord.Propose("next_step", "chainA", null);

        Assert.StartsWith("[Error]", json);
        Assert.Contains("armed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog); // truly nothing happened
    }

    [Fact]
    public void Propose_WhenArmed_ReturnsPending_AndMovesNothing()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        int writesBefore = fake.CallLog.Count(c => c.StartsWith("WriteAdsVariable", StringComparison.Ordinal));

        var json = coord.Propose("start_chain", "chainA", "Cyl1");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("pending", doc.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("proposalId").GetString()));
        // The defining invariant: proposing triggers NO additional write.
        int writesAfter = fake.CallLog.Count(c => c.StartsWith("WriteAdsVariable", StringComparison.Ordinal));
        Assert.Equal(writesBefore, writesAfter);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
    }

    [Fact]
    public void Propose_UnknownAction_Refused()
    {
        var (coord, _, _) = NewCoord();
        coord.Arm(60, null, null, null, null);

        var json = coord.Propose("LAUNCH_MISSILE", null, null);

        Assert.StartsWith("[Error]", json);
        Assert.Contains("action", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Propose_ActuatorOffAllowlist_Refused()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);

        var json = coord.Propose("next_step", "chainA", "Cyl9");

        Assert.StartsWith("[Error]", json);
        Assert.Contains("allowlist", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
    }

    [Fact]
    public void Propose_AfterArmExpiry_Refused()
    {
        var (coord, fake, clock) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);

        clock.Now = T0.AddSeconds(61); // arm window closed

        var json = coord.Propose("next_step", "chainA", "Cyl1");

        Assert.StartsWith("[Error]", json);
        Assert.False(coord.IsArmed);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
    }

    [Fact]
    public void Propose_RaisesProposalRaisedEvent()
    {
        var (coord, _, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        IbnProposal? raised = null;
        coord.ProposalRaised += p => raised = p;

        coord.Propose("next_step", "chainA", "Cyl1");

        Assert.NotNull(raised);
        Assert.Equal("next_step", raised!.Action);
        Assert.Equal(IbnProposalState.Pending, raised.State);
    }

    // ── ConfirmProposal: host-only, exactly one command bit ─────────────────────

    [Fact]
    public void ConfirmProposal_WritesExactlyOneCommandBit()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        // Confirm is the ONLY path that may write the command bit.
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
        var json = coord.ConfirmProposal(id);

        Assert.DoesNotContain("[Error]", json);
        Assert.Equal("executed", JsonDocument.Parse(json).RootElement.GetProperty("status").GetString());
        Assert.Equal(1, Writes(fake, "GVL_IBN.bStepReq, val=true")); // EXACTLY one
        // and the value written was the benign request bit set true
        Assert.Contains(fake.CallLog, c => c.Contains("WriteAdsVariable(sym=GVL_IBN.bStepReq, val=true"));
    }

    [Fact]
    public void ConfirmProposal_UnknownId_Refused_WritesNothing()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);

        var json = coord.ConfirmProposal("deadbeef");

        Assert.StartsWith("[Error]", json);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
    }

    [Fact]
    public void ConfirmProposal_AfterArmExpiry_Refused_WritesNothing()
    {
        var (coord, fake, clock) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        clock.Now = T0.AddSeconds(61);
        var json = coord.ConfirmProposal(id);

        Assert.StartsWith("[Error]", json);
        Assert.Contains("expired", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
        // proposal is now expired
        Assert.Contains("expired", coord.GetProposalStatus(id), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmProposal_Twice_SecondRefused_OnlyOneWrite()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        coord.ConfirmProposal(id);
        var second = coord.ConfirmProposal(id);

        Assert.StartsWith("[Error]", second);
        Assert.Contains("not pending", second, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Writes(fake, "bStepReq, val=true"));
    }

    [Fact]
    public void ConfirmProposal_AbortRaisedSynchronouslyDuringConfirm_DoesNotWriteCommandBit()
    {
        // E-20: simulates an operator hitting panic-abort at the exact moment
        // ConfirmProposal is processing — via the Audited event, which fires
        // synchronously on the confirming thread, exactly where a GUI handler
        // could reasonably call Abort(). The epoch re-check right before the
        // actual write must catch this and refuse to write the command bit.
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        coord.Audited += entry =>
        {
            if (entry.Kind == "confirm")
                coord.Abort();
        };

        var json = coord.ConfirmProposal(id);

        Assert.StartsWith("[Error]", json);
        Assert.Contains("state changed", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true")); // the abort race must prevent the write
        Assert.Contains("rejected", coord.GetProposalStatus(id), StringComparison.OrdinalIgnoreCase);
    }

    // ── reject ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RejectProposal_AlreadyExecuted_Refused()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));
        coord.ConfirmProposal(id);
        int writesBefore = Writes(fake, "bStepReq, val=true");

        var json = coord.RejectProposal(id, "too late");

        Assert.StartsWith("[Error]", json);
        Assert.Contains("not pending", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(writesBefore, Writes(fake, "bStepReq, val=true"));
        Assert.Contains("executed", coord.GetProposalStatus(id), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectProposal_MarksRejected_WritesNothing()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        var json = coord.RejectProposal(id, "operator cancelled");

        Assert.Equal("rejected", JsonDocument.Parse(json).RootElement.GetProperty("status").GetString());
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
        Assert.Contains("rejected", coord.GetProposalStatus(id), StringComparison.OrdinalIgnoreCase);
    }

    // ── get_proposal_status: read-only ──────────────────────────────────────────

    [Fact]
    public void GetProposalStatus_UnknownId_Error()
    {
        var (coord, _, _) = NewCoord();
        Assert.StartsWith("[Error]", coord.GetProposalStatus("nope"));
    }

    [Fact]
    public void GetProposalStatus_ExpiresStalePending_AsSideEffectOfReading()
    {
        var (coord, fake, clock) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        Assert.Contains("pending", coord.GetProposalStatus(id));
        clock.Now = T0.AddSeconds(61);
        Assert.Contains("expired", coord.GetProposalStatus(id));
        Assert.Equal(0, Writes(fake, "bStepReq, val=true")); // reading never writes
    }

    // ── heartbeat / watchdog ────────────────────────────────────────────────────

    [Fact]
    public void HeartbeatTick_WhileArmed_WritesEnable()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null); // 1 enable write
        coord.HeartbeatTick();                  // +1 enable write

        Assert.Equal(2, Writes(fake, "GVL_IBN.bEnable"));
        Assert.Equal(0, Writes(fake, "bAbort"));
    }

    [Fact]
    public void HeartbeatTick_AfterExpiry_AbortsAndHomes()
    {
        var (coord, fake, clock) = NewCoord();
        coord.Arm(60, null, null, null, null);

        clock.Now = T0.AddSeconds(61);
        coord.HeartbeatTick(); // watchdog fires

        Assert.False(coord.IsArmed);
        Assert.Equal(1, Writes(fake, "GVL_IBN.bAbort"));
    }

    [Fact]
    public void HeartbeatTick_WhenNotArmed_NoOp()
    {
        var (coord, fake, _) = NewCoord();
        coord.HeartbeatTick();
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void HeartbeatTick_WriteThrows_RecordedNotThrown()
    {
        // Arm first against a working fake (the initial enable write must succeed
        // for Arm to commit — see Arm_InitialEnableWriteFails_RefusesArm below);
        // only THEN does the session start throwing, simulating a mid-session
        // disconnect on a later heartbeat tick.
        var fake = new FakeTwinCatSession();
        var clock = new Clock(T0);
        var coord = new IbnLiveCoordinator(fake, clock.Read);
        coord.Arm(60, null, null, null, null);

        fake.WriteAdsVariableThrows = new InvalidOperationException("disconnected");
        var ex = Record.Exception(() => coord.HeartbeatTick());

        Assert.Null(ex); // never throws out of a timer thread
        Assert.Contains(coord.AuditSnapshot(), a => a.Kind == "heartbeat-loss");
    }

    [Fact]
    public void Arm_InitialEnableWriteFails_RefusesArm_NotArmed()
    {
        // issue #154: a real WriteAdsVariable failure is reported via
        // Success=false, not necessarily an exception — Arm must treat that as
        // honestly as a throw and refuse to report "armed".
        var fake = new FakeTwinCatSession
        {
            FakeWriteAdsVariableResult = new AdsVariableResult(false, "GVL_IBN.bEnable", "BOOL", null, "net", 851, "not reachable")
        };
        var coord = new IbnLiveCoordinator(fake, () => T0);

        var json = coord.Arm(60, null, null, null, null);

        Assert.StartsWith("[Error]", json);
        Assert.False(coord.IsArmed);
        Assert.Contains(coord.AuditSnapshot(), a => a.Kind == "arm-refused");
    }

    [Fact]
    public void HeartbeatTick_WriteReturnsSuccessFalse_RecordedAsHeartbeatLoss()
    {
        // The common real-world case (issue #154): WriteAdsVariable returns
        // Success=false WITHOUT throwing at all — the old exception-only
        // detection missed this entirely (silent heartbeat failure).
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);

        fake.FakeWriteAdsVariableResult = new AdsVariableResult(false, "GVL_IBN.bEnable", "BOOL", null, "net", 851, "no route");
        coord.HeartbeatTick();

        Assert.Contains(coord.AuditSnapshot(), a => a.Kind == "heartbeat-loss" && a.Detail.Contains("no route"));
        Assert.True(coord.IsArmed); // one miss alone must not yet abort
    }

    [Fact]
    public void HeartbeatTick_ConsecutiveMisses_AutoAbortsAtDefaultThreshold()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);
        fake.FakeWriteAdsVariableResult = new AdsVariableResult(false, "GVL_IBN.bEnable", "BOOL", null, "net", 851, "no route");

        coord.HeartbeatTick(); // miss 1
        Assert.True(coord.IsArmed);
        coord.HeartbeatTick(); // miss 2
        Assert.True(coord.IsArmed);
        coord.HeartbeatTick(); // miss 3 == default threshold -> auto-abort

        Assert.False(coord.IsArmed);
        Assert.Contains(coord.AuditSnapshot(), a => a.Kind.StartsWith("heartbeat-watchdog", StringComparison.Ordinal));
        // the abort itself writes bAbort:=true even though bEnable keeps failing.
        Assert.Equal(1, Writes(fake, "GVL_IBN.bAbort"));
    }

    [Fact]
    public void HeartbeatTick_SuccessAfterMisses_ResetsMissCounter()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);
        fake.FakeWriteAdsVariableResult = new AdsVariableResult(false, "GVL_IBN.bEnable", "BOOL", null, "net", 851, "no route");
        coord.HeartbeatTick(); // miss 1
        coord.HeartbeatTick(); // miss 2

        fake.FakeWriteAdsVariableResult = new AdsVariableResult(true, "GVL_IBN.bEnable", "BOOL", true, "net", 851, "OK");
        coord.HeartbeatTick(); // success resets the counter

        fake.FakeWriteAdsVariableResult = new AdsVariableResult(false, "GVL_IBN.bEnable", "BOOL", null, "net", 851, "no route");
        coord.HeartbeatTick(); // miss 1 again (not miss 3) — must not abort
        coord.HeartbeatTick(); // miss 2 again — must not abort

        Assert.True(coord.IsArmed);
    }

    [Fact]
    public void Disarm_WritesEnableFalse()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);

        coord.Disarm();

        Assert.Contains(fake.CallLog, c => c.Contains("WriteAdsVariable(sym=GVL_IBN.bEnable, val=false"));
    }

    [Fact]
    public void Abort_WritesEnableFalse()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);

        coord.Abort();

        Assert.Contains(fake.CallLog, c => c.Contains("WriteAdsVariable(sym=GVL_IBN.bEnable, val=false"));
    }

    // ── abort / release_all (not gated by arm) ──────────────────────────────────

    [Fact]
    public void Abort_WithoutArm_StillWritesAbortBit()
    {
        var (coord, fake, _) = NewCoord();

        var json = coord.Abort();

        Assert.DoesNotContain("[Error]", json);
        Assert.Equal(1, Writes(fake, "GVL_IBN.bAbort"));
    }

    [Fact]
    public void ReleaseAll_AbortsRejectsPendingAndClearsRegistry()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        coord.Propose("next_step", "chainA", "Cyl1");
        coord.Propose("hold", "chainA", null);

        var json = coord.ReleaseAll();

        Assert.DoesNotContain("[Error]", json);
        Assert.Equal(1, Writes(fake, "GVL_IBN.bAbort"));
        Assert.False(coord.IsArmed);
        Assert.Empty(coord.ProposalsSnapshot()); // registry cleared
    }

    // ── list_active_chains: read-only ───────────────────────────────────────────

    [Fact]
    public void ListActiveChains_ShapesStatusStruct_AndReadsNothingElse()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, "GVL_IBN", null, null);
        fake.FakeReadAdsVariablesBatchResult = new AdsBatchReadResult(
            true,
            new[]
            {
                new AdsBatchReadItem("GVL_IBN.bChainRunning", "BOOL", true, true, "OK"),
                new AdsBatchReadItem("GVL_IBN.nActiveChain", "INT", 2, true, "OK"),
                new AdsBatchReadItem("GVL_IBN.nCurrentStep", "INT", 5, true, "OK"),
                new AdsBatchReadItem("GVL_IBN.bFault", "BOOL", false, true, "OK"),
            },
            "127.0.0.1.1.1", 851, "OK");

        var json = coord.ListActiveChains();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("GVL_IBN", root.GetProperty("gvlPath").GetString());
        var chain = root.GetProperty("chain");
        Assert.True(chain.GetProperty("running").GetBoolean());
        Assert.Equal(2, chain.GetProperty("activeChain").GetInt32());
        Assert.Equal(5, chain.GetProperty("currentStep").GetInt32());
        Assert.False(chain.GetProperty("fault").GetBoolean());
        // it batch-reads exactly the 4 status symbols — and writes nothing
        Assert.Contains(fake.CallLog, c => c.StartsWith("ReadAdsVariablesBatch(n=4", StringComparison.Ordinal));
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
    }

    [Fact]
    public void ListActiveChains_ConnectionFailure_ReturnsErrorString()
    {
        var (coord, fake, _) = NewCoord();
        fake.FakeReadAdsVariablesBatchResult = new AdsBatchReadResult(
            false, Array.Empty<AdsBatchReadItem>(), "127.0.0.1.1.1", 851,
            "ReadAdsVariablesBatch failed: not reachable.");

        Assert.StartsWith("[Error]", coord.ListActiveChains());
    }

    // ── audit trail ─────────────────────────────────────────────────────────────

    [Fact]
    public void AuditTrail_RecordsArmProposeConfirmExecute()
    {
        var (coord, _, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));
        coord.ConfirmProposal(id);

        var kinds = coord.AuditSnapshot().Select(a => a.Kind).ToList();
        Assert.Contains("arm", kinds);
        Assert.Contains("propose", kinds);
        Assert.Contains("confirm", kinds);
        Assert.Contains("executed", kinds);
    }

    // ── issue #2: a proposal belongs to exactly one arm window ──────────────────

    [Fact]
    public void ConfirmProposal_AfterReArm_Refused_WritesNothing()
    {
        var (coord, fake, clock) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        clock.Now = T0.AddSeconds(30);
        coord.Arm(60, new[] { "Cyl1" }, null, null, null); // re-arm while armed
        var json = coord.ConfirmProposal(id);

        Assert.StartsWith("[Error]", json);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
        Assert.Contains("rejected", coord.GetProposalStatus(id), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(coord.AuditSnapshot(), a => a.Kind == "reject" && a.Detail.Contains("superseded by re-arm"));
    }

    [Fact]
    public void ReArm_LeavesProposalsOfTheNewWindowConfirmable()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        coord.Propose("next_step", "chainA", "Cyl1");
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var fresh = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        var json = coord.ConfirmProposal(fresh);

        Assert.DoesNotContain("[Error]", json);
        Assert.Equal(1, Writes(fake, "bStepReq, val=true"));
    }

    [Fact]
    public void ConfirmProposal_AtProposalExpiry_Refused_Expired()
    {
        var (coord, fake, clock) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));

        clock.Now = T0.AddSeconds(60); // exactly the proposal's ExpiresUtc
        var json = coord.ConfirmProposal(id);

        Assert.StartsWith("[Error]", json);
        Assert.Equal(0, Writes(fake, "bStepReq, val=true"));
        Assert.Contains("expired", coord.GetProposalStatus(id), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refusals_RaiseAuditedWithoutHoldingTheStateLock()
    {
        // A subscriber that reads coordinator state from ANOTHER thread while handling the
        // audit event would deadlock if the event were raised under the lock (issue #2).
        var (coord, _, _) = NewCoord();
        coord.Arm(60, null, null, null, null);
        bool readOk = true;
        coord.Audited += _ =>
            readOk &= Task.Run(() => coord.AuditSnapshot()).Wait(TimeSpan.FromSeconds(2));

        coord.ConfirmProposal("deadbeef");
        coord.RejectProposal("deadbeef");
        var id = ProposalId(coord.Propose("next_step", null, null));
        coord.RejectProposal(id);
        coord.RejectProposal(id); // not pending any more → reject-refused

        Assert.True(readOk);
    }

    // ── issue #1: stop paths vs. in-flight IBN writes ───────────────────────────

    private static readonly TimeSpan LatchTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Holds the first write matching (symbol suffix, value) in flight until released.</summary>
    private sealed class WriteLatch(string symbolSuffix, string value)
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        private int _hit;

        public void Hook(string symbol, string v)
        {
            if (symbol.EndsWith(symbolSuffix, StringComparison.Ordinal) && v == value
                && Interlocked.Exchange(ref _hit, 1) == 0)
            {
                Entered.Set();
                Release.Wait(LatchTimeout);
            }
        }
    }

    /// <summary>The writes in order, as "symbol, val=value".</summary>
    private static List<string> WriteSequence(FakeTwinCatSession f)
    {
        lock (f.CallLog)
            return f.CallLog.Where(c => c.StartsWith("WriteAdsVariable(sym=", StringComparison.Ordinal))
                .Select(c => c["WriteAdsVariable(sym=".Length..c.IndexOf(", net=", StringComparison.Ordinal)])
                .ToList();
    }

    private static string LastWriteOf(FakeTwinCatSession f, string symbol) =>
        WriteSequence(f).Last(s => s.StartsWith(symbol + ",", StringComparison.Ordinal));

    [Fact]
    public void Abort_DuringInFlightConfirmWrite_WaitsForIt_ThenClearsTheStepRequest()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, new[] { "Cyl1" }, null, null, null);
        var id = ProposalId(coord.Propose("next_step", "chainA", "Cyl1"));
        var latch = new WriteLatch(".bStepReq", "true");
        fake.OnWriteAdsVariable = latch.Hook;

        var confirm = Task.Run(() => coord.ConfirmProposal(id));
        Assert.True(latch.Entered.Wait(LatchTimeout));
        var abort = Task.Run(() => coord.Abort());

        // The abort waits for the ONE in-flight IBN write — and is not lost.
        Assert.False(abort.Wait(TimeSpan.FromMilliseconds(200)));
        latch.Release.Set();
        Assert.True(Task.WaitAll(new Task[] { confirm, abort }, LatchTimeout));

        var seq = WriteSequence(fake);
        int stepOn = seq.IndexOf("GVL_IBN.bStepReq, val=true");
        Assert.True(stepOn >= 0);
        Assert.True(seq.IndexOf("GVL_IBN.bAbort, val=true") > stepOn);
        Assert.Equal("GVL_IBN.bStepReq, val=false", LastWriteOf(fake, "GVL_IBN.bStepReq"));
        Assert.Equal("GVL_IBN.bEnable, val=false", LastWriteOf(fake, "GVL_IBN.bEnable"));
        Assert.False(coord.IsArmed);
    }

    [Fact]
    public void Abort_DuringInFlightArmWrite_IsNotUndoneByTheArmCommit()
    {
        var (coord, fake, _) = NewCoord();
        var latch = new WriteLatch(".bEnable", "true");
        fake.OnWriteAdsVariable = latch.Hook;

        var arm = Task.Run(() => coord.Arm(60, null, null, null, null));
        Assert.True(latch.Entered.Wait(LatchTimeout));
        var abort = Task.Run(() => coord.Abort());
        Assert.False(abort.Wait(TimeSpan.FromMilliseconds(200)));
        latch.Release.Set();
        Assert.True(Task.WaitAll(new Task[] { arm, abort }, LatchTimeout));

        // Arm committed first, then the abort disarmed it — never ARMED after an ABORT.
        Assert.False(coord.IsArmed);
        Assert.Equal("GVL_IBN.bEnable, val=false", LastWriteOf(fake, "GVL_IBN.bEnable"));
        Assert.StartsWith("[Error]", coord.Propose("next_step", null, null));
    }

    [Fact]
    public void Abort_DuringInFlightHeartbeat_EnableEndsFalse()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);
        var latch = new WriteLatch(".bEnable", "true");
        fake.OnWriteAdsVariable = latch.Hook;

        var tick = Task.Run(coord.HeartbeatTick);
        Assert.True(latch.Entered.Wait(LatchTimeout));
        var abort = Task.Run(() => coord.Abort());
        Assert.False(abort.Wait(TimeSpan.FromMilliseconds(200)));
        latch.Release.Set();
        Assert.True(Task.WaitAll(new Task[] { tick, abort }, LatchTimeout));

        Assert.Equal("GVL_IBN.bEnable, val=false", LastWriteOf(fake, "GVL_IBN.bEnable"));
        Assert.False(coord.IsArmed);
    }

    [Fact]
    public void Abort_WritesAbort_ThenStepRequestFalse_ThenEnableFalse()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);
        fake.CallLog.Clear();

        coord.Abort();

        Assert.Equal(new[] { "GVL_IBN.bAbort, val=true", "GVL_IBN.bStepReq, val=false", "GVL_IBN.bEnable, val=false" },
            WriteSequence(fake));
    }

    [Fact]
    public void Abort_WhenWritesThrow_StillAttemptsEveryStopWrite_AndReportsError()
    {
        var (coord, fake, _) = NewCoord();
        coord.Arm(60, null, null, null, null);
        fake.CallLog.Clear();
        fake.WriteAdsVariableThrows = new InvalidOperationException("disconnected");

        var json = coord.Abort();

        Assert.StartsWith("[Error]", json);
        Assert.Equal(3, WriteSequence(fake).Count);
        Assert.False(coord.IsArmed);
    }

    [Fact]
    public void Arm_CachesTheResolvedNetId_HeartbeatAndStopReuseIt()
    {
        // amsNetId "project" is resolved through the open solution (COM) — the heartbeat and
        // the stop paths must use the NetId the arm write actually resolved (issue #1, race 3).
        var (coord, fake, _) = NewCoord();
        fake.FakeWriteAdsVariableResult = new AdsVariableResult(true, "GVL_IBN.bEnable", "BOOL", true, "10.20.30.40.1.1", 851, "OK");
        coord.Arm(60, null, null, "project", null);
        fake.CallLog.Clear();

        coord.HeartbeatTick();
        coord.Abort();

        Assert.Equal(4, fake.CallLog.Count);
        Assert.All(fake.CallLog, c => Assert.Contains("net=10.20.30.40.1.1", c));
    }
}
