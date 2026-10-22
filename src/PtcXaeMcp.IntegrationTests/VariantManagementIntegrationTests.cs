using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Live integration tests for twincat_get_variant_status and twincat_set_active_variant.
/// Requires: the neutral fixture open in XAE, MCP server on port 5010.
/// The round-trip test is skipped when fewer than 2 variants are defined.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class VariantManagementIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
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

    private async Task<string> CallAsync(string tool, Dictionary<string, object?>? args = null)
    {
        var result = args is null
            ? await _client!.CallToolAsync(tool)
            : await _client!.CallToolAsync(tool, args,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    [SkippableFact]
    public async Task GetVariantStatus_ReturnsJson()
    {
        SkipIfNotReady();

        string text = await CallAsync("twincat_get_variant_status");

        Assert.False(text.StartsWith("[Error]", StringComparison.Ordinal), $"Unexpected error: {text}");

        using var doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.TryGetProperty("activeVariant", out _),
            "Expected 'activeVariant' property in response");
        Assert.True(doc.RootElement.TryGetProperty("availableVariants", out _),
            "Expected 'availableVariants' property in response");
        Assert.True(doc.RootElement.TryGetProperty("groups", out _),
            "Expected 'groups' property in response");
    }

    [SkippableFact]
    public async Task SetActiveVariant_RoundTrip()
    {
        SkipIfNotReady();

        // Read current state
        string statusJson = await CallAsync("twincat_get_variant_status");
        if (statusJson.StartsWith("[Error]", StringComparison.Ordinal))
        {
            Assert.Fail($"GetVariantStatus failed unexpectedly: {statusJson}");
            return;
        }

        using var doc = JsonDocument.Parse(statusJson);
        string originalVariant = doc.RootElement.GetProperty("activeVariant").GetString() ?? string.Empty;
        var variants = doc.RootElement.GetProperty("availableVariants")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();

        if (variants.Count < 2)
            return; // fewer than 2 variants in fixture — round-trip not exercisable

        // Pick a variant that is not the current one
        string target = variants.First(v => v != originalVariant);

        // Switch without rebuild
        string setResult = await CallAsync("twincat_set_active_variant",
            new Dictionary<string, object?> { ["variantName"] = target, ["rebuildAfter"] = false });
        Assert.DoesNotContain("[Error]", setResult, StringComparison.Ordinal);

        // Verify the switch took effect
        string newStatusJson = await CallAsync("twincat_get_variant_status");
        using var newDoc = JsonDocument.Parse(newStatusJson);
        string newActive = newDoc.RootElement.GetProperty("activeVariant").GetString() ?? string.Empty;
        Assert.Equal(target, newActive);

        // Restore original variant
        if (!string.IsNullOrEmpty(originalVariant))
        {
            await CallAsync("twincat_set_active_variant",
                new Dictionary<string, object?> { ["variantName"] = originalVariant, ["rebuildAfter"] = false });
        }
    }
}
