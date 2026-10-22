using PtcXaeMcp.WinForms.TwinCat.Tree;

namespace PtcXaeMcp.WinForms.TwinCat.Apply;

/// <summary>A PLC under TIPC: its name (the TIPC child) and its source-project node (ItemType 600).</summary>
internal sealed record PlcTarget(string PlcName, string PlcPath, string ProjectNodePath);

/// <summary>Where apply_schema writes its GVL: the resolved GVL folder and the GVL name.</summary>
internal sealed record GvlTarget(PlcTarget Plc, string FolderPath, string GvlName)
{
    /// <summary>The exact tree path of the GVL: <c>TIPC^&lt;plc&gt;^&lt;project&gt;^GVLs^&lt;name&gt;</c>.</summary>
    public string GvlPath => $"{FolderPath}^{GvlName}";
}

/// <summary>
/// Resolves the PLC project and the GVL folder an apply_schema run writes into (issue #155
/// step 4, R4). Replaces the runner's solution-wide fuzzy searches (E-15): the GVL folder is an
/// exact child of the PLC's source-project node; an ambiguous or missing one is an error naming
/// the candidates — never "the first match" or the PLC root.
/// </summary>
internal sealed class ApplyTargetResolver
{
    internal const string GvlFolderName = "GVLs";

    private readonly ISysManTree _tree;
    private readonly Action<string> _warn;

