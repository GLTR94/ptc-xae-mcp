using System.Diagnostics;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Real OS process control over TcXaeShell. <see cref="TryGracefulClose"/>
/// posts WM_CLOSE via <see cref="Process.CloseMainWindow"/> (the same polite
/// shutdown the window's X button triggers); <see cref="Kill"/> is the
/// last-resort fallback for a window that never responds. Live-only — process
/// termination cannot be exercised by a unit test; the decision/ordering logic
/// lives in <see cref="XaeForceCloser"/> and is unit-tested separately.
/// </summary>
internal sealed class XaeProcessControl : IXaeProcessControl
{
    private const string XaeProcessName = "TcXaeShell";

    public IReadOnlyList<XaeProcessInfo> List()
    {
        var list = new List<XaeProcessInfo>();
        Process[] procs;
        try { procs = Process.GetProcessesByName(XaeProcessName); }
        catch { return list; }
        try
        {
            foreach (var p in procs)
            {
                DateTime? start = null;
                bool hasWindow = false;
                try { start = p.StartTime.ToUniversalTime(); } catch { }
                try { hasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { }
                list.Add(new XaeProcessInfo(p.Id, start, hasWindow));
            }
        }
        finally { foreach (var p in procs) p.Dispose(); }
        return list;
    }

    public bool TryGracefulClose(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // CloseMainWindow returns false when there is no main window to
            // message (already gone, or a zombie with no UI thread).
            return p.CloseMainWindow();
        }
        catch { return false; }
    }

    public bool WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch
        {
            // GetProcessById throws once the process is gone — that is success.
            return true;
        }
    }

    /// <summary>Kills the process and confirms it actually exited (issue #154,
    /// A-14). Returns true when the process is confirmed gone — either it
    /// exited within the wait, or it was already gone before we got to it.
    /// Returns false (never throws for that case) when Kill() ran but the
    /// process is still there after the wait — the caller must not report a
    /// clean kill in that case.</summary>
    public bool Kill(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            return p.WaitForExit(5000);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"Access denied killing process {pid}: {ex.Message}", ex);
        }
        catch (InvalidOperationException) { return true; /* process already exited */ }
        catch (ArgumentException) { return true; /* process no longer exists */ }
    }
}
