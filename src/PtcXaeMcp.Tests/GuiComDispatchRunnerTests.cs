using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.Mcp;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Unit-testable seam behind every GUI handler's
/// <c>Form1.RunGuiComActionAsync</c> call (issue #154). Exercises the four
/// behaviours the issue's acceptance criteria call out, without a WinForms
/// message loop or a live XAE: busy dispatcher, <see cref="XaeBlockedException"/>,
/// any other exception, and that the lock is always released.
/// </summary>
public sealed class GuiComDispatchRunnerTests
{
    private static (McpDispatcher Dispatcher, GuiComDispatchRunner Runner) MakeRunner()
    {
        var dispatcher = new McpDispatcher(new FakeTwinCatSession());
        return (dispatcher, new GuiComDispatchRunner(dispatcher));
    }

    [Fact]
    public async Task RunAsync_ReturnsTheActionsValue_OnSuccess()
    {
        var (_, runner) = MakeRunner();

        var outcome = await runner.RunAsync(() => "hello");

        Assert.True(outcome.Ok);
        Assert.Equal("hello", outcome.Value);
        Assert.Null(outcome.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_ReturnsBusyMessage_WhenDispatcherAlreadyHeld()
    {
        var (dispatcher, runner) = MakeRunner();
        Assert.True(dispatcher.TryAcquire()); // simulate a concurrent GUI/MCP call holding the lock

        var outcome = await runner.RunAsync(() => "should not run");

        Assert.False(outcome.Ok);
        Assert.StartsWith("[Error]", outcome.ErrorMessage);
        Assert.Contains("already in progress", outcome.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_NamesTheBlockingDialog_OnXaeBlockedException()
    {
        var (_, runner) = MakeRunner();

        var outcome = await runner.RunAsync<string>(() =>
            throw new XaeBlockedException("stuck", "Security Code", inner: null));

        Assert.False(outcome.Ok);
        Assert.Contains("Security Code", outcome.ErrorMessage);
        Assert.Contains("stuck", outcome.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_ReportsTheMessage_WhenNoDialogWasFound()
    {
        var (_, runner) = MakeRunner();

        var outcome = await runner.RunAsync<string>(() =>
            throw new XaeBlockedException("stuck with no dialog", dialog: null, inner: null));

        Assert.False(outcome.Ok);
        Assert.Contains("stuck with no dialog", outcome.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_ReportsAnyOtherException_AsError()
    {
        var (_, runner) = MakeRunner();

        var outcome = await runner.RunAsync<string>(() => throw new InvalidOperationException("boom"));

        Assert.False(outcome.Ok);
        Assert.StartsWith("[Error]", outcome.ErrorMessage);
        Assert.Contains("boom", outcome.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_AlwaysReleasesTheLock_SoTheNextCallCanRun()
    {
        var (dispatcher, runner) = MakeRunner();

        _ = await runner.RunAsync<string>(() => throw new InvalidOperationException("boom"));

        // The lock must be free again — a second call runs normally instead of reporting busy.
        var outcome = await runner.RunAsync(() => "ok");
        Assert.True(outcome.Ok);
        Assert.Equal("ok", outcome.Value);

        // And TryAcquire from outside still succeeds too.
        Assert.True(dispatcher.TryAcquire());
        dispatcher.Release();
    }
}
