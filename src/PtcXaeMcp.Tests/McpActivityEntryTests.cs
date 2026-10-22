using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for <see cref="McpActivityEntry"/> — one traced MCP
/// tool call. No SDK, no XAE, no GUI.
/// </summary>
public class McpActivityEntryTests
{
    [Fact]
    public void Ok_FactoryMethod_SetsOkFlag_AndCarriesResponse()
    {
        var started = DateTimeOffset.UnixEpoch;
        var entry = McpActivityEntry.Ok(
            "twincat_get_status", "{}", "XAE: Active", started,
            TimeSpan.FromMilliseconds(42));

        Assert.True(entry.Success);
        Assert.Equal("twincat_get_status", entry.ToolName);
        Assert.Equal("{}", entry.Arguments);
        Assert.Equal("XAE: Active", entry.Response);
        Assert.Equal(started, entry.TimestampUtc);
        Assert.Equal(42, entry.Duration.TotalMilliseconds);
    }

    [Fact]
    public void Failure_FactoryMethod_ClearsOkFlag_AndCarriesErrorText()
    {
        var entry = McpActivityEntry.Failure(
            "twincat_open_project", "{\"slnPath\":\"x\"}",
            "boom", DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(1));

        Assert.False(entry.Success);
        Assert.Equal("boom", entry.Response);
        Assert.Equal("twincat_open_project", entry.ToolName);
    }

    [Fact]
    public void NullArguments_AreNormalisedToEmptyString()
    {
        var entry = McpActivityEntry.Ok(
            "t", null, "r", DateTimeOffset.UnixEpoch, TimeSpan.Zero);

        Assert.Equal(string.Empty, entry.Arguments);
    }

    [Fact]
    public void NullResponse_IsNormalisedToEmptyString()
    {
        var entry = McpActivityEntry.Ok(
            "t", "{}", null, DateTimeOffset.UnixEpoch, TimeSpan.Zero);

        Assert.Equal(string.Empty, entry.Response);
    }

    [Theory]
    [InlineData(7000, false)]   // under the 8 KB cap → untouched
    [InlineData(20000, true)]   // over the cap → truncated with a marker
    public void Truncate_OnlyTrimsWhenOverCap_AndAppendsMarker(int len, bool expectTrim)
    {
        var big = new string('x', len);

        var clipped = McpActivityEntry.Truncate(big);

        if (expectTrim)
        {
            Assert.True(clipped.Length < len);
            Assert.Contains("truncated", clipped);
        }
        else
        {
            Assert.Equal(big, clipped);
        }
    }

    [Fact]
    public void Truncate_NullInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, McpActivityEntry.Truncate(null));
    }
}
