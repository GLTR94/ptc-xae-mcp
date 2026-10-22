using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>ADS variable read/write tools.</summary>
public partial class McpServerTests
{
    // ── ads-variable-rw ────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task AdsVar_ToolsListed()
    {
        SkipIfNotReachable();
        var tools = await _client!.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("twincat_read_ads_variable", names);
        Assert.Contains("twincat_write_ads_variable", names);
        Assert.Contains("twincat_get_variant_status", names);
        Assert.Contains("twincat_set_active_variant", names);
    }

    [SkippableFact]
    public async Task AdsVar_Read_EmptySymbolPath_ReturnsError()
    {
        SkipIfNotReachable();
        var result = await CallToolAsync("twincat_read_ads_variable",
            new Dictionary<string, object?> { ["symbolPath"] = "" });
        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    [SkippableFact]
    public async Task AdsVar_Write_EmptySymbolPath_ReturnsError()
    {
        SkipIfNotReachable();
        var result = await CallToolAsync("twincat_write_ads_variable",
            new Dictionary<string, object?> { ["symbolPath"] = "", ["value"] = "true" });
        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    [SkippableFact]
    public async Task AdsVar_Read_ReturnsJsonOrError()
    {
        SkipIfNotReachable();
        var result = await CallToolAsync("twincat_read_ads_variable",
            new Dictionary<string, object?> { ["symbolPath"] = "GVL_Safety.bSI_DriveReleased" });
        var text = GetText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        bool isJson = text.TrimStart().StartsWith("{", StringComparison.Ordinal);
        bool isError = text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(isJson || isError, $"Unexpected response: {text}");
    }
}
