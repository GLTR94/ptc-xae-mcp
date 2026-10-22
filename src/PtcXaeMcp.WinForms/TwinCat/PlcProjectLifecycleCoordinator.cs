namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// plc-project-lifecycle: create, take in, export and import PLC projects under TIPC, archive the
/// solution and open an archive. Every argument and collision
/// check runs before the XAE is touched — a failing CreateChild raises a MODAL dialog in the XAE
/// (live 2026-10-07). Every mutation is proven by a read-back (TIPC children, or the written file).
/// </summary>
internal sealed class PlcProjectLifecycleCoordinator(ITwinCatSession session)
{
    /// <summary>Where Beckhoff templates are looked up by name. Replaceable for tests.</summary>
    public IReadOnlyList<string> TemplateRoots { get; init; } = PlcProjectLifecycleArgs.DefaultTemplateRoots();

    /// <summary>File timestamps have ~2 s resolution on some file systems.</summary>
    private static readonly TimeSpan TimestampSlack = TimeSpan.FromSeconds(2);

    // ---- twincat_plc_project_create ------------------------------------------------

    public string CreateProject(string name, string? template)
    {
        string? templateError = PlcProjectLifecycleArgs.ResolveTemplate(template, TemplateRoots, out string source);
        var error = SessionGuards.RequireOpenSolution(session)
            ?? Err(PlcProjectLifecycleArgs.ValidateProjectName(name))
            ?? Err(templateError);
        if (error is not null) return error;

        if (!TryListNames(out var before, out error)) return error!;
        error = Err(PlcProjectLifecycleArgs.CheckNameFree(name, before))
            ?? SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        if (!TryCreate(name, 0, source, vInfoNull: false, out string created, out error)) return error!;
        return ReadBackNewProject(name, before, created,
            $"Created PLC project '{name}' from template '{source}'.");
    }

    // ---- twincat_plc_project_add_existing ------------------------------------------

