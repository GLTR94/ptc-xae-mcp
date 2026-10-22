using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Integration tests for the runtime-activation feature group that need no runtime: licence
/// info, the security dialog and the required-licence list. Tests run against a live XAE + MCP
/// server (PTC_XAE_MCP_FIXTURE_SLN must be set).
///
/// The activation itself lives in <see cref="RuntimeActivationXarIntegrationTests"/> (Stage XAR,
/// issue #195): here it activated whatever the fixture targeted. The neutral fixture has no target
/// NetId, so that was the local kernel runtime, and its fatal-error box blocked the rest of this stage.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class RuntimeActivationIntegrationTests : IAsyncLifetime
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

    private async Task OpenFixtureAsync()
    {
        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));
    }

    // ── License info (read-only) ──────────────────────────────────────────

    [Trait("Description", "twincat_get_license_info gibt Lizenzinformationen zurück (oder einen [Error] wenn kein Projekt offen ist).")]
    [SkippableFact(Timeout = 60_000)]
    public async Task GetLicenseInfo_WithOpenProject_ReturnsLicensesOrError()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        var result = await _client!.CallToolAsync("twincat_get_license_info",
            new Dictionary<string, object?>());
        var text = GetText(result);

        // Acceptable: license list OR a clear error (e.g. TIRC node not accessible)
        bool isLicenses = text.Contains("License",    StringComparison.OrdinalIgnoreCase)
                       || text.Contains("No license", StringComparison.OrdinalIgnoreCase);
        bool isError    = text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(isLicenses || isError,
            $"twincat_get_license_info returned unexpected response: {text}");
    }

    // ── Security dialog (no dialog open) ─────────────────────────────────

    [Trait("Description", "twincat_dismiss_security_dialog meldet 'Not found' wenn kein Dialog offen ist.")]
    [SkippableFact(Timeout = 30_000)]
    public async Task DismissSecurityDialog_WhenNoneOpen_ReportsNotFound()
    {
        SkipIfNotReady();

        var result = await _client!.CallToolAsync("twincat_dismiss_security_dialog",
            new Dictionary<string, object?>());
        var text = GetText(result);

        Assert.False(result.IsError == true, text);
        Assert.DoesNotContain("[Error]", text);
        // "dismissed" is also acceptable if a stale dialog happened to be open
        bool notFound = text.Contains("No ", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("dismissed", StringComparison.OrdinalIgnoreCase);
        Assert.True(notFound, $"Unexpected response: {text}");
    }

    // ── Required-license management ───────────────────────────────────────

    [Trait("Description", "twincat_list_required_licenses gibt die Pflichtlizenz-Liste zurück (oder none/error).")]
    [SkippableFact(Timeout = 60_000)]
    public async Task ListRequiredLicenses_WithOpenProject_ReturnsListOrNone()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        var result = await _client!.CallToolAsync("twincat_list_required_licenses",
            new Dictionary<string, object?>());
        var text = GetText(result);

        Assert.False(result.IsError == true, text);
        bool acceptable = text.Contains("Required licenses", StringComparison.OrdinalIgnoreCase)
                       || text.Contains("none", StringComparison.OrdinalIgnoreCase)
                       || text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(acceptable, $"twincat_list_required_licenses returned unexpected: {text}");
    }

    [Trait("Description", "twincat_add_required_license fügt eine Lizenz zur Pflichtliste hinzu; twincat_remove_required_license entfernt sie wieder. Nutzt eine synthetische Test-GUID, die nicht im echten Projekt vorhanden ist, damit kein echter Eintrag gelöscht wird.")]
    [SkippableFact(Timeout = 120_000)]
    public async Task AddAndRemoveRequiredLicense_RoundTrip_IdempotentAndCleanup()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        // Synthetic GUID — not a real Beckhoff license, not present in the fixture
        // project before the test runs.  Using a fake GUID avoids clobbering real entries.
        const string testGuid = "{AAAAAAAA-0000-4000-8000-000000000001}";

        // Add
        var addResult = await _client!.CallToolAsync("twincat_add_required_license",
            new Dictionary<string, object?> { ["licenseId"] = testGuid });
        var addText = GetText(addResult);
        Assert.False(addResult.IsError == true, addText);
        Assert.DoesNotContain("[Error]", addText);
        Assert.Contains("added", addText, StringComparison.OrdinalIgnoreCase);

        // List — must now contain the test GUID
        var listAfterAdd = await _client!.CallToolAsync("twincat_list_required_licenses",
            new Dictionary<string, object?>());
        var listText = GetText(listAfterAdd);
        Assert.Contains("AAAAAAAA", listText, StringComparison.OrdinalIgnoreCase);

        // Add again — must be idempotent (no close/reopen, no duplicate entry)
        var addAgain = await _client!.CallToolAsync("twincat_add_required_license",
            new Dictionary<string, object?> { ["licenseId"] = testGuid });
        var addAgainText = GetText(addAgain);
        Assert.DoesNotContain("[Error]", addAgainText);
        Assert.Contains("already", addAgainText, StringComparison.OrdinalIgnoreCase);

        // Remove
        var removeResult = await _client!.CallToolAsync("twincat_remove_required_license",
            new Dictionary<string, object?> { ["licenseId"] = testGuid });
        var removeText = GetText(removeResult);
        Assert.False(removeResult.IsError == true, removeText);
        Assert.DoesNotContain("[Error]", removeText);
        Assert.Contains("removed", removeText, StringComparison.OrdinalIgnoreCase);

        // Remove again — must be idempotent (WasAbsent = true, no close/reopen)
        var removeAgain = await _client!.CallToolAsync("twincat_remove_required_license",
            new Dictionary<string, object?> { ["licenseId"] = testGuid });
        var removeAgainText = GetText(removeAgain);
        Assert.DoesNotContain("[Error]", removeAgainText);

        // List — must no longer contain the test GUID
        var listAfterRemove = await _client!.CallToolAsync("twincat_list_required_licenses",
            new Dictionary<string, object?>());
        Assert.DoesNotContain("AAAAAAAA", GetText(listAfterRemove), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
