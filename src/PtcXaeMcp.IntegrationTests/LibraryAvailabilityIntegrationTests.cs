using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Opens the fixture .sln then calls twincat_check_libraries_installed against
/// the live MCP server and XAE. Skips unless PTC_XAE_MCP_FIXTURE_SLN is set
/// and the MCP server is reachable on http://localhost:5010.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class LibraryAvailabilityIntegrationTests : IAsyncLifetime
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

    [Trait("Description", "Alle SPS-Bibliotheksreferenzen prüfen → jede Referenz mit [Installed/Missing/VersionMismatch] gemeldet.")]
    [SkippableFact(Timeout = 180_000)]
    public async Task CheckLibrariesInstalled_AfterOpen_ReportsPerReferenceStatus()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var result = await _client.CallToolAsync("twincat_check_libraries_installed");
        var text = GetText(result);

        Assert.False(result.IsError == true, $"Check tool returned error: {text}");

        bool ok = text.StartsWith("All ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("library reference", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("No library references", StringComparison.OrdinalIgnoreCase);
        Assert.True(ok, $"Unexpected summary: {text}");

        // If anything was reported, the body must contain at least one per-reference
        // line tagged with a known status. "No library references" short-circuits
        // before producing per-line output, so skip that assertion in that case.
        if (!text.StartsWith("No library references", StringComparison.OrdinalIgnoreCase))
        {
            bool anyStatusLine = text.Contains("[Installed]")
                || text.Contains("[Missing]")
                || text.Contains("[VersionMismatch]");
            Assert.True(anyStatusLine,
                $"Expected at least one [Installed|Missing|VersionMismatch] line, got: {text}");
        }
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
