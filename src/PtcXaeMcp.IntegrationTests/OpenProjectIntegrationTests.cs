using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// End-to-end tests for twincat_open_project against a running XAE instance.
/// Skips unless PTC_XAE_MCP_FIXTURE_SLN points to a real .sln on disk
/// AND the MCP server is reachable on http://localhost:5010.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class OpenProjectIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
    private const string FixtureEnvVar = "PTC_XAE_MCP_FIXTURE_SLN";
    private McpClient? _client;
    private bool _ready;
    private string _fixtureSln = string.Empty;

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is null)
        {
            LiveXae.Announce(false, string.Empty);
            return;
        }

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
        catch
        {
            _ready = false;
        }

        if (_ready)
        {
            // Defensive: earlier test classes (e.g. CloseProjectIntegrationTests) may
            // leave XAE with no solution loaded.  Ensure the fixture is open before
            // this class's tests run so they start from a known state.  Fix for #89.
            await _client!.CallToolAsync("twincat_open_project",
                new Dictionary<string, object?>
                {
                    ["slnPath"] = _fixtureSln,
                    ["closeCurrentFirst"] = true,
                });
        }

        LiveXae.Announce(_ready, _fixtureSln);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }

    private void SkipIfNotReady() => LiveXae.GuardReady(_ready);

    [Trait("Description", "Fixture-.sln öffnen → Antwort beginnt mit \"Opened\" und enthält den Solution-Pfad.")]
    [SkippableFact(Timeout = 120_000)]
    public async Task OpenProject_OpensFixtureSln_AndReportsActiveProject()
    {
        SkipIfNotReady();

        var result = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });

        var text = GetText(result);
        Assert.False(result.IsError == true, $"Tool returned error: {text}");
        Assert.StartsWith("Opened", text);
        Assert.Contains(_fixtureSln, text);
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0)
            return string.Empty;
        return string.Join("\n", content
            .OfType<TextContentBlock>()
            .Select(c => c.Text));
    }
}
