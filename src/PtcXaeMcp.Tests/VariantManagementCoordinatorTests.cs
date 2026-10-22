using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class VariantManagementCoordinatorTests
{
    private static FakeTwinCatSession ConnectedSession(bool withVariants = true)
    {
        var session = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        if (withVariants)
        {
            session.FakeVariantStatus = new VariantStatusResult(
                "SIMULATION",
                ["SIMULATION", "PRODUCTION"],
                [new VariantGroupEntry("AllSim", ["SIMULATION"])]);
        }
        else
        {
            session.FakeVariantStatus = new VariantStatusResult(string.Empty, [], []);
        }
        return session;
    }

    // ── GetVariantStatus ─────────────────────────────────────────────────────

    [Fact]
    public void GetVariantStatus_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        string result = new VariantManagementCoordinator(fake).GetVariantStatus();
        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("GetVariantStatus", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void GetVariantStatus_WithVariants_ReturnsValidJson()
    {
        var fake = ConnectedSession(withVariants: true);
        string json = new VariantManagementCoordinator(fake).GetVariantStatus();

        Assert.DoesNotContain("[Error]", json);
        var doc = JsonDocument.Parse(json);
        Assert.Equal("SIMULATION", doc.RootElement.GetProperty("activeVariant").GetString());

        var variants = doc.RootElement.GetProperty("availableVariants");
        Assert.Equal(2, variants.GetArrayLength());
    }

    [Fact]
    public void GetVariantStatus_NoVariantsDefined_ReturnsEmptyLists()
    {
        var fake = ConnectedSession(withVariants: false);
        string json = new VariantManagementCoordinator(fake).GetVariantStatus();

        Assert.DoesNotContain("[Error]", json);
        var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("availableVariants").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("groups").GetArrayLength());
    }

    // ── SetActiveVariant ─────────────────────────────────────────────────────

    [Fact]
    public void SetActiveVariant_RebuildFalse_DoesNotCallBuild()
    {
        var fake = ConnectedSession();
        fake.FakeSetActiveVariantResult = "Variant set to 'PRODUCTION'.";

        string result = new VariantManagementCoordinator(fake).SetActiveVariant("PRODUCTION", rebuildAfter: false);

        Assert.Contains("SetActiveVariant(PRODUCTION)", fake.CallLog);
        Assert.DoesNotContain("BuildSolution()", string.Join(",", fake.CallLog));
        Assert.Contains("Rebuild required", result);
    }

    [Fact]
    public void SetActiveVariant_RebuildTrue_CallsBuildSolution()
    {
        var fake = ConnectedSession();
        fake.FakeSetActiveVariantResult = "Variant set to 'PRODUCTION'.";
        // BuildCoordinator calls session.BuildSolution() — FakeBuildSolutionResult defaults to success

        string result = new VariantManagementCoordinator(fake).SetActiveVariant("PRODUCTION", rebuildAfter: true);

        Assert.Contains("SetActiveVariant(PRODUCTION)", fake.CallLog);
        Assert.Contains("BuildSolution()", string.Join(",", fake.CallLog));
    }
}
