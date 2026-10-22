using ModelContextProtocol.Client;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Best-effort sender for the test-only <c>ptc_test_marker</c> narration tool.
/// Owns a single, lazily-connected MCP client shared by the whole assembly
/// (safe because the suite runs strictly serially — see
/// <c>AssemblyInfo.cs</c>). Used by <see cref="LiveTestTraceAttribute"/>.
/// <para>
/// A marker must NEVER affect a test run. If the host is not reachable (an
/// offline run, or one without a fixture / REQUIRE_LIVE) every send silently
/// no-ops, so the suite stays green without a live host. All failures are
/// swallowed and the one connect attempt is cached, so an offline run pays at
/// most one short timeout, not one per test.
/// </para>
/// </summary>
internal static class LiveTestMarker
{
    private const string ServerUrl = "http://localhost:5010/";
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    // One connect attempt for the whole run; null when the host is unreachable.
    private static readonly Lazy<McpClient?> _client = new(TryConnect);

    static LiveTestMarker()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!_client.IsValueCreated || _client.Value is null) return;
            try { Task.Run(async () => await _client.Value.DisposeAsync()).Wait(Budget); }
            catch { /* shutting down — best effort */ }
        };
    }

    public static void Start(string testName, string? description) =>
        Send("start", testName, description);

    public static void End(string testName) =>
        Send("end", testName, null);

    private static McpClient? TryConnect()
    {
        // Don't even probe the socket on an offline / unit-only run: the live
        // tests skip anyway, so there is nothing to narrate.
        if (LiveXae.FixtureSln() is null && !LiveXae.RequireLive)
            return null;

        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(ServerUrl),
                ConnectionTimeout = Budget,
            });
            // Task.Run escapes any captured test sync-context before blocking.
            return Task.Run(async () => await McpClient.CreateAsync(transport,
                    cancellationToken: new CancellationTokenSource(Budget).Token))
                .GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static void Send(string phase, string testName, string? description)
    {
        var client = _client.Value;
        if (client is null) return; // host unreachable → narration is a no-op

        try
        {
            var args = new Dictionary<string, object?>
            {
                ["phase"] = phase,
                ["testName"] = testName,
                ["description"] = description,
            };
            Task.Run(async () => await client.CallToolAsync("ptc_test_marker", args,
                    cancellationToken: new CancellationTokenSource(Budget).Token))
                .GetAwaiter().GetResult();
        }
        catch
        {
            // A marker must never affect a test run.
        }
    }
}
