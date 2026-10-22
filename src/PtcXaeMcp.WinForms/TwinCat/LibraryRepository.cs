using System.Security.Cryptography;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Read-back evidence for a library install (issue #152, B-14). The installed-library list proves a
/// NEW title/version; an overwrite of an already installed title/version leaves that list unchanged,
/// so the second, independent proof is a byte-identical copy of the installed file in the "System"
/// library repository on disk.
/// </summary>
internal static class LibraryRepository
{
    /// <summary>Repository roots of this machine that exist (TC 4026: ProgramData; older builds: the install dir).</summary>
    public static IReadOnlyList<string> Roots()
    {
        var roots = new List<string>();
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(programData))
            roots.Add(Path.Combine(programData, "Beckhoff", "TwinCAT", "PlcEngineering", "Managed Libraries"));
        string? installDir = TwinCatPaths.GetInstallDir();
        if (!string.IsNullOrEmpty(installDir))
            roots.Add(Path.Combine(installDir, "Components", "Plc", "Managed Libraries"));
        roots.Add(@"C:\TwinCAT\3.1\Components\Plc\Managed Libraries");
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Path of a byte-identical copy of <paramref name="libraryFile"/> in any repository root, or null.</summary>
    public static string? FindCopy(string libraryFile) => FindCopy(libraryFile, Roots());

    public static string? FindCopy(string libraryFile, IEnumerable<string> roots)
    {
        if (!File.Exists(libraryFile)) return null;
        var source = new FileInfo(libraryFile);
        byte[]? sourceHash = null;
        foreach (string root in roots)
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (string candidate in Directory.EnumerateFiles(root, "*" + source.Extension, options))
            {
                try
                {
                    var fi = new FileInfo(candidate);
                    if (fi.Length != source.Length ||
                        string.Equals(fi.FullName, source.FullName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    sourceHash ??= Hash(source.FullName);
                    if (Hash(fi.FullName).AsSpan().SequenceEqual(sourceHash)) return fi.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return null;
    }

    private static byte[] Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
