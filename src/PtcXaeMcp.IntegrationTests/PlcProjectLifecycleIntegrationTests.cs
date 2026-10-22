using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// plc-project-lifecycle end to end. Never mutates the fixture copy
/// the other tests use: the fixture is archived (.tszip) and unpacked as a scratch solution in %TEMP%,
/// every PLC-project operation runs there, and the fixture is reopened at the end.
/// Flow: save_archive → open_archive → plc_project_create (Standard) → build_plc green → export .tpzip →
/// import under a new name (same POU files) → instance_from_tmc refusal (module name taken) → reopen fixture.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public sealed class PlcProjectLifecycleIntegrationTests : IAsyncLifetime
{
    private McpClient? _client;
    private bool _ready;
    private string _fixtureSln = string.Empty;
    private readonly string _work = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp-lifecycle-" + Guid.NewGuid().ToString("N")[..8]);

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is not null)
        {
            _fixtureSln = fixture;
            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri("http://localhost:5010/"),
                    ConnectionTimeout = TimeSpan.FromSeconds(5),
                });
                _client = await McpClient.CreateAsync(transport,
                    cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
                // Start from the fixture (an earlier class may have closed it), like OpenProjectIntegrationTests.
                await _client.CallToolAsync("twincat_open_project",
                    new Dictionary<string, object?> { ["slnPath"] = _fixtureSln, ["closeCurrentFirst"] = true });
                _ready = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _ready = false;
            }
        }
        LiveXae.Announce(_ready, _fixtureSln);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            // Always hand the fixture back to the following tests.
            try
            {
                await _client.CallToolAsync("twincat_open_project",
                    new Dictionary<string, object?> { ["slnPath"] = _fixtureSln, ["closeCurrentFirst"] = true });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.WriteLine($"[lifecycle] reopening the fixture failed: {ex.Message}");
            }
            await _client.DisposeAsync();
        }
        try { if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true); }
        catch (IOException) { /* the XAE may still hold a file; %TEMP% is cleaned later */ }
        catch (UnauthorizedAccessException) { }
    }

    [Trait("Description", "Fixture als .tszip archivieren, als Scratch-Solution öffnen, PLC-Projekt anlegen, bauen, als .tpzip exportieren und unter neuem Namen importieren; danach Fixture wieder öffnen.")]
    [SkippableFact(Timeout = 900_000)]
    public async Task Lifecycle_ArchiveOpenCreateBuildExportImport()
    {
        LiveXae.GuardReady(_ready);
        Directory.CreateDirectory(_work);

        // 1. Archive the open fixture.
        string tszip = Path.Combine(_work, "Fixture.tszip");
        string text = await Call("twincat_solution_save_archive", new() { ["tszipPath"] = tszip });
        Assert.StartsWith("Saved the solution as archive", text);
        Assert.True(new FileInfo(tszip).Length > 0);

        // 2. Unpack it as a scratch solution and open it.
        string dest = Path.Combine(_work, "Scratch");
        text = await Call("twincat_solution_open_archive",
            new() { ["tszipPath"] = tszip, ["destDir"] = dest, ["name"] = "LcScratch", ["closeCurrentFirst"] = true });
        Assert.StartsWith("Opened archive", text);
        string active = await Call("twincat_get_active_project", new());
        Assert.Contains("LcScratch", active);

        // 3. New PLC project from the Beckhoff standard template; a bad template is refused, not a dialog.
        text = await Call("twincat_plc_project_create", new() { ["name"] = "LcStd", ["template"] = "No Such Template" }, expectError: true);
        Assert.Contains("not found", text);
        text = await Call("twincat_plc_project_create", new() { ["name"] = "LcStd" });
        Assert.StartsWith("Created PLC project 'LcStd'", text);
        text = await Call("twincat_plc_project_create", new() { ["name"] = "LcStd" }, expectError: true);
        Assert.Contains("already exists", text);

        // 4. It builds.
        text = await Call("twincat_build_plc", new() { ["plcProjectName"] = "LcStd" });
        Assert.Contains("Build succeeded", text, StringComparison.OrdinalIgnoreCase);

        // 5. Export → import under a new name: same POU files.
        string tpzip = Path.Combine(_work, "LcStd.tpzip");
        text = await Call("twincat_plc_project_export", new() { ["plcProjectName"] = "LcStd", ["tpzipPath"] = tpzip });
        Assert.StartsWith("Exported PLC project 'LcStd'", text);
        text = await Call("twincat_plc_project_import", new() { ["tpzipPath"] = tpzip, ["name"] = "LcImp" });
        Assert.StartsWith("Imported", text);
        Assert.Contains("LcImp", text);
        Assert.Equal(PouFiles(Path.Combine(dest, "LcStd")), PouFiles(Path.Combine(dest, "LcImp")));

        // 6. A .tmc whose module name is already a TIPC child is refused before the XAE (live: it would duplicate it).
        string tmc = Directory.GetFiles(dest, "*.tmc", SearchOption.AllDirectories)
            .First(f => Path.GetFileName(Path.GetDirectoryName(f)!) == FixtureLayout.PlcName);
        text = await Call("twincat_plc_instance_from_tmc", new() { ["tmcPath"] = tmc }, expectError: true);
        Assert.Contains("already exists", text);
    }

    private static string[] PouFiles(string projectDir) =>
        Directory.GetFiles(projectDir, "*.TcPOU", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(projectDir, f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task<string> Call(string tool, Dictionary<string, object?> args, bool expectError = false)
    {
        var result = await _client!.CallToolAsync(tool, args);
        string text = string.Join("\n", (result.Content ?? []).OfType<TextContentBlock>().Select(c => c.Text));
        Console.WriteLine($"[lifecycle] {tool}: {text}");
        if (expectError)
            Assert.StartsWith("[Error]", text);
        else
            Assert.False(text.StartsWith("[Error]", StringComparison.Ordinal), $"{tool} failed: {text}");
        return text;
    }
}
