using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Opens the fixture .sln, then exercises twincat_close_project against the
/// live MCP server and XAE. Skips unless PTC_XAE_MCP_FIXTURE_SLN is set
/// and the MCP server is reachable on http://localhost:5010.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class CloseProjectIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
    private const string FixtureEnvVar = "PTC_XAE_MCP_FIXTURE_SLN";
    private McpClient? _client;
    private bool _ready;
    private string _fixtureSln = string.Empty;

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is null) { LiveXae.Announce(false, string.Empty); return; }
        _fixtureSln = fixture;
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(ServerUrl),
                ConnectionTimeout = TimeSpan.FromSeconds(5)
            });
            _client = await McpClient.CreateAsync(transport,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            _ready = true;
        }
        catch { _ready = false; }
        LiveXae.Announce(_ready, _fixtureSln);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private void SkipIfNotReady() => LiveXae.GuardReady(_ready);

    [Trait("Description", "Geöffnetes Projekt schließen → Antwort enthält \"Closed\".")]
    [SkippableFact(Timeout = 120_000)]
    public async Task CloseProject_AfterOpen_ReturnsClosed()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var close = await _client.CallToolAsync("twincat_close_project",
            new Dictionary<string, object?> { ["saveFirst"] = false });
        var text = GetText(close);

        Assert.False(close.IsError == true, $"Close returned error: {text}");
        Assert.Contains("Closed", text);
    }

    [Trait("Description", "Schließen ohne geöffnetes Projekt → Fehlermeldung \"[Error] No solution\".")]
    [SkippableFact(Timeout = 60_000)]
    public async Task CloseProject_WhenNoSolutionOpen_ReturnsError()
    {
        SkipIfNotReady();

        // Ensure nothing is open (close any leftover from previous tests).
        await _client!.CallToolAsync("twincat_close_project",
            new Dictionary<string, object?> { ["saveFirst"] = false });

        var result = await _client.CallToolAsync("twincat_close_project",
            new Dictionary<string, object?> { ["saveFirst"] = false });
        var text = GetText(result);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("No solution", text, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Description", "Ungespeicherte Änderung (Task-Zykluszeit) + Schließen mit saveFirst=true → kehrt sofort zurück, kein Speichern-Dialog bleibt offen, Änderung steht auf der Platte.")]
    [SkippableFact(Timeout = 300_000)]
    public Task CloseProject_UnsavedChange_SaveFirst_SavesWithoutHanging() =>
        CloseWithUnsavedChangeAsync(saveFirst: true);

    [Trait("Description", "Ungespeicherte Änderung (Task-Zykluszeit) + Schließen mit saveFirst=false → kehrt sofort zurück, kein Dialog, Änderung verworfen.")]
    [SkippableFact(Timeout = 300_000)]
    public Task CloseProject_UnsavedChange_NoSave_DiscardsWithoutPrompt() =>
        CloseWithUnsavedChangeAsync(saveFirst: false);

    // Reported failure: close_project(saveFirst=true) with an unsaved change left the XAE's WPF prompt
    // "Sollen die Änderungen … gespeichert werden?" open and blocked ~196 s. Runs on a private fixture copy
    // (the change is a real project edit) and reopens the shared fixture afterwards.
    private async Task CloseWithUnsavedChangeAsync(bool saveFirst)
    {
        SkipIfNotReady();
        string? scratchSln = FixtureLayout.PrepareScratchCopy(saveFirst ? "close-save" : "close-nosave");
        Assert.NotNull(scratchSln);
        string tsproj = Directory.EnumerateFiles(Path.GetDirectoryName(scratchSln)!, "*.tsproj", SearchOption.AllDirectories).Single();
        int before = FirstTaskCycleTime(tsproj);
        int changed = before == 200_000 ? 100_000 : 200_000;

        try
        {
            var open = await _client!.CallToolAsync("twincat_open_project",
                new Dictionary<string, object?> { ["slnPath"] = scratchSln, ["closeCurrentFirst"] = true });
            Assert.False(open.IsError == true, GetText(open));

            Assert.True(XaeDte.SetFirstTaskCycleTimeUnsaved(changed),
                "Arrange failed: the task change did not leave the project dirty in the XAE.");
            Assert.Equal(before, FirstTaskCycleTime(tsproj)); // not on disk yet

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var close = await _client.CallToolAsync("twincat_close_project",
                new Dictionary<string, object?> { ["saveFirst"] = saveFirst });
            sw.Stop();
            string text = GetText(close);

            Assert.False(close.IsError == true, $"Close returned error: {text}");
            Assert.StartsWith(saveFirst ? "Saved and closed" : "Closed (without save)", text);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"Close took {sw.Elapsed.TotalSeconds:F0} s: {text}");

            var active = await _client.CallToolAsync("twincat_get_active_project", new Dictionary<string, object?>());
            Assert.Contains("\"solutionOpen\":false", GetText(active));

            Assert.Equal(saveFirst ? changed : before, FirstTaskCycleTime(tsproj));
        }
        finally
        {
            // Leave the shared fixture open again for the rest of the suite.
            await _client!.CallToolAsync("twincat_open_project",
                new Dictionary<string, object?> { ["slnPath"] = _fixtureSln, ["closeCurrentFirst"] = true });
        }
    }

    private static int FirstTaskCycleTime(string tsproj)
    {
        var task = System.Xml.Linq.XDocument.Load(tsproj).Descendants("Task").First();
        return int.Parse(task.Attribute("CycleTime")!.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
