using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Stage XAR — ibn-live against a live PLC runtime. Preconditions as for
/// <see cref="AdsVariableIntegrationTests"/>: the neutral fixture activated on the stage's explicit runtime
/// target and in Run, MCP server on port 5010. Every call that takes one passes the target as
/// <c>amsNetId</c> (issue #195); twincat_ibn_release_all has none and acts on the armed target.
///
/// Scope note (by design): the human-confirm step (IbnLiveCoordinator.ConfirmProposal)
/// is host-only and has NO MCP/automation path — that asymmetry is the whole point
/// of the safety architecture, so the popup-gated motion CANNOT be exercised
/// headlessly. These tests cover the AI-REACHABLE live surface: batch read,
/// list_active_chains shaping, the arm (diagnostics-only test support, #272) / abort
/// control surface, and a measured
/// timeout. The full deploy→run-via-popup→teardown walk-through is a manual
/// procedure.
/// </summary>
[Collection("Stage XAR")]
[Trait("Stage", "XAR")]
public class IbnLiveIntegrationTests : IAsyncLifetime
{
    private readonly XarRuntimeFixture _xar;
    private McpClient? _client;

    public IbnLiveIntegrationTests(XarRuntimeFixture xar) => _xar = xar;

    // The tools of this class that take an amsNetId: they all get the stage's runtime.
    private static readonly HashSet<string> TakesAmsNetId = new(StringComparer.Ordinal)
    {
        "twincat_read_ads_variables_batch", "twincat_list_active_chains", "twincat_ibn_arm",
        "twincat_measure_signal_transition",
    };

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
        catch { /* _client stays null; SkipIfNotReady gates tests */ }
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private void SkipIfNotReady()
    {
        _xar.SkipIfNotReady();
        if (_client is null) throw new SkipException("ibn-live: MCP client could not be created for Stage XAR.");
    }

    private async Task<string> CallAsync(string tool, Dictionary<string, object?>? args = null)
    {
        args ??= new();
        if (TakesAmsNetId.Contains(tool)) args["amsNetId"] = _xar.AmsNetId;   // issue #195
        var result = await _client!.CallToolAsync(tool, args,
            cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    // ── batch read against live symbols ───────────────────────────────────────

    [Trait("Description", "Batch-Read mehrerer Live-Symbole in einem Aufruf → success, results[] mit per-Symbol ok.")]
    [SkippableFact]
    public async Task ReadBatch_LiveSymbols_ReturnsResultsArray()
    {
        SkipIfNotReady();
        var text = await CallAsync("twincat_read_ads_variables_batch",
            new Dictionary<string, object?>
            {
                ["symbolPaths"] = new[] { FixtureLayout.BoolSymbol, FixtureLayout.UintSymbol },
            });

        Assert.False(string.IsNullOrWhiteSpace(text));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), $"Expected success=true but got: {text}");
        Assert.Equal(2, root.GetProperty("count").GetInt32());
        var results = root.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        // Both are known-good read-only symbols → ok=true.
        Assert.All(results.EnumerateArray(), i => Assert.True(i.GetProperty("ok").GetBoolean(),
            $"symbol {i.GetProperty("symbol").GetString()} not ok: {text}"));
    }

