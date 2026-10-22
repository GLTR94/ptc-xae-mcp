namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Q1 explicit recovery (issue #57). Deliberately does NOT call
/// <see cref="SessionGuards.RequireOpenSolution"/>: force-close must work
/// precisely when the DTE / solution is unreachable — that is the situation it
/// exists to recover from.
/// </summary>
internal sealed class RecoveryCoordinator(ITwinCatSession session)
{
    public string ForceCloseXae(int? pid = null)
    {
        try { return session.ForceCloseXae(pid); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }
    }
}
