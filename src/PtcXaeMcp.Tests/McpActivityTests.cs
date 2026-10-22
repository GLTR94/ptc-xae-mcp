using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for the <see cref="McpActivity"/> coordinator — the
/// central Trace wrapper every tool body passes through. No SDK/XAE/GUI.
/// </summary>
public sealed class McpActivityTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "mcpact_" + Guid.NewGuid().ToString("N"));

    public McpActivityTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private McpActivity NewActivity() =>
        new(new McpActivityLog(capacity: 50), new McpActivityJsonLog(_dir));

    [Fact]
    public void Trace_ReturnsBodyResultUnchanged_AndRecordsOneOkEntry()
    {
        var act = NewActivity();

        var result = act.Trace("twincat_get_status", null, () => "XAE: Active");

        Assert.Equal("XAE: Active", result);
        var e = Assert.Single(act.Log.Snapshot());
        Assert.Equal("twincat_get_status", e.ToolName);
        Assert.True(e.Success);
        Assert.Equal("XAE: Active", e.Response);
    }

    [Fact]
    public void Trace_BodyReturningErrorString_RecordsFailureEntry_StillReturnsIt()
    {
        var act = NewActivity();

        var result = act.Trace("twincat_open_project",
            new { slnPath = "x" }, () => "[Error] not found");

        Assert.Equal("[Error] not found", result);
        var e = Assert.Single(act.Log.Snapshot());
        Assert.False(e.Success);
    }

    [Fact]
    public async Task TraceAsync_ThrowingBody_RecordsFailure_ReturnsErrorString()
    {
        var act = NewActivity();

        var result = await act.TraceAsync("twincat_build_plc", null,
            () => throw new InvalidOperationException("boom"));

        Assert.StartsWith("[Error]", result, StringComparison.Ordinal);
        Assert.Contains("boom", result, StringComparison.Ordinal);
        var e = Assert.Single(act.Log.Snapshot());
        Assert.False(e.Success);
        Assert.Equal("[Error] boom", e.Response);
    }

    [Fact]
    public void Trace_FirstCall_AutoStartsRun_WritesJsonFile()
    {
        var act = NewActivity();

        act.Trace("twincat_get_status", null, () => "ok");

        Assert.NotNull(act.JsonLog.CurrentPath);
        Assert.True(File.Exists(act.JsonLog.CurrentPath!));
        var lines = File.ReadAllLines(act.JsonLog.CurrentPath!);
        Assert.Single(lines);
        Assert.Contains("twincat_get_status", lines[0]);
    }

    [Fact]
    public async Task Trace_RespectsGate_SingleStepBlocksUntilContinue()
    {
        var act = NewActivity();
        act.Gate.Mode = McpTestGateMode.SingleStep;

        var call = act.TraceAsync("twincat_get_status", null,
            () => Task.FromResult("ok"));
        Assert.False(call.IsCompleted);

        act.Gate.Continue();

        Assert.Equal("ok", await call.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(Assert.Single(act.Log.Snapshot()).Success);
    }

    [Fact]
    public async Task Trace_GateStop_ProducesCancelledEntry_DoesNotRunBody()
    {
        var act = NewActivity();
        act.Gate.Mode = McpTestGateMode.SingleStep;
        bool bodyRan = false;

        var call = act.TraceAsync("twincat_build_solution", null,
            () => { bodyRan = true; return Task.FromResult("built"); });
        act.Gate.Stop();

        var result = await call.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.StartsWith("[Error] Cancelled", result, StringComparison.Ordinal);
        Assert.False(bodyRan);
        Assert.False(Assert.Single(act.Log.Snapshot()).Success);
    }

    [Fact]
    public async Task Trace_AfterGateStop_NextCallRunsNormally_NotBricked()
    {
        // Regression for the live-suite failure: a Stop (or any stuck gate
        // state) must NOT cause every subsequent real MCP call to come back
        // "[Cancelled]". After Stop the gate disarms; the next call flows.
        var act = NewActivity();
        act.Gate.Mode = McpTestGateMode.SingleStep;

        var parked = act.TraceAsync("twincat_open_project", null,
            () => Task.FromResult("Opened"));
        act.Gate.Stop();
        Assert.StartsWith("[Error] Cancelled",
            await parked.WaitAsync(TimeSpan.FromSeconds(2)), StringComparison.Ordinal);

        // The very next tool call — exactly what the live suite does after the
        // stale gate cancelled open_project — must succeed.
        var next = await act.TraceAsync("twincat_get_status", null,
            () => Task.FromResult("XAE: Active"))
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("XAE: Active", next);
    }

    [Fact]
    public async Task Trace_BypassGate_IgnoresSingleStep_ForControlCommands()
    {
        var act = NewActivity();
        act.Gate.Mode = McpTestGateMode.SingleStep;

        // shutdown/stop pass bypassGate:true — must not block on the gate.
        var result = await act.TraceAsync("twincat_shutdown", null,
            () => Task.FromResult("Shutting down..."), bypassGate: true)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Shutting down...", result);
    }

    [Fact]
    public void StartNewRun_RollsToANewJsonFile()
    {
        var act = NewActivity();
        act.Trace("a", null, () => "1");
        var first = act.JsonLog.CurrentPath;

        act.StartNewRun();
        act.Trace("b", null, () => "2");
        var second = act.JsonLog.CurrentPath;

        Assert.NotEqual(first, second);
        Assert.Empty(File.ReadLines(second!).Skip(1)); // only the 'b' line
    }

    [Fact]
    public void Trace_WhenLambdaThrows_ReturnsErrorString()
    {
        var act = NewActivity();

        var result = act.Trace("twincat_build_plc", null,
            () => throw new InvalidOperationException("boom"));

        Assert.StartsWith("[Error]", result, StringComparison.Ordinal);
        Assert.Contains("boom", result);
    }

    [Fact]
    public void Trace_WhenLambdaThrows_StillRecordsEntry()
    {
        var act = NewActivity();

        act.Trace("twincat_build_plc", null,
            () => throw new InvalidOperationException("boom"));

        var e = Assert.Single(act.Log.Snapshot());
        Assert.False(e.Success);
        Assert.True(e.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task Trace_SyncOverload_CallerCancellation_ReleasesCallParkedAtGate()
    {
        // Issue #151 item 1: every synchronous [McpServerTool] method now
        // passes the MCP SDK-bound CancellationToken into this sync overload
        // of Trace (added in #169). A client disconnect delivers cancellation
        // on that token, and a call parked at the gate (SingleStep, or any
        // other operation ahead of it) must be released instead of hanging
        // forever — never mind the actual COM call in flight, which this
        // contract deliberately does not try to cancel.
        var act = NewActivity();
        act.Gate.Mode = McpTestGateMode.SingleStep;
        using var cts = new CancellationTokenSource();
        bool bodyRan = false;

        // The sync Trace(...) overload blocks the calling thread on
        // TraceAsync(...).GetAwaiter().GetResult(), so it must run on its own
        // thread for the test to be able to cancel it from outside.
        var call = Task.Run(() => act.Trace("twincat_get_status", null,
            () => { bodyRan = true; return "XAE: Active"; }, cts.Token));

        await WaitFor(() => act.Gate.PendingToolName == "twincat_get_status");
        cts.Cancel();

        var result = await call.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.StartsWith("[Error] Cancelled", result, StringComparison.Ordinal);
        Assert.False(bodyRan);
        Assert.False(Assert.Single(act.Log.Snapshot()).Success);
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (int i = 0; i < 200 && !cond(); i++)
            await Task.Delay(10);
    }
}
