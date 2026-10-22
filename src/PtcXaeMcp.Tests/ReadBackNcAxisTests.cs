using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #152 D-9: configure_axis re-reads the axis (ProduceXml) and compares every field.</summary>
public class ReadBackNcAxisTests
{
    private const string Axis = "TINC^NC-Task 1 SAF^Axes^Axis 1";

    private static FakeTwinCatSession OpenFake() => new() { IsConnected = true, SolutionOpen = true };

    [Fact]
    public void Configure_Landed_ReportsConfirmedFields()
    {
        var fake = OpenFake();
        var fields = new Dictionary<string, string> { ["velocityMax"] = "1000", ["axisType"] = "coe" };

        string summary = new NcAxesCoordinator(fake).ConfigureAxis(Axis, fields);

        summary.Should().StartWith("Configured axis").And.Contain("all 2 field(s) confirmed");
        fake.CallLog.Should().ContainInOrder($"ConfigureAxis({Axis},2)", $"ReadAxisParams({Axis})");
    }

    [Fact]
    public void Configure_NoOp_IsErrorNamingTheField()
    {
        var fake = OpenFake();
        fake.NoOpMutations.Add(nameof(FakeTwinCatSession.ConfigureAxis));

        string summary = new NcAxesCoordinator(fake).ConfigureAxis(Axis,
            new Dictionary<string, string> { ["unit"] = "°" });

        summary.Should().StartWith("[Error] configure_axis").And.Contain("unit: wanted '°', read back 'mm'");
    }

    [Fact]
    public void Configure_ReadBackFails_IsNotSuccess()
    {
        var fake = OpenFake();
        fake.ReadAxisParamsThrows = new InvalidOperationException("axis gone");

        string summary = new NcAxesCoordinator(fake).ConfigureAxis(Axis,
            new Dictionary<string, string> { ["unit"] = "mm" });

        summary.Should().StartWith("[Error]").And.Contain("not confirmed").And.Contain("axis gone");
    }

    [Theory]
    [InlineData("velocityMax", "1000", "1000.0", true)]
    [InlineData("velocityMax", "0.0002", "2E-4", true)]
    [InlineData("velocityMax", "1000", "999", false)]
    [InlineData("encoderInverse", "true", "1", true)]
    [InlineData("encoderInverse", "false", "true", false)]
    [InlineData("unit", "mm", "MM", true)]
    [InlineData("axisType", "coe", "CANopen-DS402", true)]
    [InlineData("axisType", "soe", "CANopen-DS402", false)]
    public void VerifyApplied_ComparesByMeaning(string key, string wanted, string readBack, bool same)
    {
        var mismatches = NcAxisXml.VerifyApplied(
            new Dictionary<string, string> { [key] = wanted },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [key] = readBack });
        mismatches.Should().HaveCount(same ? 0 : 1);
    }

    [Fact]
    public void VerifyApplied_AbsentField_IsMismatch()
    {
        NcAxisXml.VerifyApplied(new Dictionary<string, string> { ["jerk"] = "5" },
                new Dictionary<string, string>())
            .Should().ContainSingle().Which.Should().Contain("<absent>");
    }
}
