using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Shared helper that probes whether the TwinCAT PLC runtime is in Run mode and
/// reachable over ADS. The <em>only</em> accepted proof is a successful
/// <c>twincat_read_ads_variable</c> result. Trusting <c>activate_config</c>
/// alone is explicitly not done here (a UMR target can be "in Run" while the PLC
/// application is absent on its ADS port, causing false-green results).
///
/// <para>
/// Issue #195: every probe names its runtime. <c>amsNetId</c> is required and comes from the
/// runtime stage's explicit target (<see cref="XarRuntimeFixture.AmsNetId"/>, i.e.
/// <see cref="LiveXae.RuntimeNetId"/>), so a probe can never fall back to the local runtime.
/// </para>
/// <para>
/// Extracted from <see cref="XarRuntimeFixture"/> so the Scope stage can gate its
/// recording test on the same guarantee without duplicating the activation logic.
/// </para>
/// </summary>
internal static class RuntimeReadiness
{
    private const string ProbeSymbol = FixtureLayout.UintSymbol;

    /// <summary>
    /// Returns <see langword="true"/> when a live ADS symbol read on <paramref name="amsNetId"/> succeeds,
    /// confirming the PLC runtime is up and its ADS port is reachable.
    /// </summary>
    internal static async Task<bool> IsRuntimeInRunAsync(McpClient client, string amsNetId) =>
        (await ProbeAsync(client, amsNetId)).Up;

    /// <summary>
    /// Returns <see langword="true"/> when the MCP host was compiled with
    /// <c>TwinCat=false</c> (COM path not compiled in — runtime interop absent).
    /// </summary>
    internal static async Task<bool> IsWrongBuildAsync(McpClient client, string amsNetId)
    {
        try
        {
            var result = await client.CallToolAsync(
                "twincat_read_ads_variable",
                ProbeArgs(amsNetId),
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

            var text = GetText(result);
            return text.Contains("COM path is not compiled in", StringComparison.OrdinalIgnoreCase)
                || text.Contains("without TwinCAT", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Polls the probe until a real PLC symbol read on <paramref name="amsNetId"/> succeeds or
    /// <paramref name="timeout"/> elapses. Returns whether it succeeded and the last probe's text.
    /// </summary>
    internal static async Task<(bool Up, string LastText)> WaitForRuntimeAsync(McpClient client, string amsNetId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var probe = await ProbeAsync(client, amsNetId);
            if (probe.Up || DateTime.UtcNow >= deadline) return probe;
            await Task.Delay(2000);
        }
    }

    private static async Task<(bool Up, string LastText)> ProbeAsync(McpClient client, string amsNetId)
    {
        try
        {
            var result = await client.CallToolAsync(
                "twincat_read_ads_variable",
                ProbeArgs(amsNetId),
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

            var text = GetText(result);
            if (!text.TrimStart().StartsWith('{')) return (false, text);

            using var doc = JsonDocument.Parse(text);
            return (doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean(), text);
        }
        catch (Exception ex) { return (false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static Dictionary<string, object?> ProbeArgs(string amsNetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(amsNetId);
        return new() { ["symbolPath"] = ProbeSymbol, ["amsNetId"] = amsNetId };
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
