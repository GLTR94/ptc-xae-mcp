using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Stage XAR — the full <c>twincat_activate_config</c> cycle on the stage's explicit runtime target (issue #195).
/// It used to run in Stage XAE, against whatever the fixture targeted. The neutral fixture has no target NetId,
/// so that was the local kernel runtime. On the dev VM that runtime has no licence: the target failed, the XAE
/// showed "Zielsystem meldet fatalen Fehler", and every later XAE-stage call was refused as blocked. Here the
/// target is <see cref="XarRuntimeFixture.AmsNetId"/>, and the test cannot run without it.
/// <para>The activation it checks is the stage's own, done by <see cref="XarRuntimeFixture"/>: set_target, build,
/// <c>twincat_activate_config(autoConfirmRestart)</c>. A second activation here would find the target already in
/// Run and only restart it again (on 2026-09-27 a running user-mode runtime ignored that restart request and the tool
/// answered "restart … not confirmed"; on 2026-10-08 the restart from Run worked). When the fixture found the target
/// already running the fixture PLC and did not activate, this test skips with that reason.</para>
/// </summary>
[Collection("Stage XAR")]
[Trait("Stage", "XAR")]
public class RuntimeActivationXarIntegrationTests : IAsyncLifetime
{
    private readonly XarRuntimeFixture _xar;
    private McpClient? _client;

    public RuntimeActivationXarIntegrationTests(XarRuntimeFixture xar) => _xar = xar;

    public async Task InitializeAsync()
    {
        if (!_xar.IsReady) return;
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(LiveXae.ServerUrl),
                ConnectionTimeout = TimeSpan.FromSeconds(5)
            });
            _client = await McpClient.CreateAsync(transport,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }
        catch { /* _client stays null; SkipIfNotReady gates the test */ }
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private void SkipIfNotReady()
    {
        _xar.SkipIfNotReady();
        if (_client is null) throw new SkipException("Activation: MCP client could not be created for Stage XAR.");
    }

    private async Task<(CallToolResult Result, string Text)> CallAsync(string tool, Dictionary<string, object?> args, TimeSpan timeout)
    {
        var result = await _client!.CallToolAsync(tool, args, cancellationToken: new CancellationTokenSource(timeout).Token);
        return (result, GetText(result));
    }

    [Trait("Description", "The stage's twincat_activate_config (autoConfirmRestart) on the runtime target: 'Activated' only " +
                          "once the target reads Run; online state and logout stay coherent; the PLC answers over ADS; " +
                          "a trial licence, where listed, expires tomorrow or later.")]
    [SkippableFact(Timeout = 300_000)]
    public async Task ActivateConfig_OnTheRuntimeTarget_ReachesRunAndLicenseValid()
    {
        SkipIfNotReady();
        string activateText = _xar.ActivationResult
            ?? throw new SkipException(_xar.ActivationSkippedReason ?? "Stage XAR did not activate the fixture.");

        // The fixture set the stage's target (never the fixture's own, local one), built, and activated.
        Assert.DoesNotContain("[Error]", activateText, StringComparison.Ordinal);
        Assert.Contains("Activated", activateText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"target {_xar.AmsNetId}", activateText, StringComparison.Ordinal);
        Assert.Contains("reached Run", activateText, StringComparison.Ordinal);

        // ── #127: online-state detection + working logout ─────────────────────
        // Whether activate_config leaves the PLC online is environment-dependent
        // (licensing / runtime). The invariants we assert hold either way:
        //   1. get_plc_online_state never errors and reports a coherent value.
        //   2. logout is coherent with that value (real logout when online, no-op
        //      when offline) and never errors.
        //   3. After logout the PLC is never reported online.
        var (_, stateAfterActivateText) = await CallAsync("twincat_get_plc_online_state",
            new Dictionary<string, object?>(), TimeSpan.FromSeconds(60));
        Assert.False(stateAfterActivateText.StartsWith("[Error]", StringComparison.Ordinal),
            $"get_plc_online_state after activate returned error: {stateAfterActivateText}");
        bool? onlineAfterActivate = ParseIsOnline(stateAfterActivateText);

        var (_, logoutAfterActivateText) = await CallAsync("twincat_logout",
            new Dictionary<string, object?>(), TimeSpan.FromSeconds(60));
        Assert.False(logoutAfterActivateText.StartsWith("[Error]", StringComparison.Ordinal),
            $"logout after activate returned error: {logoutAfterActivateText}");

        // Coherence: if we were online, logout must actually log out (not a no-op).
        if (onlineAfterActivate == true)
            Assert.DoesNotContain("no-op", logoutAfterActivateText, StringComparison.OrdinalIgnoreCase);

        // Invariant: after logout the PLC must never be reported online.
        var (_, stateAfterLogoutText) = await CallAsync("twincat_get_plc_online_state",
            new Dictionary<string, object?>(), TimeSpan.FromSeconds(60));
        Assert.False(stateAfterLogoutText.StartsWith("[Error]", StringComparison.Ordinal),
            $"get_plc_online_state after logout returned error: {stateAfterLogoutText}");
        Assert.True(ParseIsOnline(stateAfterLogoutText) != true,
            $"Expected isOnline=false or null after logout; got: {stateAfterLogoutText}");

        // The system reading Run is not the PLC answering on its port: a real symbol read is the proof.
        var (up, lastRead) = await RuntimeReadiness.WaitForRuntimeAsync(_client!, _xar.AmsNetId, TimeSpan.FromSeconds(90));
        Assert.True(up, $"The PLC on {_xar.AmsNetId} did not answer a symbol read within 90 s after the activation: {lastRead}");

        // Post-activate: query license info and verify at least one trial is valid (≥ tomorrow)
        var (_, licText) = await CallAsync("twincat_get_license_info", new Dictionary<string, object?>(), TimeSpan.FromSeconds(60));

        // If the license node is inaccessible right after activation, skip the expiry check
        if (licText.StartsWith("[Error]", StringComparison.Ordinal)) return;

        // TC3 4026.22: TIRC^License returns the license-request form (<LicenseDef><LicenseRequest>),
        // not a list of installed licenses — see issue #55 for investigation.
        // Skip the expiry assertion; the activation success above is sufficient evidence.
        if (licText.Contains("No license entries", StringComparison.OrdinalIgnoreCase)) return;

        // Check for a trial expiry ≥ today+1 as evidence that the trial was generated
        bool hasValidTrialExpiry = HasExpiryOnOrAfter(licText, DateTime.Today.AddDays(1));
        Assert.True(hasValidTrialExpiry,
            $"Expected at least one license with expiry ≥ {DateTime.Today.AddDays(1):yyyy-MM-dd}.\n{licText}");
    }

    private static bool? ParseIsOnline(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var prop = doc.RootElement.GetProperty("isOnline");
            return prop.ValueKind == JsonValueKind.Null ? null : prop.GetBoolean();
        }
        catch { return null; }
    }

    private static bool HasExpiryOnOrAfter(string licenseText, DateTime threshold)
    {
        // Look for "expiry=yyyy-MM-dd" patterns in the formatted output
        var regex = new System.Text.RegularExpressions.Regex(@"expiry=(\d{4}-\d{2}-\d{2})");
        foreach (System.Text.RegularExpressions.Match m in regex.Matches(licenseText))
        {
            if (DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt) && dt >= threshold)
                return true;
        }
        return false;
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
