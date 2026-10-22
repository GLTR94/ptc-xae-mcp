using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #149: reachability texts, launch refusal on unknown process count, multi-PLC fallback.</summary>
public class XaeReachabilityTests
{
    // ── reachability texts (RequireOpenSolution) ────────────────────────────

    [Fact]
    public void Reachability_NoProcess_KeepsTheLegacyText() =>
        Assert.Equal("No TwinCAT XAE instance is running.", XaeReachability.Describe(0, null));

    [Fact]
    public void Reachability_ProcessButNoDte_SaysUnreachable()
    {
        string text = XaeReachability.Describe(1, null);
        Assert.StartsWith("TcXaeShell is running (1 process(es)) but its DTE is not reachable", text);
        Assert.DoesNotContain("No TwinCAT XAE instance is running", text);
    }

    [Fact]
    public void Reachability_UnknownProcessCount_SaysUnknown() =>
        Assert.Contains("could not be determined", XaeReachability.Describe(null, null));

    [Fact]
    public void Reachability_Dialog_WinsAndIsNamed() =>
        Assert.StartsWith("XAE is blocked by dialog 'Gehe zu Zeile'", XaeReachability.Describe(1, "Gehe zu Zeile"));

    // ── process probe: unknown never means "none" ───────────────────────────

    [Fact]
    public void LaunchBlockReason_UnknownCount_RefusesLaunch() =>
        Assert.Contains("refusing to launch a second XAE", XaeProcessProbe.LaunchBlockReason(null));

    [Fact]
    public void LaunchBlockReason_ZeroRunning_AllowsLaunch() =>
        Assert.Null(XaeProcessProbe.LaunchBlockReason(0));

    [Fact]
    public void LaunchBlockReason_OneRunning_Refuses() =>
        Assert.NotNull(XaeProcessProbe.LaunchBlockReason(1));

    // ── multi-PLC fallback (B-5) ────────────────────────────────────────────

    [Fact]
    public void Fallback_SinglePlc_IgnoresTheSegment() =>
        Assert.Equal([0], TwinCatComSession.FallbackCandidates(["FixturePlc"], "OtherName"));

    [Fact]
    public void Fallback_MultiPlc_RequiresANameMatch() =>
        Assert.Equal([1], TwinCatComSession.FallbackCandidates(["PlcA", "PlcB"], "plcb"));

    [Fact]
    public void Fallback_MultiPlc_NoMatch_ResolvesNothing() =>
        // Never guess between PLCs: a read or write must not land on another PLC.
        Assert.Empty(TwinCatComSession.FallbackCandidates(["PlcA", "PlcB"], "PlcC"));

    [Fact]
    public void Fallback_NoCandidates_ResolvesNothing() =>
        Assert.Empty(TwinCatComSession.FallbackCandidates([], "PLC"));
}
