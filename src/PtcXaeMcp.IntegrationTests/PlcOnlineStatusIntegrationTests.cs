using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Stage XAE — twincat_get_plc_online_state against a live XAE with
/// the neutral fixture open and XAE NOT logged in (offline).
///
/// Preconditions: REQUIRE_LIVE=1, PTC_XAE_MCP_FIXTURE_SLN set,
/// the neutral fixture open, MCP server on port 5010.
/// The isOnline=true path is deferred to manual smoke-test (requires
/// logging in to the PLC before the test run — too intrusive for automation).
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class PlcOnlineStatusIntegrationTests : IAsyncLifetime
{
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
                Endpoint = new Uri(LiveXae.ServerUrl),
                ConnectionTimeout = TimeSpan.FromSeconds(5),
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

    private async Task<string> CallAsync(string tool)
    {
        var result = await _client!.CallToolAsync(tool,
            cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    [Trait("Description", "Ohne Login: isOnline ist false oder null (nie true).")]
    [SkippableFact(Timeout = 30_000)]
    public async Task GetPlcOnlineState_WhenNotLoggedIn_IsOnlineIsFalseOrNull()
    {
        SkipIfNotReady();

        var text = await CallAsync("twincat_get_plc_online_state");

        Assert.False(text.StartsWith("[Error]", StringComparison.Ordinal), $"Tool returned error: {text}");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        // Must have message field with content
        var message = root.GetProperty("message").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message), $"message was empty in: {text}");

        // When XAE is not logged in, isOnline must be false or null (never true)
        var isOnlineProp = root.GetProperty("isOnline");
        bool? isOnline = isOnlineProp.ValueKind == JsonValueKind.Null
            ? null
            : isOnlineProp.GetBoolean();

        Assert.True(isOnline != true,
            $"Expected isOnline=false or null when XAE is not logged in, got: {text}");
    }

    [Trait("Description", "Ergebnis enthält immer ein nicht-leeres message-Feld.")]
    [SkippableFact(Timeout = 30_000)]
    public async Task GetPlcOnlineState_AlwaysReturnsNonEmptyMessage()
    {
        SkipIfNotReady();

        var text = await CallAsync("twincat_get_plc_online_state");

        Assert.False(text.StartsWith("[Error]", StringComparison.Ordinal), $"Tool returned error: {text}");

        using var doc = JsonDocument.Parse(text);
        var message = doc.RootElement.GetProperty("message").GetString();
        Assert.False(string.IsNullOrWhiteSpace(message), $"message was empty in: {text}");
    }
}
