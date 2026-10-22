using System.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // Deterministic anchor for Q2 (issue #57). The only trustworthy "which
    // project did we mean" signal the server has is the path it was last asked
    // to open/launch — COM GetActiveObject itself is nondeterministic, so it
    // cannot be used to discover the expected project (chicken-and-egg).
    // Static because the single-XAE / single-session constraint holds
    // (feedback_single_xae_constraint) and GetActiveTcDteOrNull is static.
    // Null => "no expectation"; callers must then keep the hard block and never
    // guess.
    private static string? _expectedSolutionPath;

    internal static void SetExpectedSolutionPath(string? slnPath) =>
        _expectedSolutionPath = string.IsNullOrWhiteSpace(slnPath) ? null : slnPath;

    internal static void ClearExpectedSolutionPath() => _expectedSolutionPath = null;

    private static readonly IRotDteEnumerator RotEnumerator = new RotDteEnumerator();

    private static RotDteResolution ResolveExpectedDte() =>
        RotDteResolver.Resolve(RotEnumerator.Enumerate(), _expectedSolutionPath);

    /// <summary>
    /// Q2 guard. With more than one TcXaeShell running, COM
    /// <c>GetActiveObject</c> is nondeterministic — so refuse unless the ROT
    /// contains exactly one DTE with the expected solution open. 0, &gt;1, or
    /// "no expectation" all keep the hard block with a precise reason. Returns
    /// null when 0/1 shells run or a unique deterministic match exists.
    /// </summary>
    public string? MultiXaeBlockReason() => MultiXaeBlockReasonCore();

    /// <summary>
    /// Attach for callers outside the session object (the schema runner, issue #149 A-11): the
    /// same ProgID probe and multi-XAE binding as every session call, the same Q2 guard, and busy
    /// retried against a deadline with the blocking dialog named — instead of a private
    /// GetActiveObject loop that swallowed every error.
    /// </summary>
    internal static dynamic AttachForExternalCaller(IEnumerable<string>? probeOrder, TimeSpan timeout)
    {
        string? multi = MultiXaeBlockReasonCore();
        if (multi is not null)
            throw new InvalidOperationException(multi.Replace("[Error] ", string.Empty));
        return ComCall.Run(() => GetActiveTcDteOrNull(probeOrder), timeout, operation: "attach")
               ?? throw new InvalidOperationException(DescribeUnreachableXae());
    }

    private static string? MultiXaeBlockReasonCore()
    {
        // Unknown (probe failed) cannot prove ambiguity; RequireOpenSolution still reports the
        // unreachable/unknown state separately.
        if (XaeProcessProbe.Count() is not int n || n <= 1) return null;

        var res = ResolveExpectedDte();
        if (res.Outcome == RotDteOutcome.Unique) return null;

        // Surface PIDs so a caller can target twincat_force_close_xae(pid=…) at
        // one specific stale instance instead of the all-or-nothing close —
        // previously the only way to see which PIDs were involved was Task
        // Manager, outside the MCP surface entirely.
        string pidNote;
        try
        {
            var procs = new XaeProcessControl().List();
            pidNote = procs.Count > 0
                ? " Running PIDs: " + string.Join(", ", procs.Select(p =>
                    $"{p.Pid}{(p.HasMainWindow ? "" : " (no window)")}")) + "."
                : "";
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { pidNote = ""; }

        string tail =
            " COM attaches to an arbitrary instance, so this is blocked to avoid " +
            "acting on the wrong project. Close all but one TcXaeShell window and " +
            "retry. If an instance is stale or crashed, recover it with the " +
            "explicit twincat_force_close_xae tool (pass pid=<PID> to close only " +
            "that one)." + pidNote;

        return res.Outcome switch
        {
            RotDteOutcome.NoExpectedPath =>
                $"[Error] {n} TwinCAT XAE instances are running and no expected " +
                "solution is tracked yet — open the intended project via " +
                "twincat_open_project so the server can bind deterministically." + tail,
            RotDteOutcome.None =>
                $"[Error] {n} TwinCAT XAE instances are running but none has the " +
                $"expected solution open ('{_expectedSolutionPath}')." + tail,
            RotDteOutcome.Ambiguous =>
                $"[Error] {res.MatchCount} of {n} running TwinCAT XAE instances have " +
                $"the expected solution open ('{_expectedSolutionPath}') — still " +
                "ambiguous." + tail,
            _ => $"[Error] {n} TwinCAT XAE instances are running." + tail,
        };
    }

    /// <summary>
    /// Q1 explicit, opt-in recovery. Never called implicitly by a mutating
    /// tool. Refuses while a dismissable known modal dialog is open (the
    /// instance is alive, not a zombie); otherwise closes each TcXaeShell
    /// gracefully and kills only the ones that do not exit in time.
    /// </summary>
    public string ForceCloseXae(int? pid = null)
    {
        bool dialog = Win32DialogAutomation.TryFindKnownModalDialog(out string? title);
        var outcome = XaeForceCloser.ForceClose(new XaeProcessControl(), dialog, title, targetPid: pid);
        // Recovery succeeded — drop the stale expectation so the next connect
        // starts clean rather than blocking on a path no instance has anymore.
        // Safe even when only one of several instances was targeted: it just
        // means the next open/probe re-derives the expectation from scratch.
        if (outcome.ActionTaken) ClearExpectedSolutionPath();
        return outcome.Message;
    }
}
