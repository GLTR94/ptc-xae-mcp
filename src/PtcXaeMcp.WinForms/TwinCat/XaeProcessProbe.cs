using System.Diagnostics;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// OS-level detection of running TcXaeShell instances. Deliberately does NOT
/// go through COM / the Running Object Table: a busy or modal-dialog-blocked
/// XAE is invisible to <c>GetActiveObject</c> but still very much a process,
/// so process enumeration is the only reliable way to see a second shell
/// (feedback_single_xae_constraint — the recurring "2 XAEs open" bug).
/// </summary>
internal static class XaeProcessProbe
{
    // TcXaeShell.exe → process name "TcXaeShell" (no extension).
    private const string XaeProcessName = "TcXaeShell";

    /// <summary>
    /// Number of live TcXaeShell processes, or <c>null</c> when the probe itself failed
    /// ("unknown" — issue #149 A-16). Callers must never read unknown as "none": that is
    /// how a second XAE got launched.
    /// </summary>
    public static int? Count()
    {
        try
        {
            var procs = Process.GetProcessesByName(XaeProcessName);
            try { return procs.Length; }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch (Exception) { return null; }
    }

    /// <summary>True only when at least one TcXaeShell is known to run (unknown → false).</summary>
    public static bool AnyRunning() => Count() > 0;

    /// <summary>True only when more than one TcXaeShell is known to run (unknown → false).</summary>
    public static bool MultipleRunning() => Count() > 1;

    /// <summary>
    /// Launch decision: a new TcXaeShell may be started only when zero are known to run.
    /// Returns the refusal text otherwise (running instance or unknown count).
    /// </summary>
    internal static string? LaunchBlockReason(int? count) => count switch
    {
        null => "Could not determine whether a TcXaeShell process is already running (process " +
                "enumeration failed) — refusing to launch a second XAE. Start the XAE by hand or retry.",
        0 => null,
        _ => $"{count} TcXaeShell process(es) already running.",
    };
}
