using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>Scope measurement tools.</summary>
public partial class McpServerTests
{
    // ── scope-measurement ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task T65_ScopeTools_AreListed()
    {
        SkipIfNotReachable();

        var tools = await _client!.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();

        Assert.Contains("twincat_create_scope_project",  names);
        Assert.Contains("twincat_add_scope_channel",     names);
        Assert.Contains("twincat_set_scope_axis_mode",   names);
        Assert.Contains("twincat_set_scope_labels",      names);
        Assert.Contains("twincat_list_scope_channels",   names);
        Assert.Contains("twincat_get_scope_status",      names);
        Assert.Contains("twincat_export_scope_data",     names);
        Assert.Contains("twincat_read_scope_acquisition",      names);
        Assert.Contains("twincat_set_scope_acquisition_symbol", names);
    }

    [SkippableFact]
    public async Task T66_GetScopeStatus_NoProjectOpen_ReturnsErrorOrNotFound()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_get_scope_status");

        var text = GetText(result);
        bool ok = text.StartsWith("[Error]", StringComparison.Ordinal)
            || text.Contains("No TwinCAT Measurement", StringComparison.OrdinalIgnoreCase)
            || text.Contains("scope project", StringComparison.OrdinalIgnoreCase);
        Assert.True(ok, $"Unexpected response: {text}");
    }

    [SkippableFact]
    public async Task T67_ListScopeChannels_NoProjectOpen_ReturnsErrorOrEmpty()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_list_scope_channels");

        var text = GetText(result);
        bool ok = text.StartsWith("[Error]", StringComparison.Ordinal)
            || text.Contains("No scope project", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Found", StringComparison.OrdinalIgnoreCase);
        Assert.True(ok, $"Unexpected response: {text}");
    }

    [SkippableFact]
    public async Task T68_AddScopeChannel_EmptySymbol_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_add_scope_channel",
            new Dictionary<string, object?> { ["symbol"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("symbol", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T69_SetScopeAxisMode_UnknownMode_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_set_scope_axis_mode",
            new Dictionary<string, object?> { ["chartName"] = "Chart1", ["mode"] = "sideways" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("sideways", text);
    }

    [SkippableFact]
    public async Task T70_SetScopeAxisMode_EmptyChart_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_set_scope_axis_mode",
            new Dictionary<string, object?> { ["chartName"] = "", ["mode"] = "stacked" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("chartName", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T71_SetScopeLabels_EmptySelector_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_set_scope_labels",
            new Dictionary<string, object?> { ["nodeSelector"] = "", ["name"] = "x" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("nodeSelector", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T72_ExportScopeData_UnknownFormat_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_export_scope_data",
            new Dictionary<string, object?> { ["format"] = "XML" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("XML", text);
    }

    // ── scope-acquisition-symbols ─────────────────────────────────────────

    [SkippableFact]
    public async Task T79_ReadScopeAcquisition_NoProjectOpen_ReturnsErrorOrStructured()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_scope_acquisition",
            new Dictionary<string, object?> { ["acqSelector"] = "DoesNotExist" });

        var text = GetText(result);
        bool ok = text.StartsWith("[Error]", StringComparison.Ordinal)
            || text.Contains("Acquisition", StringComparison.OrdinalIgnoreCase);
        Assert.True(ok, $"Unexpected response: {text}");
    }

    [SkippableFact]
    public async Task T80_ReadScopeAcquisition_EmptySelector_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_scope_acquisition",
            new Dictionary<string, object?> { ["acqSelector"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("acqSelector", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T81_SetScopeAcquisitionSymbol_NoFields_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_set_scope_acquisition_symbol",
            new Dictionary<string, object?> { ["acqSelector"] = "Acq1" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("At least one", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T82_SetScopeAcquisitionSymbol_BadPort_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_set_scope_acquisition_symbol",
            new Dictionary<string, object?> { ["acqSelector"] = "Acq1", ["targetPort"] = 99999 });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("targetPort", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task T83_AddScopeChannel_UnknownSampleMode_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_add_scope_channel",
            new Dictionary<string, object?>
            {
                ["symbol"]     = "TIPC^PLC^MAIN.x",
                ["sampleMode"] = "Telepathy",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("Telepathy", text);
    }
}
