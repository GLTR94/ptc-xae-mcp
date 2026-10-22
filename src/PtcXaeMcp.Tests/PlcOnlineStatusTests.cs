using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Unit tests for the plc-online-status feature (fake session).</summary>
public class PlcOnlineStatusTests
{
    // ---- Session plumbing ---------------------------------------------------

    [Fact]
    public void GetPlcOnlineState_Offline_RecordsCallAndReturnsOffline()
    {
        var fake = new FakeTwinCatSession
        {
            FakePlcOnlineStateResult = new PlcOnlineStateResult(false, "XAE is Offline (fake)."),
        };

        var result = fake.GetPlcOnlineState();

        Assert.Contains("GetPlcOnlineState()", fake.CallLog);
        Assert.False(result.IsOnline);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void GetPlcOnlineState_Online_ReturnsOnline()
    {
        var fake = new FakeTwinCatSession
        {
            FakePlcOnlineStateResult = new PlcOnlineStateResult(true, "XAE is Online (fake)."),
        };

        var result = fake.GetPlcOnlineState();

        Assert.True(result.IsOnline);
        Assert.Contains("Online", result.Message);
    }

    [Fact]
    public void GetPlcOnlineState_Unknown_ReturnsNullIsOnline()
    {
        var fake = new FakeTwinCatSession
        {
            FakePlcOnlineStateResult = new PlcOnlineStateResult(null, "State indeterminate."),
        };

        var result = fake.GetPlcOnlineState();

        Assert.Null(result.IsOnline);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void GetPlcOnlineState_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            GetPlcOnlineStateThrows = new InvalidOperationException("No XAE running"),
        };

        Assert.Throws<InvalidOperationException>(() => fake.GetPlcOnlineState());
        Assert.Contains("GetPlcOnlineState()", fake.CallLog);
    }

    // ---- MCP JSON shaping ---------------------------------------------------

    [Fact]
    public void Mcp_Offline_SerializesIsOnlineFalse()
    {
        var fake = new FakeTwinCatSession
        {
            FakePlcOnlineStateResult = new PlcOnlineStateResult(false, "XAE is Offline."),
        };

        var json = SerializePlcOnlineState(fake);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("isOnline").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public void Mcp_Online_SerializesIsOnlineTrue()
    {
        var fake = new FakeTwinCatSession
        {
            FakePlcOnlineStateResult = new PlcOnlineStateResult(true, "XAE is Online."),
        };

        var json = SerializePlcOnlineState(fake);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("isOnline").GetBoolean());
    }

    [Fact]
    public void Mcp_Unknown_SerializesIsOnlineNull()
    {
        var fake = new FakeTwinCatSession
        {
            FakePlcOnlineStateResult = new PlcOnlineStateResult(null, "State indeterminate."),
        };

        var json = SerializePlcOnlineState(fake);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("isOnline").ValueKind);
    }

    [Fact]
    public void Mcp_Throws_ReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            GetPlcOnlineStateThrows = new InvalidOperationException("DTE not available"),
        };

        var result = InvokeMcpTool(fake);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("DTE not available", result);
    }

    // ---- Helpers ------------------------------------------------------------

    /// <summary>
    /// Simulates the JSON serialisation that TwinCatMcpTools does, directly on
    /// the fake session — no MCP stack needed for unit tests.
    /// </summary>
    private static string SerializePlcOnlineState(FakeTwinCatSession fake)
    {
        var result = fake.GetPlcOnlineState();
        return JsonSerializer.Serialize(new { isOnline = result.IsOnline, message = result.Message });
    }

    /// <summary>
    /// Simulates the MCP tool's try/catch error-string pattern.
    /// </summary>
    private static string InvokeMcpTool(FakeTwinCatSession fake)
    {
        try
        {
            var result = fake.GetPlcOnlineState();
            return JsonSerializer.Serialize(new { isOnline = result.IsOnline, message = result.Message });
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }
}
