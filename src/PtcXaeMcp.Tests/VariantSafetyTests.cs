using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #148 (LIVE-10 / B-11) — variant validated first, read back, restored on failure.</summary>
public class VariantSafetyTests
{
    private const string PouPath = "TIPC^Plc^Plc Project^POUs^FB_A";

    private static FakeTwinCatSession OpenFake() => new() { IsConnected = true, SolutionOpen = true };

    // =====================================================================
    // Variant (LIVE-10 / B-11)
    // =====================================================================

    private static FakeTwinCatSession VariantFake()
    {
        var f = OpenFake();
        f.FakeVariantStatus = new VariantStatusResult("SIMULATION", ["SIMULATION", "PRODUCTION"],
            [new VariantGroupEntry("AllSim", ["SIMULATION"])]);
        return f;
    }

    [Fact]
    public void Variant_Unknown_SinglePrefixError_NoSet_StateUnchanged()
    {
        var fake = VariantFake();
        string r = new VariantManagementCoordinator(fake).SetActiveVariant("DOES_NOT_EXIST", rebuildAfter: false);

        Assert.StartsWith("[Error] Unknown variant 'DOES_NOT_EXIST'. Expected one of: SIMULATION, PRODUCTION, [AllSim].", r);
        Assert.DoesNotContain("[Error] [Error]", r);
        Assert.DoesNotContain("XAE >=", r);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("SetActiveVariant", StringComparison.Ordinal));
        Assert.Equal("SIMULATION", fake.FakeVariantStatus.ActiveVariant);
    }

    [Fact]
    public void Variant_CaseInsensitive_PassesProjectSpelling_AndReadsBack()
    {
        var fake = VariantFake();
        string r = new VariantManagementCoordinator(fake).SetActiveVariant("production", rebuildAfter: false);

        Assert.DoesNotContain("[Error]", r);
        Assert.Contains("SetActiveVariant(PRODUCTION)", fake.CallLog);
        Assert.Equal("PRODUCTION", fake.FakeVariantStatus.ActiveVariant);
        Assert.True(fake.CallLog.Count(c => c == "GetVariantStatus()") >= 2, "no read-back");
    }

    [Fact]
    public void Variant_Group_IsAccepted()
    {
        var fake = VariantFake();
        string r = new VariantManagementCoordinator(fake).SetActiveVariant("[allsim]", rebuildAfter: false);
        Assert.DoesNotContain("[Error]", r);
        Assert.Contains("SetActiveVariant([AllSim])", fake.CallLog);
    }

    [Fact]
    public void Variant_SetThrowsAndClears_RestoresPrevious_SinglePrefix()
    {
        var fake = VariantFake();
        fake.SetActiveVariantThrows = new InvalidOperationException("Setting CurrentProjectVariant to 'PRODUCTION' failed: E_FAIL");
        fake.VariantAfterFailedSet = string.Empty; // LIVE-10: the failed set left the variant empty

        string r = new VariantManagementCoordinator(fake).SetActiveVariant("PRODUCTION", rebuildAfter: false);

        Assert.StartsWith("[Error] Could not activate variant 'PRODUCTION'", r);
        Assert.DoesNotContain("[Error] [Error]", r);
        Assert.Contains("Restored the previous variant 'SIMULATION'", r);
        Assert.Equal("SIMULATION", fake.FakeVariantStatus.ActiveVariant);
    }

    [Fact]
    public void Variant_SetDoesNotTakeEffect_IsError_AndRestores()
    {
        var fake = VariantFake();
        fake.VariantAfterSet = string.Empty;

        string r = new VariantManagementCoordinator(fake).SetActiveVariant("PRODUCTION", rebuildAfter: false);

        Assert.StartsWith("[Error] Variant switch to 'PRODUCTION' did not take effect", r);
        Assert.Equal("SIMULATION", fake.FakeVariantStatus.ActiveVariant);
    }

    [Fact]
    public void Variant_NoVariantsDefined_IsError()
    {
        var fake = OpenFake();
        fake.FakeVariantStatus = new VariantStatusResult(string.Empty, [], []);
        string r = new VariantManagementCoordinator(fake).SetActiveVariant("SIMULATION", rebuildAfter: false);
        Assert.StartsWith("[Error] Unknown variant 'SIMULATION'. Expected one of: none", r);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("SetActiveVariant", StringComparison.Ordinal));
    }
}
