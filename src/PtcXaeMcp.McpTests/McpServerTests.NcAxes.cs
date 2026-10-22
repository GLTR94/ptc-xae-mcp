using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>NC/motion tools.</summary>
public partial class McpServerTests
{
    // ── nc-axes (Motion) tools ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task Nc_ListNcTasksAxes_RunsAndReturnsString()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_list_nc_tasks_axes");

        var text = GetText(result);
        bool ok = text.StartsWith("Found", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("No NC-Tasks", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(ok, $"Unexpected response: {text}");
    }

    [SkippableFact]
    public async Task Nc_ReadAxisParams_MissingPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_axis_params",
            new Dictionary<string, object?> { ["axisPath"] = "  " });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("axisPath", text);
    }

    [SkippableFact]
    public async Task Nc_CreateNcTask_EmptyName_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_create_nc_task",
            new Dictionary<string, object?> { ["name"] = "  " });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("name", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Nc_CreateAxis_MissingTaskPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_create_axis",
            new Dictionary<string, object?> { ["ncTaskPath"] = "", ["axisName"] = "Axis 1" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("ncTaskPath", text);
    }

    [SkippableFact]
    public async Task Nc_ConfigureAxis_UnknownField_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_configure_axis",
            new Dictionary<string, object?>
            {
                ["axisPath"] = "TINC^NC-Task 1^Axes^Axis 1",
                // No mapped field supplied at all → "supply at least one field".
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    [SkippableFact]
    public async Task Nc_ConfigureAxis_OnlyAxisPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_configure_axis",
            new Dictionary<string, object?> { ["axisPath"] = "  " });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("axisPath", text);
    }
}
