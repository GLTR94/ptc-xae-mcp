using System.IO;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Normalises <c>.sln</c> paths so two references to the same solution compare
/// equal regardless of casing, quoting, relative segments or a trailing
/// separator. Pure — both the deterministic-bind core
/// (<see cref="RotDteResolver"/>) and the session's expected-solution tracking
/// route through here so they always agree on equality
/// (spec risk: path-equality fragility).
/// </summary>
internal static class SolutionPathMatch
{
    /// <summary>Canonical form, or <c>null</c> for a blank input.</summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string p = path.Trim().Trim('"');
        try { p = Path.GetFullPath(p); } catch { /* keep best-effort literal */ }
        p = p.TrimEnd('\\', '/');
        return p.ToLowerInvariant();
    }

    /// <summary>True only when both normalise to the same non-null path.</summary>
    public static bool Equal(string? a, string? b)
    {
        string? na = Normalize(a);
        string? nb = Normalize(b);
        return na is not null && na == nb;
    }
}
