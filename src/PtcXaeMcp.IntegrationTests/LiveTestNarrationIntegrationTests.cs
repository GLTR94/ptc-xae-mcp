using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// End-to-end proof of the live-narration pipeline. The assembly-wide
/// <see cref="LiveTestTraceAttribute"/> fires a <c>start</c> marker immediately
/// before this test's body runs, so by the time the body executes the host's
/// <c>McpActivity.CurrentTest</c> must already report THIS test. Reading it back
/// over MCP (<c>ptc_test_marker phase=current</c>) therefore returns this
/// method's own name — proving Before → ptc_test_marker → CurrentTest works on a
/// real host. Skips (or hard-fails under REQUIRE_LIVE) when the server is down.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class LiveTestNarrationIntegrationTests : IAsyncLifetime
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

    [Trait("Description", "Live-Narration-Pipeline: vor diesem Test feuert LiveTestTrace einen Start-Marker → der Host muss diesen Test als laufend melden (ptc_test_marker phase=current).")]
    [SkippableFact(Timeout = 60_000)]
    public async Task CurrentTest_OnTheHost_IsThisRunningTest()
    {
        LiveXae.GuardReady(_ready);

        var result = await _client!.CallToolAsync("ptc_test_marker",
            new Dictionary<string, object?> { ["phase"] = "current" },
            cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        var text = string.Join("\n",
            result.Content.OfType<TextContentBlock>().Select(c => c.Text));

        Assert.False(string.IsNullOrWhiteSpace(text), "phase=current returned nothing");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("running").GetBoolean(),
            $"host should report a test running while this body executes; got: {text}");
        Assert.Equal(
            nameof(CurrentTest_OnTheHost_IsThisRunningTest),
            root.GetProperty("name").GetString());
    }
}
