namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Class fixture for <see cref="PouMembersIntegrationTests"/> that sweeps leftover
/// <c>FB_PouSpec_*</c> / <c>PRG_PouSpec_*</c> POUs from the fixture project once
/// <b>before</b> the first scenario (clearing leftovers from a previous aborted/crashed
/// run, which would otherwise collide and breed <c>_1</c> duplicates) and once
/// <b>after</b> the last scenario (clearing anything per-test teardown missed). Each
/// sweep persists with a <c>Save All</c>. Issue #94.
///
/// <para>The sweep runs only when the live MCP server is reachable <i>and</i> the
/// neutral fixture is the open project (<see cref="LiveXae.TryConnectFixtureClientAsync"/>);
/// a plain offline <c>dotnet test</c> resolves no fixture and is a strict no-op, so it
/// never opens or mutates an unrelated project.</para>
/// </summary>
public sealed class PouSpecCleanupFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        // Idempotent — brings the live env up in live-suite mode so the fixture is
        // open before the pre-suite sweep; a no-op otherwise.
        await LiveXae.EnsureLiveAsync();
        await SweepAsync("before suite");
    }

    public Task DisposeAsync() => SweepAsync("after suite");

    private static async Task SweepAsync(string phase)
    {
        var client = await LiveXae.TryConnectFixtureClientAsync();
        if (client is null) return;

        await using (client)
        {
            var result = await PouSpecLeftovers.SweepAsync(client);
            if (result.Removed.Count > 0)
                Console.WriteLine(
                    $"  🧹 PouSpec sweep ({phase}): removed {result.Removed.Count} leftover POU(s) — "
                    + string.Join(", ", result.Removed));
            if (result.Failed.Count > 0)
                Console.WriteLine(
                    $"  ⚠ PouSpec sweep ({phase}): {result.Failed.Count} node(s) would not delete — "
                    + string.Join(" | ", result.Failed));
        }
    }
}
