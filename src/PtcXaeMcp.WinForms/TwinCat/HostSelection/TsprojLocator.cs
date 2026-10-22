namespace PtcXaeMcp.WinForms.TwinCat.HostSelection;

/// <summary>
/// Locates the <c>.tsproj</c> that gives host resolution a TcVersion to read.
/// A TwinCAT solution's <c>.tsproj</c> typically sits in a project subfolder
/// beside the <c>.sln</c>. Any ambiguity (none found, missing path, IO error)
/// returns null — the caller then keeps the legacy probe order, so behaviour
/// is never worse than today.
/// </summary>
public static class TsprojLocator
{
    /// <summary>
    /// Given a <c>.sln</c> path (or a <c>.tsproj</c> path directly), returns
    /// the associated <c>.tsproj</c>, or null if it cannot be determined
    /// unambiguously. Never throws.
    /// </summary>
    public static string? FindForSolution(string? slnOrTsprojPath)
    {
        if (string.IsNullOrWhiteSpace(slnOrTsprojPath))
            return null;

        try
        {
            if (slnOrTsprojPath.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase))
                return File.Exists(slnOrTsprojPath) ? slnOrTsprojPath : null;

            if (!File.Exists(slnOrTsprojPath))
                return null;

            var dir = Path.GetDirectoryName(Path.GetFullPath(slnOrTsprojPath));
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            // A solution that names its .tsproj (also a generated one in %TEMP%) is exact.
            var declared = SolutionFile.ProjectPaths(File.ReadAllText(slnOrTsprojPath))
                .Where(p => p.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase))
                .Select(p => Path.GetFullPath(Path.Combine(dir, p)))
                .Where(File.Exists)
                .Take(2)
                .ToList();
            if (declared.Count == 1)
                return declared[0];

            // Search the solution tree; a single .tsproj is the common case.
            var hits = Directory.EnumerateFiles(dir, "*.tsproj", SearchOption.AllDirectories)
                .Take(2)
                .ToList();

            return hits.Count == 1 ? hits[0] : null;
        }
        catch
        {
            return null;
        }
    }
}
