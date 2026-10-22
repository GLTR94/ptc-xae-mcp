using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>Editor navigation: locate_step, goto_line, show_step.</summary>
public partial class McpServerTests
{
    // ── editor-navigation (twincat_locate_step / twincat_goto_line / twincat_show_step) ──

    [SkippableFact]
    public async Task EditorNavigation_ToolsAreListed()
    {
        SkipIfNotReachable();

        var names = (await _client!.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.Contains("twincat_locate_step", names);
        Assert.Contains("twincat_goto_line", names);
        Assert.Contains("twincat_show_step", names);
    }

    [SkippableFact]
    public async Task LocateStep_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var text = GetText(await CallToolAsync("twincat_locate_step",
            new Dictionary<string, object?> { ["treePath"] = "", ["step"] = 1010 }));

        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task LocateStep_BadStepVariable_ReturnsErrorBeforeXae()
    {
        SkipIfNotReachable();

        // Validated before any session touch — deterministic even without XAE.
        var text = GetText(await CallToolAsync("twincat_locate_step",
            new Dictionary<string, object?> { ["treePath"] = "TIPC^PLC^X", ["step"] = 1, ["stepVariable"] = "1 bad" }));

        Assert.StartsWith("[Error]", text);
        Assert.Contains("stepVariable", text);
    }

    [SkippableFact]
    public async Task GotoLine_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var text = GetText(await CallToolAsync("twincat_goto_line",
            new Dictionary<string, object?> { ["treePath"] = "", ["line"] = 5 }));

        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task GotoLine_LineZero_ReturnsError()
    {
        SkipIfNotReachable();

        var text = GetText(await CallToolAsync("twincat_goto_line",
            new Dictionary<string, object?> { ["treePath"] = "TIPC^PLC^X", ["line"] = 0 }));

        Assert.StartsWith("[Error]", text);
        Assert.Contains("line must be >= 1", text);
    }

    [SkippableFact]
    public async Task GotoLine_BadSection_ReturnsError()
    {
        SkipIfNotReachable();

        var text = GetText(await CallToolAsync("twincat_goto_line",
            new Dictionary<string, object?> { ["treePath"] = "TIPC^PLC^X", ["line"] = 5, ["section"] = "body" }));

        Assert.StartsWith("[Error]", text);
        Assert.Contains("section", text);
    }

    [SkippableFact]
    public async Task ShowStep_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var text = GetText(await CallToolAsync("twincat_show_step",
            new Dictionary<string, object?> { ["treePath"] = " ", ["step"] = 1010 }));

        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task ShowStep_UnknownPou_ReturnsStructuredError()
    {
        SkipIfNotReachable();

        // With XAE + solution: the POU lookup fails → [Error] before any editor action.
        // Without: the guard error. Either way a structured [Error], never a fault.
        var text = GetText(await CallToolAsync("twincat_show_step",
            new Dictionary<string, object?> { ["treePath"] = "TIPC^Nope^DoesNotExist", ["step"] = 1010 }));

        Assert.StartsWith("[Error]", text);
        Assert.True(text.Length > 8, $"Error must be structured: '{text}'");
    }
}