    public ApplyTargetResolver(ISysManTree tree, Action<string>? warn = null)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _warn = warn ?? (_ => { });
    }

    /// <summary>
    /// Every PLC under TIPC, in tree order. The source-project node is the child with ItemType
    /// 600; a PLC without one (e.g. a binary-only PLC) is listed with an empty project path.
    /// </summary>
    public IReadOnlyList<PlcTarget> ListPlcs()
    {
        ITreeNode tipc = _tree.Lookup("TIPC")
            ?? throw new InvalidOperationException("TwinCAT tree item 'TIPC' (PLC) not found in the open solution.");

        var result = new List<PlcTarget>();
        foreach (ITreeNode plc in tipc.Children())
        {
            string plcName = plc.Name;
            string plcPath = plc.Path.Length > 0 ? plc.Path : $"TIPC^{plcName}";
            ITreeNode? project = plc.Children().FirstOrDefault(c => c.ItemType == PlcItemType.PlcApp)
                // The source-project node is not always enumerable as a child of the TIPC entry
                // (it hangs off NestedProject); its conventional path still resolves exactly.
                ?? new[] { " Project", " Projekt" }
                    .Select(suffix => _tree.Lookup($"{plcPath}^{plcName}{suffix}"))
                    .FirstOrDefault(n => n is not null && (n.ItemType == PlcItemType.PlcApp || n.ItemType < 0));
            result.Add(new PlcTarget(plcName, plcPath, project?.Path ?? string.Empty));
        }
        return result;
    }

    /// <summary>
    /// The PLC named <paramref name="requestedName"/> (the TIPC child name; "&lt;name&gt; Project"
    /// is accepted too). A name that matches no PLC is an error in strict mode. In lenient mode
    /// (the GUI's greenfield default) it falls back to the only PLC with a warning — but never
    /// guesses between several PLCs.
    /// </summary>
    public PlcTarget ResolvePlc(string requestedName, bool strict)
    {
        IReadOnlyList<PlcTarget> plcs = ListPlcs();
        string requested = StripProjectSuffix(requestedName?.Trim() ?? string.Empty);

        if (requested.Length > 0)
        {
            var exact = plcs.Where(p => string.Equals(p.PlcName, requested, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1)
                throw new InvalidOperationException(
                    $"PLC project name '{requestedName}' is ambiguous: {DescribePlcs(exact)}.");
        }

        if (plcs.Count == 0)
            throw new InvalidOperationException(
                $"No PLC project under TIPC in the open solution (requested '{requestedName}').");

        if (strict || plcs.Count > 1)
            throw new InvalidOperationException(
                $"PLC project '{requestedName}' not found. Available: {DescribePlcs(plcs)}. " +
                "Pass the exact name (tool parameter plcProject or schema twincat.plcProjectName); " +
                "there is no fallback to another PLC.");

        _warn($"PLC project '{requestedName}' not found; using the only PLC '{plcs[0].PlcName}'.");
        return plcs[0];
    }

    /// <summary>
    /// The GVL folder of <paramref name="plc"/>: the single direct child named "GVLs" of its
    /// source-project node. Missing or ambiguous is an error listing what is there (E-15).
    /// </summary>
    public string ResolveGvlFolder(PlcTarget plc)
    {
        ArgumentNullException.ThrowIfNull(plc);
        if (string.IsNullOrWhiteSpace(plc.ProjectNodePath))
            throw new InvalidOperationException(
                $"PLC '{plc.PlcName}' has no PLC source-project node (ItemType {PlcItemType.PlcApp}) under '{plc.PlcPath}'; " +
                "a GVL cannot be created there.");

        ITreeNode project = _tree.Lookup(plc.ProjectNodePath)
            ?? throw new InvalidOperationException($"PLC project node '{plc.ProjectNodePath}' not found.");

        List<ITreeNode> children = project.Children().ToList();
        var folders = children
            .Where(c => string.Equals(c.Name, GvlFolderName, StringComparison.OrdinalIgnoreCase) &&
                        (c.ItemType == PlcItemType.Folder || c.ItemType < 0))
            .ToList();

        if (folders.Count == 1)
        {
            string path = folders[0].Path;
            return path.Length > 0 ? path : $"{plc.ProjectNodePath}^{folders[0].Name}";
        }

        if (folders.Count > 1)
            throw new InvalidOperationException(
                $"GVL folder under '{plc.ProjectNodePath}' is ambiguous: " +
                string.Join(", ", folders.Select(f => $"'{f.Path}'")) + ". Nothing was written.");

        string present = children.Count == 0
            ? "<none>"
            : string.Join(", ", children.Select(c => $"'{c.Name}'"));
        throw new InvalidOperationException(
            $"No '{GvlFolderName}' folder directly under '{plc.ProjectNodePath}' (children: {present}). " +
            "There is no fallback to another folder or the PLC root. Create the folder in the PLC project, then retry.");
    }

    /// <summary><see cref="ResolvePlc"/> + <see cref="ResolveGvlFolder"/>.</summary>
    public GvlTarget ResolveGvlTarget(string requestedPlcName, string gvlName, bool strict)
    {
        if (string.IsNullOrWhiteSpace(gvlName))
            throw new InvalidOperationException("gvlName is empty.");
        PlcTarget plc = ResolvePlc(requestedPlcName, strict);
        return new GvlTarget(plc, ResolveGvlFolder(plc), gvlName.Trim());
    }

    /// <summary>EtherCAT master ItemSubType under TIID.</summary>
    internal const int EtherCatMasterSubType = 111;

    /// <summary>
    /// The EtherCAT master an apply run builds under (moved from the runner's dynamic
    /// ResolveMasterName/ResolveIoDevice, issue #155 step 7). A pinned
    /// <paramref name="targetMasterPath"/> is authoritative. Otherwise an exact name match among
    /// the TIID masters (SubType 111) wins; in <paramref name="strict"/> mode nothing else is
    /// accepted. Lenient mode (the GUI's greenfield default) keeps the historical fallbacks: the
    /// first master when no name is given or none matches, and a designation-key prefix match.
    /// </summary>
    public ITreeNode ResolveMaster(string requestedName, string? targetMasterPath, bool strict)
    {
        if (!string.IsNullOrWhiteSpace(targetMasterPath))
        {
            ITreeNode? pinned = _tree.Lookup(targetMasterPath);
            if (pinned is not null && pinned.SubType == EtherCatMasterSubType) return pinned;
            throw new InvalidOperationException(
                $"Target master path '{targetMasterPath}' not found or not an EtherCAT master (SubType {EtherCatMasterSubType}). " +
                "Fail-closed: no fuzzy fallback.");
        }

        ITreeNode tiid = _tree.Lookup("TIID")
            ?? throw new InvalidOperationException("TwinCAT tree item 'TIID' (I/O devices) not found in the open solution.");
        List<ITreeNode> masters = tiid.Children().Where(c => c.SubType == EtherCatMasterSubType).ToList();
        string selected = BrownfieldGuard.SelectMaster(requestedName ?? string.Empty,
            masters.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), strict);

        ITreeNode? exact = masters.FirstOrDefault(m => string.Equals(m.Name, selected, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        if (!BrownfieldGuard.AllowFuzzyDeviceMatch(strict))
            throw new InvalidOperationException(
                $"EtherCAT master '{selected}' not found exactly. Fail-closed: no StartsWith fuzzy match. " +
                "Pass the exact master name or targetMasterPath.");

        string key = Schema.LinkCandidates.TerminalNameKey(selected);
        ITreeNode? fuzzy = masters.FirstOrDefault(m =>
        {
            string childKey = Schema.LinkCandidates.TerminalNameKey(m.Name);
            return key.Length > 0 && childKey.Length > 0 &&
                   (childKey.StartsWith(key, StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith(childKey, StringComparison.OrdinalIgnoreCase));
        });
        if (fuzzy is not null)
        {
            _warn($"EtherCAT master '{selected}' not found exactly; using '{fuzzy.Name}' (lenient designation match).");
            return fuzzy;
        }

        throw new InvalidOperationException(
            $"EtherCAT master '{selected}' not found. Available: " +
            (masters.Count == 0 ? "<none>" : string.Join(", ", masters.Select(m => $"'{m.Name}'"))) + ".");
    }

    internal static string StripProjectSuffix(string name)
    {
        foreach (string suffix in new[] { " Project", " Projekt" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length].Trim();
        return name;
    }

    private static string DescribePlcs(IEnumerable<PlcTarget> plcs)
    {
        string list = string.Join(", ", plcs.Select(p => $"'{p.PlcName}'"));
        return list.Length == 0 ? "<none>" : list;
    }
}
