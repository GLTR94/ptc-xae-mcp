using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

public sealed class McpDispatcherTests
{
    private static McpDispatcher New() => new(new FakeTwinCatSession());

    [Fact]
    public void Run_WhenBusy_ThrowsInvalidOperationException()
    {
        var d = New();
        var lockHeld = new ManualResetEventSlim(false);
        var releaseSignal = new ManualResetEventSlim(false);

        // LongRunning allocates a dedicated thread so blocking releaseSignal.Wait()
        // doesn't starve the thread pool when running alongside ~1000 other tests.
        var t = Task.Factory.StartNew<string>(() => d.Run(() =>
        {
            lockHeld.Set();
            releaseSignal.Wait();
            return "ok";
        }), TaskCreationOptions.LongRunning);

        Assert.True(lockHeld.Wait(TimeSpan.FromSeconds(2)), "lock was never acquired");

        var ex = Assert.Throws<InvalidOperationException>(() => d.Run(() => "second"));
        Assert.Contains("already in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        releaseSignal.Set();
        Assert.True(t.Wait(TimeSpan.FromSeconds(2)), "first task did not complete");
    }

    [Fact]
    public void Run_SequentialCalls_BothSucceed()
    {
        var d = New();
        var first = d.Run(() => "first");
        var second = d.Run(() => "second");
        Assert.Equal("first", first);
        Assert.Equal("second", second);
    }

    [Fact]
    public void Run_AfterSuccess_InvokesCallbackExactlyOnce()
    {
        var d = New();
        int callbackCount = 0;
        d.Run(() => "ok", afterSuccess: () => callbackCount++);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void Run_AfterError_DoesNotInvokeCallback()
    {
        var d = New();
        int callbackCount = 0;
        d.Run(() => "[Error] boom", afterSuccess: () => callbackCount++);
        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void Busy_NamesTheXaeDialogTheRunningCallWaitsFor()
    {
        // Issue #149: any window owned by the disabled XAE main window counts, not only #32770 boxes.
        var ex = McpDispatcher.Busy(() => "Gehe zu Zeile");
        Assert.StartsWith("An operation is already in progress. The XAE shows a dialog", ex.Message);
        Assert.Contains("'Gehe zu Zeile'", ex.Message);
    }

    [Fact]
    public void Busy_WithoutDialog_IsThePlainMessage() =>
        Assert.Equal("An operation is already in progress.", McpDispatcher.Busy(() => null).Message);

    [Fact]
    public void Busy_ProbeFailure_IsThePlainMessage() =>
        Assert.Equal("An operation is already in progress.",
            McpDispatcher.Busy(() => throw new InvalidOperationException("enum failed")).Message);

    // ── issue #154, A-12/F-30: Dispose() vs. an in-flight tool call ────────────

    [Fact]
    public void Dispose_WaitsForInFlightBody_ThenDoesNotThrowFromRelease()
    {
        var d = New();
        var lockHeld = new ManualResetEventSlim(false);
        var releaseSignal = new ManualResetEventSlim(false);

        var t = Task.Factory.StartNew<string>(() => d.Run(() =>
        {
            lockHeld.Set();
            releaseSignal.Wait(TimeSpan.FromSeconds(2));
            return "ok";
        }), TaskCreationOptions.LongRunning);
        Assert.True(lockHeld.Wait(TimeSpan.FromSeconds(2)), "lock was never acquired");

        // Dispose while the body is still running (host shutdown mid-tool-call).
        var disposeTask = Task.Run(() => d.Dispose());
        releaseSignal.Set();

        // Neither the running tool's Run() nor Dispose() must throw —
        // previously Release() inside Run()'s finally would throw
        // ObjectDisposedException once Dispose() beat it to disposing the
        // semaphore.
        var runEx = Record.Exception(() => t.Wait(TimeSpan.FromSeconds(3)));
        var disposeEx = Record.Exception(() => disposeTask.Wait(TimeSpan.FromSeconds(3)));

        Assert.Null(runEx);
        Assert.Null(disposeEx);
        Assert.Equal("ok", t.Result);
    }

    [Fact]
    public void Run_AfterDispose_ThrowsBusyNotObjectDisposed()
    {
        var d = New();
        d.Dispose();

        var ex = Record.Exception(() => d.Run(() => "should not run"));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("already in progress", ex!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryAcquireRelease_AfterDispose_NeitherThrows()
    {
        // Mirrors ServerHost.RunApplySchemaAsync's TryAcquire()/Release() pairing.
        var d = New();
        d.Dispose();

        bool acquired = false;
        var ex = Record.Exception(() =>
        {
            acquired = d.TryAcquire();
        });

        Assert.Null(ex);
        Assert.False(acquired);
    }
}
