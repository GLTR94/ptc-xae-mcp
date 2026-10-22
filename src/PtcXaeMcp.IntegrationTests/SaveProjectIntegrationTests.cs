using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Opens the fixture .sln then exercises twincat_save_project against the
/// live MCP server and XAE. Skips unless PTC_XAE_MCP_FIXTURE_SLN is set
/// and the MCP server is reachable on http://localhost:5010.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class SaveProjectIntegrationTests : IAsyncLifetime
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

    [Trait("Description", "Geöffnetes Projekt speichern → Antwort beginnt mit \"Saved\".")]
    [SkippableFact(Timeout = 120_000)]
    public async Task SaveProject_AfterOpen_ReturnsSaved()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var save = await _client.CallToolAsync("twincat_save_project");
        var text = GetText(save);

        Assert.False(save.IsError == true, $"Save returned error: {text}");
        Assert.StartsWith("Saved", text);
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
