using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #153 (D-8): the safety write goes through a validating coordinator.</summary>
public class SafetyCoordinatorTests
{
    private static FakeTwinCatSession Open() => new() { IsConnected = true, SolutionOpen = true };

    [Theory]
    [InlineData(@"..\..\bSI_Evil", 2)]
    [InlineData("xNoPrefix", 2)]
    [InlineData("bSI_Ok", 3)]
    [InlineData("", 2)]
    public void AddAliasDevice_Invalid_ReturnsError_WithoutTouchingTheSession(string name, int subType)
    {
        var fake = Open();
        string result = new SafetyCoordinator(fake).AddAliasDevice(name, subType, null);
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddAliasDevice_Valid_DelegatesWithGroup()
    {
        var fake = Open();
        string result = new SafetyCoordinator(fake).AddAliasDevice("bSQ_Request", 1, " Group2 ");
        Assert.Contains("SafetyAddAliasDevice(signal=bSQ_Request,subType=1,group=Group2)", fake.CallLog);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void AddAliasDevice_SessionThrows_IsError()
    {
        var fake = Open();
        fake.SafetyAddAliasDeviceThrows = new InvalidOperationException("Rolled back — the .sds was removed");
        string result = new SafetyCoordinator(fake).AddAliasDevice("bSI_Ok", 2, null);
        Assert.Equal("[Error] Rolled back — the .sds was removed", result);
    }

    [Fact]
    public void AddAliasDevice_NoSafetyProject_IsError()
    {
        var fake = Open();
        fake.SafetyAddAliasDeviceResult = "No TwinCAT Safety project (.splcproj) found in the open solution.";
        Assert.StartsWith("[Error]", new SafetyCoordinator(fake).AddAliasDevice("bSI_Ok", 2, null));
    }

    [Fact]
    public void AddAliasDevice_NoSolution_IsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        Assert.StartsWith("[Error]", new SafetyCoordinator(fake).AddAliasDevice("bSI_Ok", 2, null));
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void ReadIoMap_SessionThrows_IsError()
    {
        var fake = Open();
        fake.SafetyReadIoMapThrows = new IOException("sharing violation");
        Assert.Equal("[Error] sharing violation", new SafetyCoordinator(fake).ReadIoMap());
    }
}
