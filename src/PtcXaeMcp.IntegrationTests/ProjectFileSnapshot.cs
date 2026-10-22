using System.Text.RegularExpressions;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Byte-exact copy of a fixture solution's project files (<c>*.plcproj</c>, <c>*.tsproj</c>),
/// taken before a test mutates the project through the XAE. <see cref="RestoreChanged"/> writes
/// back every file that differs, so a failed library test can never leave the per-run fixture
/// uncompilable for the tests after it (issue #178). Restoring files under an open solution is
/// only safe with the solution closed without saving — see
/// <c>LibraryManageReferencesIntegrationTests</c>.
/// </summary>
internal sealed class ProjectFileSnapshot
{
    private static readonly string[] Patterns = ["*.plcproj", "*.tsproj"];

    private readonly Dictionary<string, byte[]> _files;

    private ProjectFileSnapshot(Dictionary<string, byte[]> files) => _files = files;

    public IReadOnlyCollection<string> Files => _files.Keys;

    /// <summary>Snapshots every project file below the solution's folder.</summary>
    public static ProjectFileSnapshot Capture(string slnPath)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(slnPath))
            ?? throw new ArgumentException($"No folder for '{slnPath}'.", nameof(slnPath));
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string pattern in Patterns)
            foreach (string f in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                files[f] = File.ReadAllBytes(f);
        return new ProjectFileSnapshot(files);
    }

    /// <summary>Files whose current content differs from the snapshot (or that were deleted).</summary>
    public IReadOnlyList<string> ChangedFiles() =>
        _files.Where(kv => !File.Exists(kv.Key) || !File.ReadAllBytes(kv.Key).AsSpan().SequenceEqual(kv.Value))
              .Select(kv => kv.Key)
              .ToList();

    /// <summary>Writes the snapshot back over every changed file; returns the files written.</summary>
    public IReadOnlyList<string> RestoreChanged()
    {
        var changed = ChangedFiles();
        foreach (string f in changed)
            File.WriteAllBytes(f, _files[f]);
        return changed;
    }
}

/// <summary>
/// Parses <c>twincat_list_library_references</c> output
/// (<c>"  Placeholder: Name Version (Publisher) [src=…]…"</c>) into comparable references.
/// </summary>
internal static class LibraryReferenceListing
{
    internal sealed record Reference(string Placeholder, string Name, string Version, string Publisher)
    {
        public override string ToString() => $"{Placeholder}: {Name} {Version} ({Publisher})";
    }

    private static readonly Regex Line = new(
        @"^\s+(?<ph>[^:\s][^:]*):\s+(?<name>\S+)\s+(?:(?<ver>[^\s(]+)\s+)?\((?<pub>[^)]*)\)",
        RegexOptions.CultureInvariant);

    public static IReadOnlyList<Reference> Parse(string listing) =>
        listing.Split('\n')
            .Select(l => Line.Match(l.TrimEnd('\r')))
            .Where(m => m.Success)
            .Select(m => new Reference(m.Groups["ph"].Value.Trim(), m.Groups["name"].Value,
                m.Groups["ver"].Success ? m.Groups["ver"].Value : string.Empty, m.Groups["pub"].Value))
            .ToList();

    public static bool Contains(string listing, string name) =>
        Parse(listing).Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(r.Placeholder, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null when both listings hold the same references (order ignored), otherwise the difference.</summary>
    public static string? Difference(string before, string after)
    {
        var a = Parse(before).Select(r => r.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var b = Parse(after).Select(r => r.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = a.Except(b, StringComparer.OrdinalIgnoreCase).ToList();
        var extra = b.Except(a, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0 && extra.Count == 0) return null;
        return $"missing: [{string.Join("; ", missing)}], extra: [{string.Join("; ", extra)}]";
    }
}
