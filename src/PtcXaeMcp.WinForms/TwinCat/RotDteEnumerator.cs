using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Real COM Running Object Table walk. Materialises every registered moniker,
/// keeps the ones that duck-type as a DTE (expose
/// <c>.Solution.FullName</c>) and records that solution path. Deliberately
/// matches by object shape, not by the moniker display-name string — the
/// TcXaeShell DTE moniker format is unverified on TC3 4026.22
/// (<c>reference_tc3_4026_22_quirks</c>, <c>feedback_browse_never_name_tree</c>),
/// so binding by shape removes that risk. Live-only: COM/ROT timing cannot be
/// reproduced by unit tests — verified via the live suite.
/// </summary>
internal sealed class RotDteEnumerator : IRotDteEnumerator
{
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

    public IReadOnlyList<RotDteCandidate> Enumerate()
    {
        var result = new List<RotDteCandidate>();
        IRunningObjectTable? rot = null;
        IBindCtx? bind = null;
        IEnumMoniker? en = null;
        try
        {
            if (GetRunningObjectTable(0, out rot) != 0 || rot is null) return result;
            if (CreateBindCtx(0, out bind) != 0 || bind is null) return result;

            rot.EnumRunning(out en);
            if (en is null) return result;

            var monikers = new IMoniker[1];
            while (en.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                try
                {
                    if (rot.GetObject(moniker, out object? obj) != 0 || obj is null)
                        continue;

                    string? sln = TryReadSolutionFullName(obj);
                    if (sln is not null)
                        result.Add(new RotDteCandidate(sln, obj));
                }
                catch { /* not a DTE / busy / access denied — skip */ }
                finally { if (moniker is not null) Marshal.ReleaseComObject(moniker); }
            }
        }
        catch { /* ROT not available — behave like "nothing running" */ }
        finally
        {
            if (en is not null) Marshal.ReleaseComObject(en);
            if (bind is not null) Marshal.ReleaseComObject(bind);
            if (rot is not null) Marshal.ReleaseComObject(rot);
        }
        return result;
    }

    // Duck-type: a DTE exposes Solution.FullName. An empty FullName means a DTE
    // with no solution open — still a valid candidate (it just won't match an
    // expected path), so we keep it with an empty string rather than dropping
    // it, which keeps the match-count honest.
    private static string? TryReadSolutionFullName(object obj)
    {
        try
        {
            dynamic dte = obj;
            dynamic solution = dte.Solution;
            if (solution is null) return null;
            string full = (string)solution.FullName;
            return full ?? string.Empty;
        }
        catch { return null; }
    }
}