    [Trait("Description", "Batch-Read mit einem unbekannten Symbol → Verbindung ok (success), nur dieses Item ok=false.")]
    [SkippableFact]
    public async Task ReadBatch_OneBadSymbol_IsolatedNotWholeError()
    {
        SkipIfNotReady();
        var text = await CallAsync("twincat_read_ads_variables_batch",
            new Dictionary<string, object?>
            {
                ["symbolPaths"] = new[] { FixtureLayout.BoolSymbol, "NOPE.bDoesNotExist" },
            });

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), $"connection should succeed: {text}");
        var items = root.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("ok").GetBoolean());        // the good one
        Assert.Contains(items, i => !i.GetProperty("ok").GetBoolean());       // the bad one isolated
    }

    // ── list_active_chains with no test PRG deployed ───────────────────────────

    [Trait("Description", "list_active_chains ohne deployedes Test-PRG → success (Verbindung ok), Chain-Werte null/abwesend.")]
    [SkippableFact]
    public async Task ListActiveChains_NoTestPrg_ReturnsSuccessWithEmptyChain()
    {
        SkipIfNotReady();
        var text = await CallAsync("twincat_list_active_chains",
            new Dictionary<string, object?> { ["gvlPath"] = "GVL_IBN_DoesNotExist" });

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        // Connection succeeds; the (absent) status symbols come back ok=false, so
        // the derived chain fields are null. Never a whole-call error.
        Assert.True(root.GetProperty("success").GetBoolean(), $"Expected success=true but got: {text}");
        var chain = root.GetProperty("chain");
        Assert.Equal(JsonValueKind.Null, chain.GetProperty("running").ValueKind);
        Assert.All(root.GetProperty("raw").EnumerateArray(),
            i => Assert.False(i.GetProperty("ok").GetBoolean()));
    }

    // ── arm / abort surface on the fixture's IbnLive probe (no motion) ──────────
    // Arm refuses unless its first bEnable write succeeds (#154), so it needs the probe GVL
    // (FixtureLayout.IbnProbeGvlName, issue #200). The fixture has no test PRG: nothing moves.
    // twincat_ibn_arm is test support on the diagnostics surface (issue #272: production IBN
    // Live is armed only in the host window), so this needs the host started with --diagnostics
    // — which LiveXae's bootstrap does.

    [Trait("Description", "arm → armed JSON mit Token/Ablauf; danach release_all räumt auf (kein Test-PRG nötig).")]
    [SkippableFact]
    public async Task Arm_ReturnsArmedToken_ThenReleaseAllCleansUp()
    {
        SkipIfNotReady();
        var tools = (await _client!.ListToolsAsync()).Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(tools.Contains("twincat_ibn_arm"),
            "twincat_ibn_arm is not registered: the host must run with --diagnostics " +
            "(it is test support only since issue #272; production arming happens in the host window).");
        bool released = false;
        try
        {
            var armText = await CallAsync("twincat_ibn_arm",
                new Dictionary<string, object?> { ["ttlSeconds"] = 5, ["gvlPath"] = FixtureLayout.IbnProbeGvlName });

            Assert.False(armText.StartsWith("[Error]", StringComparison.Ordinal), $"Arm refused: {armText}");
            using (var doc = JsonDocument.Parse(armText))
            {
                var root = doc.RootElement;
                Assert.True(root.GetProperty("armed").GetBoolean(), $"Expected armed=true but got: {armText}");
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("armToken").GetString()));
                Assert.Equal(5, root.GetProperty("ttlSeconds").GetInt32());
            }

            // release_all disarms and writes the probe's abort bit, which exists now.
            var releaseText = await CallAsync("twincat_ibn_release_all");
            released = true;
            Assert.False(releaseText.StartsWith("[Error]", StringComparison.Ordinal), $"release_all failed: {releaseText}");
            using var release = JsonDocument.Parse(releaseText);
            Assert.False(release.RootElement.GetProperty("armed").GetBoolean(), $"Expected armed=false but got: {releaseText}");
            Assert.Equal(FixtureLayout.IbnProbeAbortSymbol, release.RootElement.GetProperty("abortSymbol").GetString());
        }
        finally
        {
            // Never leave the heartbeat running after a failed assertion.
            if (!released) await CallAsync("twincat_ibn_release_all");
        }
    }

    // ── measure_signal_transition timeout ───────────────────────────────────────

    [Trait("Description", "measure_signal_transition mit unmöglicher Startbedingung + kurzem Timeout → timedOut=true.")]
    [SkippableFact]
    public async Task Measure_ImpossibleStart_ShortTimeout_TimesOut()
    {
        SkipIfNotReady();
        var text = await CallAsync("twincat_measure_signal_transition",
            new Dictionary<string, object?>
            {
                ["startSymbol"] = FixtureLayout.UintSymbol,
                ["startEquals"] = "9999",   // never true
                ["endSymbol"] = FixtureLayout.BoolSymbol,
                ["endEquals"] = "true",
                ["timeoutMs"] = 300,
            });

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean(), $"Expected success=true but got: {text}");
        Assert.True(root.GetProperty("timedOut").GetBoolean());
    }
}
