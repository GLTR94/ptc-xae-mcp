using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #148 — unknown implLanguage / axisType / scope edge+compare / ADS BOOL values are errors, never silent defaults.</summary>
public class StrictEnumValueTests
{
    private const string PouPath = "TIPC^Plc^Plc Project^POUs^FB_A";

    private static FakeTwinCatSession OpenFake() => new() { IsConnected = true, SolutionOpen = true };

    // =====================================================================
    // Unknown enum values → [Error] (LIVE-9 / B-8, axisType, scope, ADS BOOL)
    // =====================================================================

    [Fact]
    public void CreatePou_UnknownImplLanguage_ErrorsBeforeSession()
    {
        var fake = OpenFake();
        string r = new PlcCodeCoordinator(fake).CreatePou("TIPC^Plc^Plc Project^POUs", "FB_X", "FUNCTION_BLOCK", null, "FDB");
        Assert.Equal("[Error] Unknown implLanguage 'FDB'. Expected ST, IL, SFC, FBD, CFC or LD.", r);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void CreatePou_KnownLanguage_PassesCanonical_AndTrimmedName()
    {
        var fake = OpenFake();
        string r = new PlcCodeCoordinator(fake).CreatePou("TIPC^Plc^Plc Project^POUs", "  FB_X ", "FUNCTION_BLOCK", null, " fbd ");
        Assert.DoesNotContain("[Error]", r);
        Assert.Contains(fake.CallLog, c => c.StartsWith("CreatePou(", StringComparison.Ordinal) && c.Contains("name=FB_X,") && c.Contains("lang=FBD"));
        Assert.Contains("'FB_X'", r);
    }

    [Fact]
    public void AddPouMember_UnknownImplLanguage_ErrorsBeforeSession()
    {
        var fake = OpenFake();
        string r = new PouMembersCoordinator(fake).AddPouMember(PouPath, "ACTION", "A_Run", null, "FDB");
        Assert.StartsWith("[Error] Unknown implLanguage 'FDB'", r);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddPouMember_Get_ReportsRealNodeName()
    {
        var fake = OpenFake();
        fake.MembersByPou[PouPath] = new();
        string r = new PouMembersCoordinator(fake).AddPouMember(PouPath, "GET", "whatever", null, null);
        Assert.StartsWith("Added GET 'Get'", r);
        Assert.Contains(fake.CallLog, c => c.StartsWith("AddPouMember(", StringComparison.Ordinal) && c.Contains("name=Get,"));
    }

    [Fact]
    public void ConfigureAxis_UnknownAxisType_ErrorsBeforeSession()
    {
        var fake = OpenFake();
        string r = new NcAxesCoordinator(fake).ConfigureAxis("TINC^NC^Axes^Axis 1",
            new Dictionary<string, string> { ["axisType"] = "stepper" });
        Assert.StartsWith("[Error] Unknown axisType 'stepper'. Expected ", r);
        Assert.Empty(fake.CallLog);
    }

    [Theory]
    [InlineData("Up", null, "[Error] Unknown edge 'Up'. Expected Rising, Falling, Both.")]
    [InlineData(null, "GreaterEqual", "[Error] Unknown compare 'GreaterEqual'. Expected Greater, Less, Equal.")]
    public void ScopeTrigger_UnknownEdgeOrCompare_ErrorsBeforeSession(string? edge, string? compare, string expected)
    {
        var fake = OpenFake();
        string r = new ScopeCoordinator(fake).AddScopeTriggerSet("G1", "ChannelSet", "MAIN.x", 1.0, edge, compare);
        Assert.Equal(expected, r);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void ScopeTrigger_Normalizes_Casing()
    {
        Assert.Equal("Rising", ScopeTriggerCondition.NormalizeEdge(" rising ").Value);
        Assert.Equal("Less", ScopeTriggerCondition.NormalizeCompare("LESS").Value);
        Assert.Equal((null, null), ScopeTriggerCondition.NormalizeEdge(null));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData(" false ", false)]
    [InlineData("0", false)]
    [InlineData("\"true\"", true)]
    public void AdsBool_KnownValues(string raw, bool expected)
    {
        Assert.True(AdsBoolValue.TryParse(raw, out bool v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("ture")]
    [InlineData("2")]
    [InlineData("")]
    public void AdsBool_UnknownValues_AreErrors_NotFalse(string raw)
    {
        Assert.False(AdsBoolValue.TryParse(raw, out _));
        Assert.Equal($"[Error] Unknown BOOL value '{raw}'. Expected true, false, 1 or 0.", AdsBoolValue.UnknownMessage(raw));
    }
}
