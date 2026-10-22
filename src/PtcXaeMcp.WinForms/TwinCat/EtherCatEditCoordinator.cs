namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Guard + validation + error-shaping layer over the EtherCAT edit session
/// methods, mirroring <see cref="FieldbusCoordinator"/>. All public methods
/// return either a success summary or an "[Error] …"-prefixed string; they
/// never throw.
/// </summary>
internal sealed class EtherCatEditCoordinator(ITwinCatSession session)
{
    public string AddDevice(string name, string? deviceKind)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "[Error] name is required.";
        string kind = string.IsNullOrWhiteSpace(deviceKind) ? "master" : deviceKind!.Trim().ToLowerInvariant();
        if (kind is not ("master" or "slave"))
            return $"[Error] deviceKind '{deviceKind}' is invalid — expected 'master' or 'slave'.";

        string? guard = SessionGuards.RequireOpenSolution(session);
        if (guard is not null) return guard;

        try { return session.AddEtherCatDevice(name.Trim(), kind); }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }
    }

    public string AddBox(string parentPath, string? vInfo, string? name,
        string? previousPort, string? beforeSibling, string? boxesJson)
    {
        string? pathErr = ValidateMutablePath(parentPath, nameof(parentPath));
        if (pathErr is not null) return pathErr;

        bool hasJson = !string.IsNullOrWhiteSpace(boxesJson);
        bool hasVInfo = !string.IsNullOrWhiteSpace(vInfo);
        if (hasJson == hasVInfo)
            return "[Error] Provide exactly one of 'vInfo' (single box) or 'boxesJson' "
                 + "(subtree/topology), not both and not neither.";

        IReadOnlyList<EtherCatBoxSpec> specs;
        try
        {
            specs = hasJson
                ? EtherCatSpecJson.Parse(boxesJson!)
                : [new EtherCatBoxSpec(vInfo!.Trim(),
                    string.IsNullOrWhiteSpace(name) ? null : name!.Trim(),
                    string.IsNullOrWhiteSpace(previousPort) ? null : previousPort!.Trim())];
        }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }

        if (specs.Count == 0)
            return "[Error] No EtherCAT nodes to add.";

        foreach (var spec in specs)
        {
            string? portErr = ValidatePortTree(spec);
            if (portErr is not null) return portErr;
        }

        string? guard = SessionGuards.RequireOpenSolution(session);
        if (guard is not null) return guard;

        try
        {
            var results = new List<string>();
            int total = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                // beforeSibling positions only the first top-level box; nested
                // children always append.
                string msg = session.AddEtherCatBox(parentPath, specs[i],
                    i == 0 ? beforeSibling : null);
                results.Add(msg);
                total += specs[i].NodeCount;
            }
            return specs.Count == 1
                ? results[0]
                : $"Added {total} EtherCAT nodes across {specs.Count} top-level boxes under "
                  + $"'{parentPath}'.\n" + string.Join("\n", results);
        }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }
    }

    public string RemoveBox(string treePath)
    {
        string? pathErr = ValidateMutablePath(treePath, nameof(treePath));
        if (pathErr is not null) return pathErr;

        string? guard = SessionGuards.RequireOpenSolution(session);
        if (guard is not null) return guard;

        try { return session.RemoveEtherCatBox(treePath); }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }
    }

    public string SetPreviousPort(string treePath, string port)
    {
        string? pathErr = ValidateMutablePath(treePath, nameof(treePath));
        if (pathErr is not null) return pathErr;
        if (string.IsNullOrWhiteSpace(port))
            return "[Error] port is required (e.g. A, B, C or D).";
        if (!IsPortLetter(port.Trim()))
            return $"[Error] port '{port}' is invalid — expected a single letter (A/B/C/D).";

        string? guard = SessionGuards.RequireOpenSolution(session);
        if (guard is not null) return guard;

        try { return session.SetEtherCatPreviousPort(treePath, port.Trim()); }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }
    }

    // ---- validation helpers ------------------------------------------------

    private static string? ValidateMutablePath(string? path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            return $"[Error] {paramName} is required.";
        if (path.Trim().Equals("TIID", StringComparison.OrdinalIgnoreCase))
            return $"[Error] {paramName} must be a node under TIID (not the root). "
                 + "Add boxes under an EtherCAT master or coupler.";
        return null;
    }

    private static string? ValidatePortTree(EtherCatBoxSpec spec)
    {
        if (spec.PreviousPort is not null && !IsPortLetter(spec.PreviousPort))
            return $"[Error] previousPort '{spec.PreviousPort}' for '{spec.VInfo}' is invalid "
                 + "— expected a single letter (A/B/C/D).";
        foreach (var child in spec.ChildList)
        {
            string? err = ValidatePortTree(child);
            if (err is not null) return err;
        }
        return null;
    }

    private static bool IsPortLetter(string port) =>
        port.Length == 1 && port[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
