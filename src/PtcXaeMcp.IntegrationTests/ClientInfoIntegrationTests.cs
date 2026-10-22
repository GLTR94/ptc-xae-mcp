using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Regression guard for #71. The host now registers every client into the
/// session registry at the MCP <c>initialize</c> handshake (via
/// <c>ISessionMigrationHandler</c>), instead of relying on the
/// <c>twincat_get_client_info</c> tool to populate it. That rewired the
/// Streamable-HTTP transport configuration, so this exercises the connect →
/// handshake → registry → tool path end-to-end against the live server and
/// asserts a connected client resolves to a real session (not the degraded
/// fallback). Skips unless the live server is reachable (hard-fails when
/// REQUIRE_LIVE=1).
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class ClientInfoIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
    private McpClient? _client;
    private bool _ready;

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is null) { LiveXae.Announce(false, string.Empty); return; }

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
        LiveXae.Announce(_ready, fixture);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private void SkipIfNotReady() => LiveXae.GuardReady(_ready);

    [Trait("Description", "Verbundener Client wird über den initialize-Handshake registriert → twincat_get_client_info löst diese Session auf (nicht der [Stale]/[Error]-Fallback).")]
    [SkippableFact(Timeout = 60_000)]
    public async Task GetClientInfo_AfterConnect_ResolvesThisSession()
    {
        SkipIfNotReady();

        var info = GetText(await _client!.CallToolAsync("twincat_get_client_info"));

        // The resolved-this-session form proves the handshake → registry path
        // is intact through the rewired transport. The degraded branches
        // ("[Stale] ..." / "[Error] No client session ...") would mean the
        // session pipeline is broken — exactly the #71 failure mode.
        Assert.StartsWith("Client (this session):", info);
        Assert.Contains("Session id:", info);
        Assert.DoesNotContain("[Stale]", info);
        Assert.DoesNotContain("[Error]", info);
        // Identity is self-reported but must not be the empty/unknown sentinel
        // for a properly handshaken client.
        Assert.DoesNotContain("(unknown)", info);
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
