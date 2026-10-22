namespace PtcXaeMcp.McpTests;

/// <summary>plc-instance-settings tools over the wire: argument contract and tool-error answers (never a
/// protocol exception). read_*/get_* are on NonMutatingTools; write_*/set_* only run without an open solution.</summary>
public partial class McpServerTests
{
    [SkippableFact]
    public async Task ReadInitSymbols_IsAToolAnswer()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_init_symbols", new Dictionary<string, object?>());

        var text = GetText(result);
        Assert.DoesNotContain("An error occurred invoking", text);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [SkippableFact]
    public async Task ReadInitSymbols_UnknownProject_IsAToolError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_init_symbols",
            new Dictionary<string, object?> { ["plcProjectName"] = "NoSuchPlc_4711" });

        Assert.StartsWith("[Error]", GetText(result));
    }

    [SkippableFact]
    public async Task WriteInitSymbol_WithoutSolution_IsAToolError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_write_init_symbol",
            new Dictionary<string, object?> { ["symbol"] = "GVL_Fixture.nInitParam", ["value"] = "42" });

        var text = GetText(result);
        Assert.DoesNotContain("An error occurred invoking", text);
        Assert.StartsWith("[Error]", text);
    }

    [SkippableFact]
    public async Task ReadRetainConfig_IsAToolAnswer()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_retain_config", new Dictionary<string, object?>());

        var text = GetText(result);
        Assert.DoesNotContain("An error occurred invoking", text);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [SkippableFact]
    public async Task GetTypeFileGeneration_IsAToolAnswer()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_get_type_file_generation", new Dictionary<string, object?>());

        var text = GetText(result);
        Assert.DoesNotContain("An error occurred invoking", text);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [SkippableFact]
    public async Task SetTypeFileGeneration_ConflictingFlags_IsAToolError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_set_type_file_generation",
            new Dictionary<string, object?> { ["tmc"] = false, ["tpy"] = true });

        var text = GetText(result);
        Assert.DoesNotContain("An error occurred invoking", text);
        Assert.StartsWith("[Error]", text);
    }
}