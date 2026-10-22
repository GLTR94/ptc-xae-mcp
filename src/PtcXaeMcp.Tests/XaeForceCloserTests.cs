using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Q1 force-close decision/ordering (issue #57): refuse while a dismissable
/// dialog is up, ask politely before killing, only kill what does not exit.
/// No real process is touched.
/// </summary>
public class XaeForceCloserTests
{
    private sealed class FakeControl : IXaeProcessControl
    {
        public List<XaeProcessInfo> Procs { get; } = new();
        public HashSet<int> ExitsGracefully { get; } = new();
        public HashSet<int> HasWindow { get; } = new();
        public HashSet<int> KillFails { get; } = new();
        public List<string> Calls { get; } = new();

        public IReadOnlyList<XaeProcessInfo> List() => Procs;

        public bool TryGracefulClose(int pid)
        {
            Calls.Add($"close({pid})");
            return HasWindow.Contains(pid);
        }

        public bool WaitForExit(int pid, TimeSpan timeout)
        {
            Calls.Add($"wait({pid})");
            return ExitsGracefully.Contains(pid);
        }

        public bool Kill(int pid)
        {
            Calls.Add($"kill({pid})");
            return !KillFails.Contains(pid);
        }
    }

    private static XaeProcessInfo P(int pid) => new(pid, DateTime.UtcNow, true);

    [Fact]
    public void NoProcess_NoActionTaken()
    {
        var r = XaeForceCloser.ForceClose(new FakeControl(), false, null);
        Assert.False(r.ActionTaken);
        Assert.Contains("nothing to close", r.Message);
    }

    [Fact]
    public void DismissableDialogPresent_RefusesAndPointsAtDismissTool()
    {
        var ctl = new FakeControl();
        ctl.Procs.Add(P(100));
        var r = XaeForceCloser.ForceClose(ctl, true, "Sicherheitscode eingeben");
        Assert.False(r.ActionTaken);
        Assert.Contains("Refusing to force-close", r.Message);
        Assert.Contains("twincat_dismiss_security_dialog", r.Message);
        Assert.Contains("Sicherheitscode eingeben", r.Message);
        Assert.Empty(ctl.Calls); // never touched the process
    }

    [Fact]
    public void GracefulCloseSucceeds_NoKill()
    {
        var ctl = new FakeControl();
        ctl.Procs.Add(P(100));
        ctl.HasWindow.Add(100);
        ctl.ExitsGracefully.Add(100);

        var r = XaeForceCloser.ForceClose(ctl, false, null, TimeSpan.FromMilliseconds(1));

        Assert.True(r.ActionTaken);
        Assert.Equal(new[] { "close(100)", "wait(100)" }, ctl.Calls);
        Assert.Contains("closed gracefully", r.Message);
        Assert.Contains("unsaved XAE work is lost", r.Message);
    }

    [Fact]
    public void GracefulCloseTimesOut_ThenKills()
    {
        var ctl = new FakeControl();
        ctl.Procs.Add(P(100));
        ctl.HasWindow.Add(100); // window exists but never exits

        var r = XaeForceCloser.ForceClose(ctl, false, null, TimeSpan.FromMilliseconds(1));

        Assert.True(r.ActionTaken);
        Assert.Equal(new[] { "close(100)", "wait(100)", "kill(100)" }, ctl.Calls);
        Assert.Contains("killed", r.Message);
    }

    [Fact]
    public void NoWindow_KillsWithoutWaiting()
    {
        var ctl = new FakeControl();
        ctl.Procs.Add(P(100)); // not in HasWindow → CloseMainWindow returns false

        var r = XaeForceCloser.ForceClose(ctl, false, null);

        Assert.True(r.ActionTaken);
        Assert.Equal(new[] { "close(100)", "kill(100)" }, ctl.Calls);
        Assert.Contains("no window to close", r.Message);
    }

    [Fact]
    public void KillDoesNotConfirmExit_ReportedHonestlyNotAsKilled()
    {
        // issue #154, A-14: Kill() returning false (process still there after
        // the attempt) must be reported honestly, not as a plain "killed".
        var ctl = new FakeControl();
        ctl.Procs.Add(P(100)); // no window -> straight to kill
        ctl.KillFails.Add(100);

        var r = XaeForceCloser.ForceClose(ctl, false, null);

        Assert.True(r.ActionTaken);
        Assert.Contains("kill FAILED", r.Message);
        Assert.DoesNotContain("PID 100: no window to close — killed.", r.Message);
        Assert.Contains("did not confirm exit", r.Message);
    }

    [Fact]
    public void MultipleProcesses_AllHandled()
    {
        var ctl = new FakeControl();
        ctl.Procs.Add(P(1));
        ctl.Procs.Add(P(2));
        ctl.HasWindow.Add(1);
        ctl.ExitsGracefully.Add(1);
        ctl.HasWindow.Add(2); // 2 hangs → killed

        var r = XaeForceCloser.ForceClose(ctl, false, null, TimeSpan.FromMilliseconds(1));

        Assert.True(r.ActionTaken);
        Assert.Contains("kill(2)", ctl.Calls);
        Assert.DoesNotContain("kill(1)", ctl.Calls);
    }
}
