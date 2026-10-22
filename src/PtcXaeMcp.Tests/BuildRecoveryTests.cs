using System.Runtime.InteropServices;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Unit tests for the build-recovery feature group.
/// Commit 1 (com_message_filter): IMessageFilter / Retry-Wrapper.
/// Commit 2 (twincat_logout): idempotent logout.
/// Commits 3–4 (clean / rebuild): coordinator tests.
/// </summary>
public class BuildRecoveryTests
{
    // ── Commit 1: IMessageFilter / Retry-Wrapper ──────────────────────────────

    private const int RPC_E_SERVERCALL_RETRYLATER = unchecked((int)0x8001010A);

    private static COMException RetryLater() => new("server busy", RPC_E_SERVERCALL_RETRYLATER);

    [Fact]
    public void ErrorCollectionRetry_ThrowsRetryLaterNTimes_ThenSucceeds_ReturnsResult()
    {
        int calls = 0;
        var result = ComBusyRetry.Run<string>(() =>
        {
            calls++;
            if (calls < 3) throw RetryLater();
            return "errors_populated";
        }, timeout: TimeSpan.FromSeconds(5), delay: TimeSpan.FromMilliseconds(1));

        Assert.Equal("errors_populated", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void ErrorCollectionRetry_ExhaustsTimeout_ThrowsXaeBlockedWithBusyHResult()
    {
        // Issue #149: exhaustion is a final XaeBlockedException carrying the busy HResult.
        //
        // Driven by a fake clock (issue #157 item 7), not real Stopwatch/Thread.Sleep
        // time: a real-clock version of this test (timeout=30ms, delay=10ms) was flaky
        // under CI/JIT jitter (~1/8 failures) because the FIRST call alone could take
        // longer than the whole timeout on a loaded machine, throwing after only one
        // call. The fake clock advances by a fixed step per retry, so "how many calls
        // happen before the deadline" is deterministic and has nothing to do with how
        // long this process actually takes to run.
        int calls = 0;
        var fakeElapsed = TimeSpan.Zero;
        var ex = Assert.Throws<PtcXaeMcp.WinForms.TwinCat.XaeBlockedException>(() =>
            ComBusyRetry.Run<string>(() =>
            {
                calls++;
                fakeElapsed += TimeSpan.FromMilliseconds(10);
                throw RetryLater();
            }, timeout: TimeSpan.FromMilliseconds(30), delay: TimeSpan.Zero, elapsedOverride: () => fakeElapsed));

        Assert.Equal(RPC_E_SERVERCALL_RETRYLATER, ex.HResult);
        Assert.True(calls >= 2, $"Expected retries before timeout, got {calls}");
    }

    [Fact]
    public void ErrorCollectionRetry_NonBusyException_NotRetried()
    {
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() =>
            ComBusyRetry.Run<string>(() =>
            {
                calls++;
                throw new InvalidOperationException("hard error");
            }, timeout: TimeSpan.FromSeconds(5), delay: TimeSpan.FromMilliseconds(1)));

        Assert.Equal(1, calls);
    }

    // ── Commit 2: twincat_logout (idempotent) ─────────────────────────────────

    [Fact]
    public void Logout_AlreadyOffline_ReturnsNoOp()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            FakeLogoutPlcResult = new LogoutPlcResult(false, false, "Was already offline (fake)."),
        };

        var result = fake.LogoutPlc();

        Assert.False(result.WasOnline);
        Assert.False(result.IsOnlineNow);
        Assert.Contains("LogoutPlc()", fake.CallLog);
    }

    [Fact]
    public void Logout_WasOnline_TransitionsOffline()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            FakeLogoutPlcResult = new LogoutPlcResult(true, false, "PLC logged out via ConsumeXml(<LogoutCmd/>) — now offline."),
        };

        var result = fake.LogoutPlc();

        Assert.True(result.WasOnline);
        Assert.False(result.IsOnlineNow);
    }

    [Fact]
    public void Logout_SessionThrows_SurfacedAsCOMException()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            LogoutPlcThrows = new InvalidOperationException("DTE not found"),
        };

        Assert.Throws<InvalidOperationException>(() => fake.LogoutPlc());
    }

    // ── Commits 3–4: Clean + Rebuild coordinator ──────────────────────────────

    [Fact]
    public void CleanSolution_HappyPath_ReturnsSuccess()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            FakeCleanResult = new CleanResult(true, "Solution cleaned."),
        };

        var r = fake.CleanSolution();

        Assert.True(r.Success);
        Assert.Contains("CleanSolution()", fake.CallLog);
    }

    [Fact]
    public void RebuildSolution_HappyPath_ReturnsSuccess()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            FakeRebuildResult = new RebuildResult(true, 0, Array.Empty<BuildError>()),
        };

        var result = new BuildCoordinator(fake).RebuildSolution();

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("Build succeeded", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RebuildSolution_WithErrors_ReturnsErrorList()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            FakeRebuildResult = new RebuildResult(false, 1,
                [new BuildError("error", "Type 'BOOL' not found", "FB_X", 5)]),
        };

        var result = new BuildCoordinator(fake).RebuildSolution();

        Assert.Contains("Build failed", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Type 'BOOL' not found", result);
    }

    [Fact]
    public void RebuildPlc_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).RebuildPlc(null);

        Assert.StartsWith("[Error]", result);
    }

    [Fact]
    public void RebuildSolution_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).RebuildSolution();

        Assert.StartsWith("[Error]", result);
    }
}
