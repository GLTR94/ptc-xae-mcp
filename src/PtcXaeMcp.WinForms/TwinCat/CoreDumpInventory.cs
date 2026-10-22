namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Enumerates .core dump files across UMR instance Boot folders and an optional PLC project folder.
/// Pure file I/O — no XAE/COM required.
/// </summary>
internal static class CoreDumpInventory
{
    /// <summary>
    /// Find all .core files for the given instance/port filters.
    /// Pass <paramref name="projectFolder"/> to also search for manual dumps and check compileinfo presence.
    /// Results sorted newest-first.
    /// </summary>
    public static IReadOnlyList<CoreDumpEntry> ListDumps(
        string? instanceName, int? port, string? projectFolder)
    {
        var results = new List<CoreDumpEntry>();
        bool hasCI = HasCompileInfo(projectFolder);

        foreach (var (name, dir) in EnumerateInstanceDirs(instanceName))
        {
            var coreDumpRoot = Path.Combine(dir, "3.1", "Boot", "Plc", "CoreDump");
            if (!Directory.Exists(coreDumpRoot)) continue;

            // Auto/exception dumps: Port_NNN.XXX\Plc\Port_NNN.XXX.core
            foreach (var portDir in SafeEnumDirs(coreDumpRoot))
            {
                var dirName = Path.GetFileName(portDir);
                if (!System.Text.RegularExpressions.Regex.IsMatch(dirName, @"^Port_\d+\.\d+$",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    continue;

                var plcSub = Path.Combine(portDir, "Plc");
                if (!Directory.Exists(plcSub)) continue;

                foreach (var f in SafeEnumFiles(plcSub, "*.core"))
                {
                    int? filePort = CoreDumpClassifier.ExtractPort(f);
                    if (port.HasValue && filePort != port.Value) continue;
                    var fi = new FileInfo(f);
                    results.Add(new CoreDumpEntry(f, CoreDumpKind.AutoException,
                        fi.LastWriteTime, fi.Length, filePort, hasCI, name));
                }
            }

            // Call-stack dumps: Port_NNN_CallStackDump_*.core directly in CoreDump root
            foreach (var f in SafeEnumFiles(coreDumpRoot, "Port_*CallStackDump*.core"))
            {
                int? filePort = CoreDumpClassifier.ExtractPort(f);
                if (port.HasValue && filePort != port.Value) continue;
                var fi = new FileInfo(f);
                results.Add(new CoreDumpEntry(f, CoreDumpKind.CallStackOnly,
                    fi.LastWriteTime, fi.Length, filePort, hasCI, name));
            }
        }

        // Manual dumps in the PLC project folder
        if (!string.IsNullOrWhiteSpace(projectFolder) && Directory.Exists(projectFolder))
        {
            foreach (var f in SafeEnumFiles(projectFolder, "*.core"))
            {
                if (CoreDumpClassifier.ClassifyPath(f) != CoreDumpKind.Manual) continue;
                var fi = new FileInfo(f);
                results.Add(new CoreDumpEntry(f, CoreDumpKind.Manual,
                    fi.LastWriteTime, fi.Length, null, hasCI, null));
            }
        }

        results.Sort((a, b) => b.WriteTime.CompareTo(a.WriteTime));
        return results;
    }

    public static CoreDumpEntry? FindNewest(string? instanceName, int? port, string? projectFolder)
    {
        var all = ListDumps(instanceName, port, projectFolder);
        return all.Count > 0 ? all[0] : null;
    }

    public static bool HasCompileInfo(string? projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder)) return false;
        var ciDir = Path.Combine(projectFolder, "_CompileInfo");
        if (!Directory.Exists(ciDir)) return false;
        return SafeEnumFiles(ciDir, "*.compileinfo").Any();
    }

    private static IReadOnlyList<(string Name, string Dir)> EnumerateInstanceDirs(string? instanceName)
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in UmrProcessManager.RuntimesRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in SafeEnumDirs(root))
            {
                var name = Path.GetFileName(dir)!;
                if (!string.IsNullOrWhiteSpace(instanceName) &&
                    !name.Equals(instanceName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(name))
                    result.Add((name, dir));
            }
        }

        return result;
    }

    private static IEnumerable<string> SafeEnumDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumFiles(string path, string pattern)
    {
        try { return Directory.EnumerateFiles(path, pattern); }
        catch { return []; }
    }
}
