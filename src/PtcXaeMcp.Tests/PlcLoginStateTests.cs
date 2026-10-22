using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #148 (LIVE-4) — the online state is never inferred from a disabled Login command.</summary>
public class PlcLoginStateTests
{
    private const string PouPath = "TIPC^Plc^Plc Project^POUs^FB_A";

    private static FakeTwinCatSession OpenFake() => new() { IsConnected = true, SolutionOpen = true };

    // =====================================================================
    // Honest login state (LIVE-4)
    // =====================================================================

    [Theory]
    [InlineData(true,  false, "Proceed")]
    [InlineData(false, true,  "AlreadyOnline")]
    [InlineData(null,  true,  "AlreadyOnline")]
    [InlineData(false, false, "NoTarget")]   // LIVE-4: was reported as "already online"
    [InlineData(null,  false, "Unknown")]
    [InlineData(true,  null,  "Unknown")]
    [InlineData(false, null,  "Unknown")]
    public void Login_Precheck_NeverInfersOnlineFromDisabledLogin(bool? login, bool? logout, string expected) =>
        Assert.Equal(expected, PlcOnlineInference.BeforeLogin(login, logout).ToString());

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, null)]
    public void OnlineState_ComesFromLogoutOnly(bool? logoutAvailable, bool? expectedOnline) =>
        Assert.Equal(expectedOnline, PlcOnlineInference.IsOnline(logoutAvailable));
}
