using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>
/// MCP protocol tests for the ibn-live feature group against the live MCP server
/// (http://localhost:5010). XAE is NOT required — these exercise tool
/// registration, the gate-relevant NEGATIVE invariant (no confirm/execute tool),
/// and the marshalling of the read/propose/stop surface. Skipped automatically
/// when the server is not reachable. Start ptc-xae-mcp.exe before running.
/// </summary>
[Collection("McpServer")]
public class IbnLiveMcpTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
    private McpClient? _client;
    private bool _serverReachable;

    public async Task InitializeAsync()
    {
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(ServerUrl),
                ConnectionTimeout = TimeSpan.FromSeconds(5)
            });
            _client = await McpClient.CreateAsync(transport,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            _serverReachable = true;
        }
        catch
        {
            _serverReachable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private void SkipIfNotReachable()
    {
        if (!_serverReachable)
            throw new Xunit.SkipException("MCP server not reachable on " + ServerUrl);
    }

    private async Task<string> CallAsync(string tool, Dictionary<string, object?>? args = null)
    {
        var result = await _client!.CallToolAsync(tool, args,
            cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    // ── tool registration ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task IbnLiveTools_AreAllRegistered()
    {
        SkipIfNotReachable();

        var names = (await _client!.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        Assert.Contains("twincat_read_ads_variables_batch", names);
        // twincat_ibn_arm is not here on purpose: production arming happens only in the host
        // window (issue #272) — see Arm_IsOnlyTestSupport_OnTheDiagnosticsSurface.
        Assert.Contains("twincat_ibn_disarm", names);
        Assert.Contains("twincat_ibn_propose", names);
        Assert.Contains("twincat_ibn_get_proposal_status", names);
        Assert.Contains("twincat_list_active_chains", names);
        Assert.Contains("twincat_ibn_abort", names);
        Assert.Contains("twincat_ibn_release_all", names);
        Assert.Contains("twincat_measure_signal_transition", names);
    }

    // ── gate-relevant NEGATIVE invariant ─────────────────────────────────────────

    [SkippableFact]
    public async Task Ibn_NoConfirmOrExecuteToolExists()
    {
        SkipIfNotReachable();

        var names = (await _client!.ListToolsAsync()).Select(t => t.Name).ToList();

        // The asymmetric surface: the AI may propose, but there is deliberately NO
        // AI-reachable confirm/execute path. Guard against accidental later exposure.
        var forbidden = names.Where(n =>
            n.Contains("ibn", StringComparison.OrdinalIgnoreCase) &&
            (n.Contains("confirm", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("execute", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("trigger", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("step_now", StringComparison.OrdinalIgnoreCase) ||
             n.Contains("force", StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.True(forbidden.Count == 0,
            $"Forbidden AI-reachable execute surface found: {string.Join(", ", forbidden)}");

        // Explicit belt-and-braces: none of these names may ever appear.
        foreach (var banned in new[]
                 {
                     "twincat_ibn_confirm", "twincat_ibn_confirm_proposal",
                     "twincat_ibn_execute", "twincat_ibn_step", "twincat_ibn_trigger",
                 })
            Assert.DoesNotContain(banned, names);
    }

    // ── arming is a human action in the host window (issue #272) ──────────────────

    [SkippableFact]
    public async Task Arm_IsOnlyTestSupport_OnTheDiagnosticsSurface()
    {
        SkipIfNotReachable();

        var names = (await _client!.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        // ptc_test_marker is registered only with --diagnostics; twincat_ibn_arm travels with it.
        if (names.Contains("ptc_test_marker"))
        {
            Assert.Contains("twincat_ibn_arm", names);
            return;
        }

        // A released host: the AI cannot arm. Calling the tool anyway is refused by the server
        // (unknown tool) — either as a protocol error or as an error result, never as "armed".
        Assert.DoesNotContain("twincat_ibn_arm", names);
        CallToolResult? result = null;
        Exception? error = await Record.ExceptionAsync(async () =>
            result = await _client!.CallToolAsync("twincat_ibn_arm",
                new Dictionary<string, object?> { ["ttlSeconds"] = 30 },
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token));
        Assert.True(error is not null || result?.IsError == true,
            "twincat_ibn_arm must be refused on a production host");
        Assert.False(await IsArmedAsync(), "a refused arm call must leave the host disarmed");
    }

    /// <summary>Armed state as the AI can observe it: propose is refused with "Not armed".</summary>
    private async Task<bool> IsArmedAsync()
    {
        var text = await CallAsync("twincat_ibn_propose",
            new Dictionary<string, object?> { ["action"] = "LAUNCH" }); // invalid action: never creates a proposal
        return !text.Contains("Not armed", StringComparison.OrdinalIgnoreCase);
    }

    // ── propose is gated by an arm token ──────────────────────────────────────────

    [SkippableFact]
    public async Task Propose_WithoutArm_ReturnsDefinedError()
    {
        SkipIfNotReachable();

        // Ensure a disarmed state first (release_all disarms regardless of ADS).
        await CallAsync("twincat_ibn_release_all");

        var text = await CallAsync("twincat_ibn_propose",
            new Dictionary<string, object?> { ["action"] = "next_step", ["chainId"] = "chainA" });

        Assert.StartsWith("[Error]", text);
        Assert.Contains("armed", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Propose_MalformedRequest_IsRefused()
    {
        SkipIfNotReachable();

        // Arm so we (usually) get past the arm gate and reach action validation.
        // The server's single-op dispatcher is shared across all test collections,
        // so under parallel load the arm may itself be refused — in which case the
        // propose is refused as "not armed". Either way the safety-relevant property
        // holds: a malformed propose is REFUSED (the exact "unknown action" message
        // is asserted deterministically in IbnLiveCoordinatorTests).
        // twincat_ibn_arm exists only on a --diagnostics host (issue #272); on a released host
        // the propose is refused as "not armed", which keeps the property under test.
        var names = (await _client!.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        if (names.Contains("twincat_ibn_arm"))
            await CallAsync("twincat_ibn_arm", new Dictionary<string, object?> { ["ttlSeconds"] = 30 });
        try
        {
            var text = await CallAsync("twincat_ibn_propose",
                new Dictionary<string, object?> { ["action"] = "LAUNCH" });
            Assert.StartsWith("[Error]", text);
        }
        finally
        {
            await CallAsync("twincat_ibn_release_all"); // clean up host state
        }
    }

    // ── get_proposal_status is read-only and defined for unknown ids ──────────────

    [SkippableFact]
    public async Task GetProposalStatus_UnknownId_ReturnsDefinedError()
    {
        SkipIfNotReachable();

        var text = await CallAsync("twincat_ibn_get_proposal_status",
            new Dictionary<string, object?> { ["proposalId"] = "does-not-exist" });

        Assert.StartsWith("[Error]", text);
    }

    // ── batch read input validation (no XAE needed) ───────────────────────────────

    [SkippableFact]
    public async Task ReadBatch_EmptyList_ReturnsDefinedError()
    {
        SkipIfNotReachable();

        var text = await CallAsync("twincat_read_ads_variables_batch",
            new Dictionary<string, object?> { ["symbolPaths"] = Array.Empty<string>() });

        Assert.StartsWith("[Error]", text);
    }
}