    public string AddExisting(string path, string mode, string? name)
    {
        string? modeError = PlcProjectLifecycleArgs.ParseMode(mode, out var parsed);
        var error = SessionGuards.RequireOpenSolution(session)
            ?? Err(PlcProjectLifecycleArgs.RequireExistingFile(path, "path", ".plcproj", ".tpzip"))
            ?? Err(modeError);
        if (error is not null) return error;

        if (string.Equals(Path.GetExtension(path), ".tpzip", StringComparison.OrdinalIgnoreCase))
        {
            // A .tpzip is always unpacked as a copy; MOVE/IN_PLACE have no meaning for an archive.
            if (parsed != PlcProjectAddMode.Copy)
                return $"[Error] mode {mode.Trim().ToUpperInvariant()} does not apply to a .tpzip archive — use mode=COPY (or twincat_plc_project_import).";
            if (string.IsNullOrWhiteSpace(name))
                return "[Error] name is required when path is a .tpzip archive.";
            return Import(path, name);
        }

        // CreateChild ignores the name for a .plcproj and keeps the project's own <Name> (live 2026-10-07).
        string effective = PlcProjectLifecycleArgs.EffectivePlcprojName(path);
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), effective, StringComparison.OrdinalIgnoreCase))
            return $"[Error] The XAE names a project taken in from a .plcproj after the project itself ('{effective}'); " +
                   $"it cannot be renamed to '{name}' here. Omit name or pass name='{effective}'.";
        error = Err(PlcProjectLifecycleArgs.ValidateProjectName(effective, "the project's name"));
        if (error is not null) return error;

        if (parsed != PlcProjectAddMode.InPlace)
        {
            // COPY/MOVE put the project folder next to the solution, named after the .plcproj file.
            string? slnDir = SolutionDirectory();
            string target = slnDir is null ? string.Empty : Path.Combine(slnDir, Path.GetFileNameWithoutExtension(path));
            string sourceDir = Path.GetDirectoryName(path) ?? string.Empty;
            if (target.Length > 0 && Directory.Exists(target) && !SamePath(target, sourceDir) && Directory.EnumerateFileSystemEntries(target).Any())
                return $"[Error] mode {parsed.ToString().ToUpperInvariant()} would copy the project to '{target}', which already exists and is not empty. Use mode=IN_PLACE or move it away first.";
        }

        if (!TryListNames(out var before, out error)) return error!;
        error = Err(PlcProjectLifecycleArgs.CheckNameFree(effective, before))
            ?? SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        // Infosys: IN_PLACE passes an empty name.
        string passName = parsed == PlcProjectAddMode.InPlace ? string.Empty : effective;
        if (!TryCreate(passName, (int)parsed, path, vInfoNull: false, out string created, out error)) return error!;

        string modeText = parsed switch
        {
            PlcProjectAddMode.Copy => "copied into the solution (source unchanged)",
            PlcProjectAddMode.Move => File.Exists(path)
                ? "moved into the solution (note: the source file still exists)"
                : "moved into the solution (source folder moved)",
            _ => $"referenced in place at {path}",
        };
        return ReadBackNewProject(effective, before, created, $"Added PLC project '{effective}' — {modeText}.");
    }

    // ---- twincat_plc_project_export ------------------------------------------------

    public string Export(string plcProjectName, string tpzipPath)
    {
        var error = SessionGuards.RequireOpenSolution(session)
            ?? Err(PlcProjectLifecycleArgs.ValidateProjectName(plcProjectName, "plcProjectName"))
            ?? Err(PlcProjectLifecycleArgs.RequireOutputFile(tpzipPath, "tpzipPath", ".tpzip"));
        if (error is not null) return error;

        if (!TryListNames(out var names, out error)) return error!;
        if (!names.Any(n => string.Equals(n, plcProjectName, StringComparison.OrdinalIgnoreCase)))
            return $"[Error] No PLC project '{plcProjectName}' under TIPC. Present: {Join(names)}.";

        DateTime started = DateTime.UtcNow - TimestampSlack;
        try { session.ExportPlcProject(plcProjectName, tpzipPath); }
        catch (Exception ex) { return McpErrors.EnsureError(ComCall.Describe(ex)); }

        return FileWrittenSince(tpzipPath, started, "Export")
            ?? $"Exported PLC project '{plcProjectName}' to {tpzipPath}. read-back: {FileProof(tpzipPath)}.";
    }

    // ---- twincat_plc_project_import ------------------------------------------------

    public string Import(string tpzipPath, string name)
    {
        var error = SessionGuards.RequireOpenSolution(session)
            ?? Err(PlcProjectLifecycleArgs.RequireExistingFile(tpzipPath, "tpzipPath", ".tpzip"))
            ?? Err(PlcProjectLifecycleArgs.ValidateProjectName(name));
        if (error is not null) return error;

        if (!TryListNames(out var before, out error)) return error!;
        error = Err(PlcProjectLifecycleArgs.CheckNameFree(name, before))
            ?? SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        // Infosys: TIPC.CreateChild(name, 0, null, pfad) — the name IS honoured for a .tpzip (live 2026-10-07).
        if (!TryCreate(name, 0, tpzipPath, vInfoNull: true, out string created, out error)) return error!;
        return ReadBackNewProject(name, before, created, $"Imported '{tpzipPath}' as PLC project '{name}'.");
    }

    // ---- twincat_solution_save_archive ---------------------------------------------

    public string SaveArchive(string tszipPath)
    {
        var error = SessionGuards.RequireOpenSolution(session)
            ?? Err(PlcProjectLifecycleArgs.RequireOutputFile(tszipPath, "tszipPath", ".tszip"));
        if (error is not null) return error;

        DateTime started = DateTime.UtcNow - TimestampSlack;
        try
        {
            // The archive is built from the files on disk — save first so it holds the current state.
            session.SaveAll();
            session.SaveSolutionArchive(tszipPath);
        }
        catch (Exception ex) { return McpErrors.EnsureError(ComCall.Describe(ex)); }

        return FileWrittenSince(tszipPath, started, "Save as archive")
            ?? $"Saved the solution as archive {tszipPath}. read-back: {FileProof(tszipPath)}.";
    }

    // ---- twincat_solution_open_archive ---------------------------------------------

    public string OpenArchive(string tszipPath, string destDir, string name, bool closeCurrentFirst)
    {
        var error = Err(PlcProjectLifecycleArgs.RequireExistingFile(tszipPath, "tszipPath", ".tszip"))
            ?? Err(PlcProjectLifecycleArgs.ValidateProjectName(name));
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(destDir) || !Path.IsPathFullyQualified(destDir))
            return $"[Error] destDir must be an absolute path: {destDir}";
        if (Directory.Exists(destDir) && Directory.EnumerateFileSystemEntries(destDir).Any())
            return $"[Error] destDir '{destDir}' already exists and is not empty — pass a new or empty directory.";
        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(destDir));
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            return $"[Error] The parent directory of destDir does not exist: {parent}";

        try
        {
            var multi = session.MultiXaeBlockReason();
            if (multi is not null) return multi;
            if (!session.IsConnected)
                return McpErrors.EnsureError(session.DescribeUnreachable()) +
                       " Start the XAE first (e.g. twincat_open_project), then retry.";
            if (session.HasOpenSolution())
            {
                if (!closeCurrentFirst)
                    return "[Error] A solution is already open. Use closeCurrentFirst=true (closes it without saving) or close it first.";
                session.CloseSolution(saveFirst: false);
            }
        }
        catch (Exception ex) { return McpErrors.EnsureError(ComCall.Describe(ex)); }

        string opened;
        try { opened = session.OpenSolutionArchive(tszipPath, destDir, name); }
        catch (Exception ex) { return McpErrors.EnsureError(ComCall.Describe(ex)); }

        string expected = Path.Combine(destDir, name + ".sln");
        if (!File.Exists(expected))
            return $"[Error] Open archive did not land: '{expected}' does not exist after AddFromTemplate returned (open solution: '{opened}').";
        string? active;
        try { active = session.ActiveSolutionPath(); }
        catch (Exception ex) { return McpErrors.EnsureError(ComCall.Describe(ex)); }
        if (!SamePath(active, expected))
            return $"[Error] The archive was unpacked to '{expected}', but the open solution is '{active ?? "<none>"}'. Open it with twincat_open_project.";
        return $"Opened archive {tszipPath} as {expected}. read-back: the open solution is {active}.";
    }

    // ---- twincat_plc_instance_from_tmc ---------------------------------------------

    public string InstanceFromTmc(string? name, string tmcPath)
    {
        var error = SessionGuards.RequireOpenSolution(session)
            ?? Err(PlcProjectLifecycleArgs.RequireExistingFile(tmcPath, "tmcPath", ".tmc"));
        if (error is not null) return error;

        // CreateChild ignores the name for a .tmc: the instance is named after the TMC module (live 2026-10-07).
        string? module = PlcProjectLifecycleArgs.TmcModuleName(tmcPath);
        if (module is null)
            return $"[Error] '{tmcPath}' contains no <Module> with a <Name> — not a PLC module class file.";
        if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), module, StringComparison.OrdinalIgnoreCase))
            return $"[Error] The XAE names a PLC instance created from a .tmc after its module ('{module}'); " +
                   $"it cannot be named '{name}' here. Omit name or pass name='{module}'.";

        if (!TryListNames(out var before, out error)) return error!;
        error = Err(PlcProjectLifecycleArgs.CheckNameFree(module, before))
            ?? SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        if (!TryCreate(module, 0, tmcPath, vInfoNull: false, out string created, out error)) return error!;
        return ReadBackNewProject(module, before, created, $"Created PLC instance '{module}' from {tmcPath}.",
            next: "link its I/O and activate the configuration (an instance from a .tmc has no PLC source to build)");
    }

    // ---- helpers ---------------------------------------------------------------------

    private static string? Err(string? reason) => reason is null ? null : $"[Error] {reason}";

    private bool TryListNames(out IReadOnlyList<string> names, out string? error)
    {
        try { names = session.ListPlcProjectNames(); error = null; return true; }
        catch (Exception ex) { names = []; error = McpErrors.EnsureError(ComCall.Describe(ex)); return false; }
    }

    private bool TryCreate(string name, int subType, string source, bool vInfoNull, out string created, out string? error)
    {
        try { created = session.CreatePlcProjectChild(name, subType, source, vInfoNull); error = null; return true; }
        catch (Exception ex) { created = string.Empty; error = McpErrors.EnsureError(ComCall.Describe(ex)); return false; }
    }

    /// <summary>Re-reads TIPC (independent of the COM object CreateChild returned) and saves.</summary>
    private string ReadBackNewProject(string expected, IReadOnlyList<string> before, string created, string message,
        string next = "twincat_build_plc to compile it")
    {
        if (!TryListNames(out var after, out var error))
            return $"{error} (after: {message})";
        int countBefore = before.Count(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase));
        int countAfter = after.Count(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase));
        if (countAfter <= countBefore)
            return $"[Error] CreateChild returned '{created}', but TIPC does not list a new '{expected}'. TIPC: {Join(after)}.";

        string save;
        try { session.SaveAll(); save = "saved"; }
        catch (Exception ex) { save = $"save failed: {ComCall.Describe(ex)} — call twincat_save_project"; }

        return $"{message} read-back: TIPC lists {Join(after)}; {save}. Next: {next}.";
    }

    private static string? FileWrittenSince(string path, DateTime startedUtc, string operation)
    {
        if (!File.Exists(path))
            return $"[Error] {operation} did not land: '{path}' does not exist after the call returned.";
        DateTime written = File.GetLastWriteTimeUtc(path);
        return written < startedUtc
            ? $"[Error] {operation} did not land: '{path}' was last written {written:yyyy-MM-dd HH:mm:ss}Z, before this call — it is an old file."
            : null;
    }

    private static string FileProof(string path) =>
        $"{new FileInfo(path).Length} bytes written {File.GetLastWriteTimeUtc(path):HH:mm:ss}Z";

    private string? SolutionDirectory()
    {
        try
        {
            string? sln = session.ActiveSolutionPath();
            return string.IsNullOrEmpty(sln) ? null : Path.GetDirectoryName(sln);
        }
        catch (Exception) { return null; } // only feeds an optional pre-check; the XAE call reports real failures
    }

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                      Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static string Join(IReadOnlyList<string> names) => names.Count == 0 ? "<none>" : string.Join(", ", names);
}
