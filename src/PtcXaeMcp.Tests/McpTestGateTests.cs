using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for <see cref="McpTestGate"/> — the cooperative
/// step/pause/stop gate the Trace wrapper awaits before each tool call.
/// No SDK, no XAE, no GUI.
/// </summary>
public class McpTestGateTests
{
    [Fact]
    public async Task RunMode_IsDefault_AndDoesNotBlock()
    {
        var gate = new McpTestGate();

        Assert.Equal(McpTestGateMode.Run, gate.Mode);
        // Completes effectively immediately.
        await gate.WaitAsync("twincat_get_status", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SingleStep_BlocksUntilContinue_ReleasesExactlyOne()
    {
        var gate = new McpTestGate();
        gate.Mode = McpTestGateMode.SingleStep;

        var first = gate.WaitAsync("a", CancellationToken.None);
        var second = gate.WaitAsync("b", CancellationToken.None);

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        gate.Continue();

        // Exactly one of the two is released by a single Continue().
        var releasedIndex = await Task.WhenAny(first, second)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(releasedIndex == first || releasedIndex == second);
        Assert.True(first.IsCompleted ^ second.IsCompleted);
    }

    [Fact]
    public async Task SingleStep_PendingToolName_ReflectsWhatIsWaiting()
    {
        var gate = new McpTestGate { Mode = McpTestGateMode.SingleStep };

        var pending = gate.WaitAsync("twincat_open_project", CancellationToken.None);

        // Give the gate a moment to register the waiter.
        await WaitFor(() => gate.PendingToolName == "twincat_open_project");
        Assert.Equal("twincat_open_project", gate.PendingToolName);

        gate.Continue();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(gate.PendingToolName);
    }

    [Fact]
    public async Task Stop_CancelsCurrentAndSubsequentWaits()
    {
        var gate = new McpTestGate { Mode = McpTestGateMode.SingleStep };
        var waiting = gate.WaitAsync("a", CancellationToken.None);

        gate.Stop();

        // The in-flight parked call is cancelled…
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
        // …but Stop DISARMS the gate: the very next call must flow normally.
        // (Regression: a latching Stop bricked every subsequent MCP call and
        // silently cancelled a whole live-suite run.)
        Assert.Equal(McpTestGateMode.Run, gate.Mode);
        await gate.WaitAsync("b", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task FreshGate_NeverCancels_OrdinaryTraffic()
    {
        // The state a freshly-constructed gate (and every server start) is in.
        var gate = new McpTestGate();

        for (int i = 0; i < 50; i++)
            await gate.WaitAsync($"twincat_tool_{i}", CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SwitchingBackToRun_ReleasesAllWaiters()
    {
        var gate = new McpTestGate { Mode = McpTestGateMode.SingleStep };
        var a = gate.WaitAsync("a", CancellationToken.None);
        var b = gate.WaitAsync("b", CancellationToken.None);

        gate.Mode = McpTestGateMode.Run;

        await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ResetForNewRun_DiscardsStragglers_AndIsPassThrough()
    {
        var gate = new McpTestGate { Mode = McpTestGateMode.SingleStep };
        var straggler = gate.WaitAsync("a", CancellationToken.None);

        gate.ResetForNewRun();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => straggler.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(McpTestGateMode.Run, gate.Mode);
        await gate.WaitAsync("b", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CallerCancellation_PropagatesOutOfWait()
    {
        var gate = new McpTestGate { Mode = McpTestGateMode.SingleStep };
        using var cts = new CancellationTokenSource();

        var waiting = gate.WaitAsync("a", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ModeChange_RaisesChanged()
    {
        var gate = new McpTestGate();
        int fires = 0;
        gate.Changed += () => Interlocked.Increment(ref fires);

        gate.Mode = McpTestGateMode.SingleStep;
        gate.Mode = McpTestGateMode.SingleStep; // no-op, same value
        gate.Mode = McpTestGateMode.Run;

        Assert.Equal(2, fires);
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (int i = 0; i < 200 && !cond(); i++)
            await Task.Delay(10);
    }
}
