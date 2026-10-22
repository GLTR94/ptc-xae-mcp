using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>
/// issue #154, E-1: twincat_stop must be able to cancel an MCP-started
/// apply_schema run, not just the GUI Run/Stop button's own token.
/// <see cref="McpRunCancellation"/> is the COM-free piece of that wiring —
/// this proves the actual mechanism twincat_apply_schema/twincat_stop use:
/// a tracked run's token is cancelled, the awaiting Task observes
/// OperationCanceledException, and the tool layer (TwinCatMcpTools.Schema.cs)
/// turns that into McpErrors.CancelledMessage ("[Error] Cancelled").
/// </summary>
public class McpRunCancellationTests
{
    [Fact]
    public void CancelCurrent_NoRunTracked_ReturnsFalse()
    {
        var registry = new McpRunCancellation();
        Assert.False(registry.CancelCurrent());
        Assert.False(registry.HasCurrent);
    }

    [Fact]
    public void CancelCurrent_TrackedRun_CancelsItsToken()
    {
        var registry = new McpRunCancellation();
        using var cts = new CancellationTokenSource();
        using var _ = registry.Track(cts);

        Assert.True(registry.HasCurrent);
        bool cancelled = registry.CancelCurrent();

        Assert.True(cancelled);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Untrack_OnDispose_CancelCurrentNoLongerAffectsIt()
    {
        var registry = new McpRunCancellation();
        var cts = new CancellationTokenSource();
        var scope = registry.Track(cts);
        scope.Dispose(); // run finished normally, like RunApplySchemaAsync's `using`

        bool cancelled = registry.CancelCurrent();

        Assert.False(cancelled);
        Assert.False(cts.IsCancellationRequested);
        cts.Dispose();
    }

    [Fact]
    public async Task CancelCurrent_WhileAwaitingLinkedToken_ObservedAsOperationCanceled()
    {
        // Mirrors RunApplySchemaAsync exactly: a linked CTS is tracked for the
        // duration of the run; twincat_stop calls CancelCurrent() from another
        // "thread" (here: concurrently via Task.Run) while the run awaits.
        var registry = new McpRunCancellation();
        using var mcpToken = new CancellationTokenSource(); // the per-call request token (never itself cancelled here)
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(mcpToken.Token);
        using var track = registry.Track(linked);

        var runTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), linked.Token); // the "long apply_schema run"
        });

        // Give the run a moment to actually start awaiting, then simulate
        // twincat_stop.
        await Task.Delay(20);
        bool stopped = registry.CancelCurrent();

        var ex = await Record.ExceptionAsync(() => runTask);

        Assert.True(stopped);
        Assert.IsType<TaskCanceledException>(ex); // OperationCanceledException subtype
        // This is exactly what TwinCatMcpTools.Schema.cs's ApplySchema catches
        // (catch (OperationCanceledException)) and turns into McpErrors.Cancelled(...).
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    [Fact]
    public void SecondTrack_SupersedesFirst_ForCancelCurrentPurpose()
    {
        var registry = new McpRunCancellation();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var t1 = registry.Track(cts1);
        using var t2 = registry.Track(cts2);

        registry.CancelCurrent();

        Assert.False(cts1.IsCancellationRequested);
        Assert.True(cts2.IsCancellationRequested);
    }
}
