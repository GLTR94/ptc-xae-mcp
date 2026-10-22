using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>IO mapping tools: link/unlink, mapping info, device disable.</summary>
public partial class McpServerTests
{
    // ── IO Mapping tools ───────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T32_IoMappingTools_AreListed()
    {
        SkipIfNotReachable();

        var tools = await _client!.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();

        Assert.Contains("twincat_link_variables", names);
        Assert.Contains("twincat_unlink_variables", names);
        Assert.Contains("twincat_get_mapping_info", names);
        Assert.Contains("twincat_set_device_disabled", names);
    }

    [SkippableFact]
    public async Task T33_LinkVariables_EmptyV1_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_link_variables",
            new Dictionary<string, object?> { ["v1"] = "", ["v2"] = "TIID^Device 1^Ch1" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T34_LinkVariables_EmptyV2_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_link_variables",
            new Dictionary<string, object?> { ["v1"] = "TIPC^PLC^MAIN.bVar", ["v2"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("v2", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T35_UnlinkVariables_EmptyV1_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_unlink_variables",
            new Dictionary<string, object?> { ["v1"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T36_GetMappingInfo_WritesFileOrReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_get_mapping_info",
            new Dictionary<string, object?>());

        var text = GetText(result);
        Assert.True(
            text.StartsWith("[Error]", StringComparison.Ordinal) ||
            text.Contains("Mappings.xml", StringComparison.OrdinalIgnoreCase),
            $"Expected Mappings.xml path or [Error], got: {text}");
    }

    [SkippableFact]
    public async Task T37_SetDeviceDisabled_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_set_device_disabled",
            new Dictionary<string, object?> { ["treePath"] = "", ["disabled"] = true });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }
}
