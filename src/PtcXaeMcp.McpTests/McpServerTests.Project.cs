using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>Project lifecycle (open/save/close validation), host resolution and active-project status.</summary>
public partial class McpServerTests
{
    // ── Test 8 ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T8_OpenProject_RelativePath_ReturnsValidationError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_open_project",
            new Dictionary<string, object?> { ["slnPath"] = "relative.sln" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("absolute", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 9 ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T9_OpenProject_WrongExtension_ReturnsValidationError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_open_project",
            new Dictionary<string, object?> { ["slnPath"] = @"C:\Windows\notepad.exe" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains(".sln", text);
    }

    // ── Test 10 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T10_OpenProject_NonExistentFile_ReturnsValidationError()
    {
        SkipIfNotReachable();

        var path = Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".sln");
        var result = await CallToolAsync("twincat_open_project",
            new Dictionary<string, object?> { ["slnPath"] = path });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("not found", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 11 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T11_SaveProject_WhenNoXaeOrNoSolution_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_save_project");

        var text = GetText(result);
        bool ok = text.StartsWith("Saved", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(ok, $"Unexpected response: {text}");
        if (result.IsError == true)
        {
            Assert.StartsWith("[Error]", text, StringComparison.Ordinal);
            Assert.True(text.Length > 10, $"Error response must not be empty: '{text}'");
        }
    }

    // ── Test 12 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T12_CloseProject_WhenNoXaeOrNoSolution_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_close_project",
            new Dictionary<string, object?> { ["saveFirst"] = false });

        var text = GetText(result);
        bool ok = text.StartsWith("Closed", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Saved", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(ok, $"Unexpected response: {text}");
    }

    // ── Test 54 — host-selection: tool is listed ──────────────────────────────

    [SkippableFact]
    public async Task T54_ResolveHost_IsListed()
    {
        SkipIfNotReachable();

        var tools = await _client!.ListToolsAsync();
        Assert.Contains("twincat_resolve_host", tools.Select(t => t.Name).ToHashSet());
    }

    // ── Test 55 — host-selection: 4026 .tsproj → 64-bit host ──────────────────

    [SkippableFact]
    public async Task T55_ResolveHost_4026Tsproj_ReportsShell64()
    {
        SkipIfNotReachable();

        var path = Path.Combine(Path.GetTempPath(),
            "hs55_" + Guid.NewGuid().ToString("N") + ".tsproj");
        await File.WriteAllTextAsync(path,
            "<TcSmProject TcVersion=\"3.1.4026.22\"><Project/></TcSmProject>");
        try
        {
            var result = await CallToolAsync("twincat_resolve_host",
                new Dictionary<string, object?> { ["tsprojOrSlnPath"] = path });

            var text = GetText(result);
            Assert.False(result.IsError == true, $"Tool error: {text}");
            Assert.Contains("TcXaeShell.DTE.17.0", text);
            Assert.Contains("Shell64", text);
            Assert.Contains("3.1.4026.22", text);
        }
        finally { File.Delete(path); }
    }

    // ── Test 56 — host-selection: 4024 .tsproj → 32-bit host ──────────────────

    [SkippableFact]
    public async Task T56_ResolveHost_4024Tsproj_ReportsShell32()
    {
        SkipIfNotReachable();

        var path = Path.Combine(Path.GetTempPath(),
            "hs56_" + Guid.NewGuid().ToString("N") + ".tsproj");
        await File.WriteAllTextAsync(path,
            "<TcSmProject TcVersion=\"3.1.4024.55\"><Project/></TcSmProject>");
        try
        {
            var result = await CallToolAsync("twincat_resolve_host",
                new Dictionary<string, object?> { ["tsprojOrSlnPath"] = path });

            var text = GetText(result);
            Assert.False(result.IsError == true, $"Tool error: {text}");
            Assert.Contains("TcXaeShell.DTE.15.0", text);
            Assert.Contains("Shell32", text);
        }
        finally { File.Delete(path); }
    }

    // ── Test 57 — host-selection: malformed path → clean error ────────────────

    [SkippableFact]
    public async Task T57_ResolveHost_BadPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_resolve_host",
            new Dictionary<string, object?>
            {
                ["tsprojOrSlnPath"] = @"X:\nope\does_not_exist.tsproj"
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.True(text.Length > 8, $"Error must be structured: '{text}'");
    }

    // ── Test 61 — active-project-status: tool is listed ───────────────────────

    [SkippableFact]
    public async Task T61_GetActiveProject_IsListed()
    {
        SkipIfNotReachable();

        var tools = await _client!.ListToolsAsync();
        Assert.Contains("twincat_get_active_project", tools.Select(t => t.Name).ToHashSet());
    }

    // ── Test 62 — active-project-status: returns JSON with required fields ─────

    [SkippableFact]
    public async Task T62_GetActiveProject_ReturnsJsonWithRequiredFields()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_get_active_project");

        var text = GetText(result);
        Assert.False(result.IsError == true, $"Tool returned error: {text}");
        Assert.False(string.IsNullOrWhiteSpace(text), "Response was empty");

        using var doc = System.Text.Json.JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("solutionOpen", out _),
            $"Missing 'solutionOpen' field in: {text}");
        Assert.True(root.TryGetProperty("solutionPath", out _),
            $"Missing 'solutionPath' field in: {text}");
        Assert.True(root.TryGetProperty("solutionName", out _),
            $"Missing 'solutionName' field in: {text}");
    }
}
