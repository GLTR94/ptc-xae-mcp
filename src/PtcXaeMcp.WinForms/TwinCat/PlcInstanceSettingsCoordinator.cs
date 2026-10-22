namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// plc-instance-settings: settings that live on the PLC
/// instance (init values of <c>TcInitSymbol</c> variables, retain areas) or on the PLC project node
/// (.tmc/.tpy generation) instead of in the PLC source. The PLC project and its instance are found by
/// ItemType (56 under TIPC, 57 below it), never by label. Every write is proven by a fresh ProduceXml.
/// </summary>
internal sealed class PlcInstanceSettingsCoordinator(ITwinCatSession session)
{
    // ---- twincat_read_init_symbols -------------------------------------------------

    public string ReadInitSymbols(string? plcProjectName)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        if (!TryResolve(plcProjectName, out var target, out error)) return error!;
        if (!TryReadInit(target, out var symbols, out error)) return error!;

        if (symbols.Count == 0)
            return $"PLC instance '{target.InstancePath}': no init symbols. A variable becomes one with " +
                   "{attribute 'TcInitSymbol'} in its declaration (twincat_plc_edit_entries) and a build.";

        return $"PLC instance '{target.InstancePath}': {symbols.Count} init symbol(s) (name : type = value):" + Environment.NewLine +
               string.Join(Environment.NewLine, symbols.Select(s => $"  {s.Name} : {s.Type ?? "?"} = {s.Value}"));
    }

    // ---- twincat_write_init_symbol -------------------------------------------------

    /// <summary>
    /// Sets one init value. Live (2026-10-08) ConsumeXml silently ignores an unknown or non-init symbol
    /// and an unparseable value, and WRAPS an out-of-range number (DINT 99999999999 → 1215752191) — so the
    /// symbol must already be listed, and a read-back that differs restores the old value and is an error.
    /// </summary>
    public string WriteInitSymbol(string symbol, string value, string? plcProjectName)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        if (string.IsNullOrWhiteSpace(symbol)) return "[Error] symbol is required (e.g. 'GVL_Params.nSpeed').";
        if (value is null || value.Trim().Length == 0) return "[Error] value is required.";
        if (!TryResolve(plcProjectName, out var target, out error)) return error!;
        if (!TryReadInit(target, out var before, out error)) return error!;

        var current = before.FirstOrDefault(s => string.Equals(s.Name, symbol.Trim(), StringComparison.OrdinalIgnoreCase));
        if (current is null)
            return $"[Error] '{symbol}' is not an init symbol of '{target.InstancePath}'. Init symbols: {Join(before.Select(s => s.Name))}. " +
                   "A variable becomes one with {attribute 'TcInitSymbol'} in its declaration and a build.";

        error = SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        string requested = value.Trim();
        if (!TryConsume(target.InstancePath, PlcInstanceSettingsXml.BuildInitSymbolXml(current.Name, requested), out error)) return error!;
        if (!TryReadInit(target, out var after, out error)) return $"{error} (after writing '{current.Name}')";

        var stored = after.FirstOrDefault(s => string.Equals(s.Name, current.Name, StringComparison.OrdinalIgnoreCase));
        if (stored is null)
            return $"[Error] After the write '{current.Name}' is no longer listed as an init symbol of '{target.InstancePath}'.";
        if (!PlcInstanceSettingsXml.ValuesEqual(requested, stored.Value))
        {
            string restore = PlcInstanceSettingsXml.ValuesEqual(current.Value, stored.Value)
                ? "the old value is unchanged"
                : RestoreInit(target, current);
            return $"[Error] The XAE did not store '{requested}' for {current.Name} ({current.Type ?? "?"}): read-back is '{stored.Value}' " +
                   $"(value not valid for the type or out of range); {restore}.";
        }

        string save = Save();
        return $"Set init value {current.Name} ({current.Type ?? "?"}) = {stored.Value} (was {current.Value}) on '{target.InstancePath}'. " +
               $"read-back: {stored.Value}; {save}. The value is stored in the TwinCAT configuration (.tsproj), not in the PLC " +
               "boot project — it takes effect with the next Activate Configuration (twincat_activate_config) and PLC restart; this tool does not activate.";
    }

    // ---- twincat_read_retain_config ------------------------------------------------

    /// <summary>Retain areas and their symbols — read-only (Infosys: "rein lesend").</summary>
    public string ReadRetainConfig(string? plcProjectName)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        if (!TryResolve(plcProjectName, out var target, out error)) return error!;
        if (!TryProduce(target.InstancePath, out string xml, out error)) return error!;

        IReadOnlyList<RetainArea>? areas;
        IReadOnlyList<string> mappings;
        try
        {
            areas = PlcInstanceSettingsXml.ParseRetainAreas(xml);
            mappings = PlcInstanceSettingsXml.ParseRetainMappings(xml);
        }
        catch (Exception ex) { return McpErrors.EnsureError(ex.Message); }
        if (areas is null) return NotBuilt(target);

        var lines = new List<string>();
        int symbols = areas.Sum(a => a.Symbols.Count);
        lines.Add(areas.Count == 0
            ? $"PLC instance '{target.InstancePath}': no retain area (no VAR RETAIN variable in the built project)."
            : $"PLC instance '{target.InstancePath}': {areas.Count} retain area(s), {symbols} symbol(s). Read-only — retain variables are declared in the source (VAR RETAIN).");
        foreach (var a in areas)
        {
            lines.Add($"  area '{a.Name}' (AreaNo {Show(a.AreaNo)}, {Show(a.ByteSize)} bytes):");
            foreach (var s in a.Symbols)
                lines.Add($"    {s.Name} : {s.Type ?? "?"} (BitSize {Show(s.BitSize)}, BitOffs {Show(s.BitOffs)})");
        }
        lines.Add(mappings.Count == 0
            ? "  RetainDataMappings: none"
            : $"  RetainDataMappings ({mappings.Count}):");
        lines.AddRange(mappings.Select(m => "    " + m));
        return string.Join(Environment.NewLine, lines);
    }

    // ---- twincat_get/set_type_file_generation ----------------------------------------

    public string GetTypeFileGeneration(string? plcProjectName)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        if (!TryResolveProject(plcProjectName, out var target, out error)) return error!;
        if (!TryReadFlags(target, out var flags, out error)) return error!;
        return $"PLC project '{target.ProjectPath}': {DescribeFlags(flags)}.";
    }

    /// <summary>
    /// Live (2026-10-08) the XAE couples the flags: TpyFileCopy=true also sets TmcFileCopy, TmcFileCopy=false
    /// also clears TpyFileCopy. So tmc=false with tpy=true is refused, TMC is sent before TPY, and the result
    /// reports the flags read back — not the ones requested.
    /// </summary>
    public string SetTypeFileGeneration(bool? tmc, bool? tpy, string? plcProjectName)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        if (tmc is null && tpy is null) return "[Error] Pass tmc and/or tpy.";
        if (tmc == false && tpy == true)
            return "[Error] tmc=false with tpy=true is not possible: the XAE always generates the .tmc when the .tpy is generated.";
        if (!TryResolveProject(plcProjectName, out var target, out error)) return error!;
        if (!TryReadFlags(target, out var before, out error)) return error!;

        error = SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;
        if (!TryConsume(target.ProjectPath, PlcInstanceSettingsXml.BuildTypeFileXml(tmc, tpy), out error)) return error!;
        if (!TryReadFlags(target, out var after, out error)) return $"{error} (after the write)";

        // What the coupling makes of the request: tpy=true implies tmc=true; tmc=false implies tpy=false.
        bool expectTmc = tmc ?? (tpy == true || before.Tmc);
        bool expectTpy = tpy ?? (tmc == false ? false : before.Tpy);
        if (after.Tmc != expectTmc || after.Tpy != expectTpy)
            return $"[Error] The XAE did not apply the setting: requested {DescribeRequest(tmc, tpy)}, read-back {DescribeFlags(after)} (before: {DescribeFlags(before)}).";

        string save = Save();
        string coupled = (tmc is null && after.Tmc != before.Tmc) || (tpy is null && after.Tpy != before.Tpy)
            ? " (the XAE changed the other flag with it)"
            : string.Empty;
        return $"PLC project '{target.ProjectPath}': set {DescribeRequest(tmc, tpy)}. read-back: {DescribeFlags(after)}{coupled}; {save}. " +
               "Takes effect at the next build (twincat_build_plc).";
    }

    private bool TryResolveProject(string? plcProjectName, out PlcTarget target, out string? error) =>
        TryResolve(plcProjectName, out target, out error, requireInstance: false);

    private bool TryReadFlags(PlcTarget target, out TypeFileFlags flags, out string? error)
    {
        flags = new TypeFileFlags(false, false);
        if (!TryProduce(target.ProjectPath, out string xml, out error)) return false;
        TypeFileFlags? parsed;
        try { parsed = PlcInstanceSettingsXml.ParseTypeFileFlags(xml); }
        catch (Exception ex) { error = McpErrors.EnsureError(ex.Message); return false; }
        if (parsed is null)
        {
            error = $"[Error] '{target.ProjectPath}' has no PlcProjectDef with TmcFileCopy/TpyFileCopy — not a PLC project with source (e.g. an instance created from a .tmc).";
            return false;
        }
        flags = parsed;
        return true;
    }

    private static string DescribeFlags(TypeFileFlags f) =>
        $"TmcFileCopy={(f.Tmc ? "true" : "false")}, TpyFileCopy={(f.Tpy ? "true" : "false")}";

    private static string DescribeRequest(bool? tmc, bool? tpy) => string.Join(", ",
        new[] { tmc is null ? null : $"tmc={(tmc.Value ? "true" : "false")}", tpy is null ? null : $"tpy={(tpy.Value ? "true" : "false")}" }
            .Where(s => s is not null));

    private static string Show<T>(T? value) where T : struct =>
        value is null ? "?" : Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "?";

    private string RestoreInit(PlcTarget target, InitSymbolValue old)
    {
        if (!TryConsume(target.InstancePath, PlcInstanceSettingsXml.BuildInitSymbolXml(old.Name, old.Value), out var error))
            return $"restoring '{old.Value}' failed: {error}";
        if (!TryReadInit(target, out var check, out error)) return $"restore not verified: {error}";
        var now = check.FirstOrDefault(s => string.Equals(s.Name, old.Name, StringComparison.OrdinalIgnoreCase));
        return now is not null && PlcInstanceSettingsXml.ValuesEqual(old.Value, now.Value)
            ? $"restored the old value '{old.Value}'"
            : $"restoring '{old.Value}' did not land (now '{now?.Value ?? "<missing>"}') — check twincat_read_init_symbols";
    }

    private bool TryReadInit(PlcTarget target, out IReadOnlyList<InitSymbolValue> symbols, out string? error)
    {
        symbols = [];
        if (!TryProduce(target.InstancePath, out string xml, out error)) return false;
        IReadOnlyList<InitSymbolValue>? parsed;
        try { parsed = PlcInstanceSettingsXml.ParseInitSymbols(xml); }
        catch (Exception ex) { error = McpErrors.EnsureError(ex.Message); return false; }
        if (parsed is null) { error = NotBuilt(target); return false; }
        symbols = parsed;
        return true;
    }

    // ---- shared ------------------------------------------------------------------------

    /// <summary>The PLC project node (TIPC child) and its instance node.</summary>
    internal sealed record PlcTarget(string ProjectName, string ProjectPath, string InstancePath);

    /// <summary>
    /// Finds the PLC project (named, or the only one) under TIPC and its instance child (ItemType 57).
    /// </summary>
    private bool TryResolve(string? plcProjectName, out PlcTarget target, out string? error, bool requireInstance = true)
    {
        target = new PlcTarget(string.Empty, string.Empty, string.Empty);
        IReadOnlyList<TreeChildInfo> projects;
        try
        {
            projects = session.ListTreeChildren("TIPC")
                .Where(c => c.ItemType == PlcInstanceSettingsXml.PlcProjectItemType).ToList();
        }
        catch (Exception ex) { error = McpErrors.EnsureError(ComCall.Describe(ex)); return false; }

        TreeChildInfo? project;
        if (string.IsNullOrWhiteSpace(plcProjectName))
        {
            if (projects.Count != 1)
            {
                error = projects.Count == 0
                    ? "[Error] No PLC project under TIPC."
                    : $"[Error] {projects.Count} PLC projects under TIPC ({Join(projects.Select(p => p.Name))}) — pass plcProjectName.";
                return false;
            }
            project = projects[0];
        }
        else
        {
            project = projects.FirstOrDefault(p => string.Equals(p.Name, plcProjectName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                error = $"[Error] No PLC project '{plcProjectName}' under TIPC. Present: {Join(projects.Select(p => p.Name))}.";
                return false;
            }
        }

        string projectPath = $"TIPC^{project.Name}";
        if (!requireInstance)
        {
            target = new PlcTarget(project.Name, projectPath, string.Empty);
            error = null;
            return true;
        }
        IReadOnlyList<TreeChildInfo> children;
        try { children = session.ListTreeChildren(projectPath); }
        catch (Exception ex) { error = McpErrors.EnsureError(ComCall.Describe(ex)); return false; }
        var instance = children.FirstOrDefault(c => c.ItemType == PlcInstanceSettingsXml.PlcInstanceItemType);
        if (instance is null)
        {
            error = $"[Error] PLC project '{project.Name}' has no PLC instance (ItemType {PlcInstanceSettingsXml.PlcInstanceItemType}) — build it first (twincat_build_plc).";
            return false;
        }

        target = new PlcTarget(project.Name, projectPath, $"{projectPath}^{instance.Name}");
        error = null;
        return true;
    }

    private bool TryProduce(string path, out string xml, out string? error)
    {
        try { xml = session.ProduceTreeItemXml(path); error = null; return true; }
        catch (Exception ex) { xml = string.Empty; error = McpErrors.EnsureError(ComCall.Describe(ex)); return false; }
    }

    private bool TryConsume(string path, string xml, out string? error)
    {
        try { session.ConsumeTreeItemXml(path, xml); error = null; return true; }
        catch (Exception ex) { error = McpErrors.EnsureError(ComCall.Describe(ex)); return false; }
    }

    private string Save()
    {
        try { session.SaveAll(); return "saved"; }
        catch (Exception ex) { return $"save failed: {ComCall.Describe(ex)} — call twincat_save_project"; }
    }

    private static string NotBuilt(PlcTarget target) =>
        $"[Error] PLC instance '{target.InstancePath}' carries no module data (TcModuleInstance) — build the PLC project first (twincat_build_plc).";

    private static string Join(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? "<none>" : string.Join(", ", list);
    }
}
