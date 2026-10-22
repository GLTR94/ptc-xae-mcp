using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class AdsVariableTests
{
    // ---- ReadAdsVariable (session plumbing) ----------------------------------

    [Fact]
    public void ReadAdsVariable_HappyPath_RecordsCallAndReturnsResult()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadAdsVariableResult = new AdsVariableResult(
                true, "GVL_Safety.bSI_DriveReleased", "BOOL", false,
                "192.168.1.1.1.1", 851, "OK"),
        };

        var result = fake.ReadAdsVariable("GVL_Safety.bSI_DriveReleased", null, 851);

        Assert.Contains("ReadAdsVariable(sym=GVL_Safety.bSI_DriveReleased", fake.CallLog[0]);
        Assert.True(result.Success);
        Assert.Equal("BOOL", result.PlcType);
        Assert.Equal(false, result.Value);
    }

    [Fact]
    public void ReadAdsVariable_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            ReadAdsVariableThrows = new InvalidOperationException("ADS server unreachable"),
        };

        Assert.Throws<InvalidOperationException>(() =>
            fake.ReadAdsVariable("MAIN.rSpeed", null, 851));
        Assert.Contains("ReadAdsVariable", fake.CallLog[0]);
    }

    // ---- WriteAdsVariable (session plumbing) --------------------------------

    [Fact]
    public void WriteAdsVariable_HappyPath_RecordsCallAndReturnsResult()
    {
        var fake = new FakeTwinCatSession
        {
            FakeWriteAdsVariableResult = new AdsVariableResult(
                true, "PRG_IntegrationScratch.bTest", "BOOL", true,
                "127.0.0.1.1.1", 851, "OK"),
        };

        var result = fake.WriteAdsVariable("PRG_IntegrationScratch.bTest", "true", null, 851);

        Assert.Contains("WriteAdsVariable(sym=PRG_IntegrationScratch.bTest, val=true", fake.CallLog[0]);
        Assert.True(result.Success);
        Assert.Equal(true, result.Value);
    }

    [Fact]
    public void WriteAdsVariable_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            WriteAdsVariableThrows = new InvalidOperationException("write error"),
        };

        Assert.Throws<InvalidOperationException>(() =>
            fake.WriteAdsVariable("MAIN.bFlag", "true", null, 851));
    }

    // ---- AdsVariableCoordinator (JSON shaping + validation) -----------------

    [Fact]
    public void Coordinator_Read_SerializesResultAsJson()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadAdsVariableResult = new AdsVariableResult(
                true, "GVL_Safety.bSI_DriveReleased", "BOOL", false,
                "127.0.0.1.1.1", 851, "OK"),
        };

        var json = new AdsVariableCoordinator(fake).ReadAdsVariable(
            "GVL_Safety.bSI_DriveReleased", null, null);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("GVL_Safety.bSI_DriveReleased", root.GetProperty("symbol").GetString());
        Assert.Equal("BOOL", root.GetProperty("plcType").GetString());
        Assert.False(root.GetProperty("value").GetBoolean());
        Assert.Equal(851, root.GetProperty("adsPort").GetInt32());
    }

    [Fact]
    public void Coordinator_Read_EmptySymbolPath_ReturnsError()
    {
        var fake = new FakeTwinCatSession();

        var text = new AdsVariableCoordinator(fake).ReadAdsVariable("", null, null);

        Assert.StartsWith("[Error]", text);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("ReadAdsVariable", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_Read_FailureReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadAdsVariableResult = new AdsVariableResult(
                false, "MAIN.rSpeed", null, null, "127.0.0.1.1.1", 851,
                "ReadAdsVariable failed: ADS server not reachable."),
        };

        var text = new AdsVariableCoordinator(fake).ReadAdsVariable("MAIN.rSpeed", null, null);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("not reachable", text);
        Assert.EndsWith("(target 127.0.0.1.1.1:851)", text);
    }

    // Issue #202: a failed ADS call names the NetId it actually went to, so a wrong default NetId is visible.
    [Theory]
    [InlineData("SymbolLoader failed: TargetPortNotFound", "199.4.42.250.1.1", 851,
        "[Error] SymbolLoader failed: TargetPortNotFound (target 199.4.42.250.1.1:851)")]
    [InlineData("[Error] ADS 199.4.42.250.1.1:851 timed out", "199.4.42.250.1.1", 851,
        "[Error] ADS 199.4.42.250.1.1:851 timed out")]
    [InlineData("no NetId known", "", 851, "[Error] no NetId known")]
    [InlineData("no NetId known", null, 852, "[Error] no NetId known")]
    public void Coordinator_FailedAt_AppendsTheTargetOnce(string message, string? netId, int port, string expected)
    {
        Assert.Equal(expected, AdsVariableCoordinator.FailedAt(message, netId, port));
    }

    [Fact]
    public void Coordinator_Write_FailureNamesTheTarget()
    {
        var fake = new FakeTwinCatSession
        {
            FakeWriteAdsVariableResult = new AdsVariableResult(
                false, "MAIN.bEnable", null, null, "199.4.42.250.1.1", 852, "WriteAdsVariable failed: timeout"),
        };

        var text = new AdsVariableCoordinator(fake).WriteAdsVariable("MAIN.bEnable", "TRUE", null, 852);

        Assert.Equal("[Error] WriteAdsVariable failed: timeout (target 199.4.42.250.1.1:852)", text);
    }

    [Fact]
    public void Coordinator_Write_SerializesResultAsJson()
    {
        var fake = new FakeTwinCatSession
        {
            FakeWriteAdsVariableResult = new AdsVariableResult(
                true, "MAIN.bEnable", "BOOL", true, "127.0.0.1.1.1", 851, "OK"),
        };

        var json = new AdsVariableCoordinator(fake).WriteAdsVariable(
            "MAIN.bEnable", "true", null, null);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("MAIN.bEnable", root.GetProperty("symbol").GetString());
        Assert.True(root.GetProperty("writtenValue").GetBoolean());
    }

    [Fact]
    public void Coordinator_Write_EmptySymbolPath_ReturnsError()
    {
        var fake = new FakeTwinCatSession();

        var text = new AdsVariableCoordinator(fake).WriteAdsVariable("  ", "true", null, null);

        Assert.StartsWith("[Error]", text);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WriteAdsVariable", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_Write_InputMappedRefused_ReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            FakeWriteAdsVariableResult = new AdsVariableResult(
                false, "GVL_Safety.bSI_DriveReleased", "BOOL", null,
                "127.0.0.1.1.1", 851,
                "[Error] Symbol 'GVL_Safety.bSI_DriveReleased' is input-mapped (AT %I*) — write refused."),
        };

        var text = new AdsVariableCoordinator(fake).WriteAdsVariable(
            "GVL_Safety.bSI_DriveReleased", "true", null, null);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("input-mapped", text);
    }

    [Fact]
    public void Coordinator_Write_UsesDefaultPort851WhenNotSpecified()
    {
        var fake = new FakeTwinCatSession();

        _ = new AdsVariableCoordinator(fake).WriteAdsVariable("MAIN.bFlag", "true", null, null);

        Assert.Contains("port=851", fake.CallLog[0]);
    }

    // ---- InspectFbInstance (session plumbing) --------------------------------

    [Fact]
    public void InspectFbInstance_HappyPath_RecordsCallAndReturnsResult()
    {
        var fake = new FakeTwinCatSession
        {
            FakeInspectFbInstanceResult = new InspectFbInstanceResult(
                true, "MAIN.fbMachine", "FB_Machine",
                new[]
                {
                    new FbMemberEntry("Step", "MAIN.fbMachine.Step", false, "INT", 2, 0),
                    new FbMemberEntry("State", "MAIN.fbMachine.State", true, "E_State", 2, null),
                },
                new[] { new FbRpcMethodEntry("get_State", false, 0) },
                "127.0.0.1.1.1", 851, "OK"),
        };

        var result = fake.InspectFbInstance("MAIN.fbMachine", null, 851);

        Assert.Contains("InspectFbInstance(path=MAIN.fbMachine", fake.CallLog[0]);
        Assert.True(result.Success);
        Assert.Equal(2, result.Members.Count);
        Assert.True(result.Members[1].IsProperty);
        Assert.Single(result.RpcMethods);
        Assert.Equal("get_State", result.RpcMethods[0].Name);
    }

    [Fact]
    public void InspectFbInstance_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            InspectFbInstanceThrows = new InvalidOperationException("ADS server unreachable"),
        };

        Assert.Throws<InvalidOperationException>(() =>
            fake.InspectFbInstance("MAIN.fbMachine", null, 851));
        Assert.Contains("InspectFbInstance", fake.CallLog[0]);
    }

    // ---- AdsVariableCoordinator.InspectFbInstance (JSON shaping + validation) --

    [Fact]
    public void Coordinator_Inspect_SerializesResultAsJson()
    {
        var fake = new FakeTwinCatSession
        {
            FakeInspectFbInstanceResult = new InspectFbInstanceResult(
                true, "MAIN.fbMachine", "FB_Machine",
                new[]
                {
                    new FbMemberEntry("Step", "MAIN.fbMachine.Step", false, "INT", 2, 0),
                    new FbMemberEntry("State", "MAIN.fbMachine.State", true, "E_State", 2, null),
                },
                new[] { new FbRpcMethodEntry("get_State", false, 0) },
                "127.0.0.1.1.1", 851, "OK"),
        };

        var json = new AdsVariableCoordinator(fake).InspectFbInstance("MAIN.fbMachine", null, null);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("FB_Machine", root.GetProperty("typeName").GetString());
        var members = root.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(2, members.Count);
        Assert.True(members[1].GetProperty("isProperty").GetBoolean());
        Assert.Equal(JsonValueKind.Null, members[1].GetProperty("offset").ValueKind);
        var rpcMethods = root.GetProperty("rpcMethods").EnumerateArray().ToList();
        Assert.Single(rpcMethods);
        Assert.Equal("get_State", rpcMethods[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Coordinator_Inspect_EmptyInstancePath_ReturnsError()
    {
        var fake = new FakeTwinCatSession();

        var text = new AdsVariableCoordinator(fake).InspectFbInstance("", null, null);

        Assert.StartsWith("[Error]", text);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("InspectFbInstance", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_Inspect_FailureReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            FakeInspectFbInstanceResult = new InspectFbInstanceResult(
                false, "MAIN.fbMissing", null, Array.Empty<FbMemberEntry>(), Array.Empty<FbRpcMethodEntry>(),
                "127.0.0.1.1.1", 851,
                "[Error] Symbol 'MAIN.fbMissing' not found in the PLC symbol table."),
        };

        var text = new AdsVariableCoordinator(fake).InspectFbInstance("MAIN.fbMissing", null, null);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("not found", text);
    }

    [Fact]
    public void Coordinator_Inspect_UsesDefaultPort851WhenNotSpecified()
    {
        var fake = new FakeTwinCatSession();

        _ = new AdsVariableCoordinator(fake).InspectFbInstance("MAIN.fbMachine", null, null);

        Assert.Contains("port=851", fake.CallLog[0]);
    }
}
