using System.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed record XaeProcessInfo(int Pid, DateTime? StartTimeUtc, bool HasMainWindow);

/// <summary>
/// OS process-control seam for the recovery tool. Behind an interface so
/// <see cref="XaeForceCloser"/>'s decision/ordering logic is unit-testable
/// without actually terminating anything; the real implementation
/// (<see cref="XaeProcessControl"/>) is live-only.
/// </summary>
internal interface IXaeProcessControl
{
    IReadOnlyList<XaeProcessInfo> List();
    /// <summary>Posts a graceful close (WM_CLOSE). False if no window to close.</summary>
    bool TryGracefulClose(int pid);
    bool WaitForExit(int pid, TimeSpan timeout);
    /// <summary>Kills the process and confirms it actually exited. Returns true
    /// only when the process is confirmed gone afterward (issue #154, A-14) —
    /// never assume success just because Kill() didn't throw.</summary>
    bool Kill(int pid);
}

internal sealed record ForceCloseOutcome(bool ActionTaken, string Message);

/// <summary>
/// Q1 explicit, opt-in recovery (issue #57). Never invoked implicitly by a
/// normal mutating call — terminating a TcXaeShell from inside an ordinary tool
/// would be a destructive, hard-to-reverse side effect. Refuses while a
/// dismissable modal dialog is open (the instance is alive, not a zombie);
/// otherwise asks each shell to close gracefully and only kills the ones that
/// do not exit in time. Pure decision/ordering — unit-tested via a fake
/// <see cref="IXaeProcessControl"/>.
/// </summary>
internal static class XaeForceCloser
{
    public static readonly TimeSpan GracefulWait = TimeSpan.FromSeconds(5);

    public static ForceCloseOutcome ForceClose(
        IXaeProcessControl ctl,
        bool dismissableDialogPresent,
        string? dialogTitle,
        TimeSpan? gracefulWaitOverride = null,
        int? targetPid = null)
    {
        var procs = ctl.List();
        if (procs.Count == 0)
            return new(false, "No TcXaeShell process is running — nothing to close.");

        // Opt-in narrowing: without it this closes EVERY running TcXaeShell,
        // which previously was the only lever — fine for "everything is stale",
        // destructive when only one of several instances (each possibly holding
        // someone's unsaved work) is the actual zombie.
        if (targetPid is int wanted)
        {
            var match = procs.FirstOrDefault(p => p.Pid == wanted);
            if (match is null)
                return new(false,
                    $"PID {wanted} is not a running TcXaeShell process. Running: " +
                    string.Join(", ", procs.Select(p => p.Pid)));
            procs = new[] { match };
        }

        if (dismissableDialogPresent)
            return new(false,
                "Refusing to force-close: a dismissable modal dialog " +
                $"('{dialogTitle ?? "unknown"}') is open, so the instance is alive, " +
                "not a zombie. Use twincat_dismiss_security_dialog (or dismiss it " +
                "manually) instead. Force-close is only for a stale/crashed XAE.");

        var wait = gracefulWaitOverride ?? GracefulWait;
        var lines = new List<string>();
        bool anyKillFailed = false;
        foreach (var p in procs)
        {
            bool asked = ctl.TryGracefulClose(p.Pid);
            if (asked && ctl.WaitForExit(p.Pid, wait))
            {
                lines.Add($"PID {p.Pid}: closed gracefully.");
                continue;
            }
            // A-14: report the kill honestly — Kill() returning false means the
            // process is still there despite the attempt, not "killed".
            bool killed = ctl.Kill(p.Pid);
            if (!killed) anyKillFailed = true;
            string verb = killed ? "killed" : "kill FAILED — process may still be running";
            lines.Add(asked
                ? $"PID {p.Pid}: did not exit within {wait.TotalSeconds:F0}s — {verb}."
                : $"PID {p.Pid}: no window to close — {verb}.");
        }
        string summary = anyKillFailed
            ? "Force-close attempted but at least one process did not confirm exit — verify manually:\n"
            : "Force-closed TcXaeShell (any unsaved XAE work is lost):\n";
        return new(true, summary + string.Join("\n", lines));
    }
}
