using System.Text;
using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // ── coredump-analysis ─────────────────────────────────────────────────────
    //
    // File-based backbone: no XAE/COM required for List and Analyze.
    // Both tools discover files via UmrProcessManager.RuntimesRoots and an
    // optional PLC project folder (auto-discovered from the open solution when
    // XAE is running; skipped gracefully when it is not).
    //
    // ReadCallStack is deliberately isolated as a GUI-automation best-effort;
    // it never fabricates a stack and always degrades to a precondition error.

    public string CoreDumpList(string? instanceName, int? port, string? projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            projectFolder = TryGetActiveProjectFolder();

        var entries = CoreDumpInventory.ListDumps(instanceName, port, projectFolder);

        var sb = new StringBuilder();
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"=== Core dump inventory  " +
            $"instance={instanceName ?? "all"}  port={port?.ToString(CultureInfo.InvariantCulture) ?? "all"} ===");

        if (entries.Count == 0)
        {
            sb.AppendLine("No core dumps found.");
            if (string.IsNullOrWhiteSpace(projectFolder))
                sb.AppendLine(
                    "[Tip] Pass projectFolder= to also search for manual dumps in the PLC project directory.");
            return sb.ToString();
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Found {entries.Count} dump(s):");
        sb.AppendLine();

        foreach (var e in entries)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{e.Kind}]  {e.FilePath}");
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"  Instance  : {e.InstanceName ?? "(project folder)"}  " +
                $"Port: {e.Port?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"  Written   : {e.WriteTime:yyyy-MM-dd HH:mm:ss}  " +
                $"Size: {e.SizeBytes / 1024.0:F1} KB");
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"  compileinfo: {(e.HasCompileInfo ? "present" : "MISSING — dump uninterpretable without it")}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public string CoreDumpAnalyze(
        string? dumpPath, string? instanceName, int? port, string? projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            projectFolder = TryGetActiveProjectFolder();

        // Resolve dump
        if (string.IsNullOrWhiteSpace(dumpPath))
        {
            var newest = CoreDumpInventory.FindNewest(instanceName, port, projectFolder)
                ?? throw new InvalidOperationException(
                    "No core dump found. Provide dumpPath= or ensure the instance/port filter matches " +
                    "an existing dump. Use twincat_coredump_list to see available dumps.");
            dumpPath = newest.FilePath;
        }

        if (!File.Exists(dumpPath))
            throw new FileNotFoundException($"Core dump not found: {dumpPath}", dumpPath);

        var fi = new FileInfo(dumpPath);
        var kind = CoreDumpClassifier.ClassifyPath(dumpPath);
        int? filePort = CoreDumpClassifier.ExtractPort(dumpPath);

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"=== Core dump analysis: {Path.GetFileName(dumpPath)} ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Path     : {dumpPath}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Kind     : {kind}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Written  : {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Size     : {fi.Length / 1024.0:F1} KB");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Port     : {filePort?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"compileinfo: {(CoreDumpInventory.HasCompileInfo(projectFolder) ? "present" : "not found")}");

        if (kind == CoreDumpKind.Manual)
            sb.AppendLine("[Note] Manual dump — evidence of engineer inspection, NOT a crash.");
        if (kind == CoreDumpKind.CallStackOnly)
            sb.AppendLine("[Note] Call-stack dump — no variable values (CreateCallstackCoreDump).");

        // ── String extraction ──────────────────────────────────────────────────
        byte[] data;
        try { data = File.ReadAllBytes(dumpPath); }
        catch (Exception ex) when (ComCall.NotBusy(ex)) {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Error] Could not read dump file: {ex.Message}");
            return sb.ToString();
        }

        var (lastTs, stateStrings) = CoreDumpStringExtractor.ExtractStateInfo(data, fi.LastWriteTime);

        // ── Hang gap ───────────────────────────────────────────────────────────
        sb.AppendLine();
        if (lastTs.HasValue)
        {
            var gap = fi.LastWriteTime - lastTs.Value;
            sb.AppendLine(CultureInfo.InvariantCulture, $"Last app-log : {lastTs.Value:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Hang gap     : {FormatGap(gap)}");
        }
        else
        {
            sb.AppendLine("Last app-log : (no timestamp found in embedded strings)");
        }

        // ── State strings ──────────────────────────────────────────────────────
        if (stateStrings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"--- Embedded strings ({stateStrings.Count} unique) ---");
            foreach (var s in stateStrings.Take(40))
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {s}");
            if (stateStrings.Count > 40)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  … ({stateStrings.Count - 40} more omitted)");
        }

        // ── Event join ─────────────────────────────────────────────────────────
        var (dbPath, xmlPath) = FindEventFiles(dumpPath, projectFolder);
        if (dbPath is null)
        {
            sb.AppendLine(
                "\n[Events] LoggedEvents.db not found " +
                "(searched near dump and projectFolder). Pass projectFolder= to help locate it.");
        }
        else if (xmlPath is null)
        {
            sb.AppendLine("\n[Events] CurrentEventMessages.xml not found near the database.");
        }
        else
        {
            var (events, errNote) = CoreDumpEventJoiner.JoinEvents(dbPath, xmlPath);
            sb.AppendLine();
            if (errNote is not null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"[Events] {errNote}");
            }
            else if (events.Count == 0)
            {
                sb.AppendLine("[Events] No active (unconfirmed) events in LoggedEvents.db.");
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"--- Active events ({events.Count}) ---");
                foreach (var ev in events)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  Source: {ev.SourceId}  Class: {ev.EventClassId}");
                    if (ev.MessageText is not null)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    Message: {ev.MessageText}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    public string CoreDumpReadCallStack() => CoreDumpCallStackReader.ReadCallStack();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatGap(TimeSpan gap)
    {
        if (gap.TotalSeconds < 10)
            return $"{gap.TotalSeconds:F1} s — trap at instant (exception)";
        if (gap.TotalMinutes < 1)
            return $"{gap.TotalSeconds:F0} s — stalled shortly before dump";
        return $"{gap.TotalMinutes:F1} min — PLC stalled/hung; dump taken {gap.TotalMinutes:F0} min after last log";
    }

    private string? TryGetActiveProjectFolder()
    {
        try
        {
            var sln = ActiveSolutionPath();
            if (string.IsNullOrEmpty(sln) || !File.Exists(sln)) return null;
            var slnDir = Path.GetDirectoryName(sln);
            if (slnDir is null) return null;
            var plcprojs = Directory.GetFiles(slnDir, "*.plcproj", SearchOption.AllDirectories);
            return plcprojs.Length > 0 ? Path.GetDirectoryName(plcprojs[0]) : null;
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return null; }
    }

    private static (string? DbPath, string? XmlPath)
        FindEventFiles(string dumpPath, string? projectFolder)
    {
        string? dbPath = null;
        string? xmlPath = null;

        // Search candidate directories: near the dump, in the project folder, up to 3 parents
        var searchRoots = new List<string>();
        var dumpDir = Path.GetDirectoryName(dumpPath);
        if (!string.IsNullOrEmpty(dumpDir)) searchRoots.Add(dumpDir);
        if (!string.IsNullOrEmpty(projectFolder)) searchRoots.Add(projectFolder);

        foreach (var startDir in searchRoots)
        {
            var dir = startDir;
            for (int depth = 0; depth <= 3; depth++)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) break;
                if (dbPath is null && File.Exists(Path.Combine(dir, "LoggedEvents.db")))
                    dbPath = Path.Combine(dir, "LoggedEvents.db");
                if (xmlPath is null && File.Exists(Path.Combine(dir, "CurrentEventMessages.xml")))
                    xmlPath = Path.Combine(dir, "CurrentEventMessages.xml");
                if (dbPath is not null && xmlPath is not null) break;
                dir = Path.GetDirectoryName(dir) ?? "";
            }
            if (dbPath is not null && xmlPath is not null) break;
        }

        return (dbPath, xmlPath);
    }
}
