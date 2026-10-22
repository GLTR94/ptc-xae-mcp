using System.Globalization;
namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // ===================================================================
    // Safety recon (read-only spike). Beckhoff documents the TwinCAT
    // Safety project as NOT addressable through the Automation Interface —
    // empirically false (the Safety Motion Designer drives it). This
    // diagnostic maps exactly HOW it is reachable on the live fixture so a
    // real safety tool family can be designed against observed facts, not
    // guesses:
    //   1. enumerate dte.Solution.Projects — the safety .splcproj is a
    //      SEPARATE VS project, not a child of the PLC system manager, so
    //      LookupTreeItem on the PLC sysman never sees it;
    //   2. for each project's COM .Object, probe (a) whether the Object is
    //      itself a tree item (the project root node) and (b) which
    //      ITcSysManager root shortcuts resolve on it — standard roots
    //      anchor the output, safety-candidate shortcuts are guesses we
    //      report only when they actually resolve;
    //   3. deep-walk any safety-relevant root (project FullName ends in
    //      .splcproj, or a node/shortcut name contains "saf"), emitting
    //      name/ItemType/ItemSubType/childCount/path + ProduceXml(false).
    // Pure reconnaissance: no mutation, no save.
    // ===================================================================

    // Root shortcuts probed on every project Object. The first row is the
    // documented/known set (anchors what a 'normal' sysmanager exposes); the
    // rest are safety-candidate guesses — only resolving ones are reported.
    private static readonly string[] SafetyReconRootProbes =
    {
        "TIRC", "TIPC", "TINC", "TICC", "TIAC", "TIID",
        "TIRS", "TIRR", "TIPP", "TIVD", "TCOM", "TIAX",
        "TXPR", "TISA", "TISD", "TISP", "TISC",
        "TISAFE", "TISF", "TISG", "TISH", "SAFETY", "TwinSAFE",
    };

    public string SafetyRecon() => ComBusyRetry.Run(() =>
    {
        var sb = new System.Text.StringBuilder();
        dynamic dte = GetActiveTcDte();

        sb.AppendLine("=== Safety recon (read-only) ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Root shortcuts probed per project: {string.Join(", ", SafetyReconRootProbes)}");
        sb.AppendLine();

        dynamic? projects;
        try { projects = dte.Solution.Projects; }
        catch (Exception ex) when (ComCall.NotBusy(ex)) { sb.AppendLine(CultureInfo.InvariantCulture, $"<dte.Solution.Projects threw: {ex.Message}>"); return sb.ToString(); }
        if (projects is null) { sb.AppendLine("<no Solution.Projects>"); return sb.ToString(); }

        int count = 0; try { count = (int)projects.Count; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        sb.AppendLine(CultureInfo.InvariantCulture, $"[Solution.Projects] count={count}");

        var safetyTargets = new List<(string Label, object Node)>();

        for (int i = 1; i <= count; i++)
        {
            dynamic? project;
            try { project = projects.Item(i); }
            catch (Exception ex) when (ComCall.NotBusy(ex)) { sb.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] <Item threw: {ex.Message}>"); continue; }
            if (project is null) { sb.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] <null>"); continue; }

            string pname = SafeStr(() => (string)project.Name);
            string pkind = SafeStr(() => (string)project.Kind);
            string pfull = SafeStr(() => (string)project.FullName);
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] Name='{pname}'");
            sb.AppendLine(CultureInfo.InvariantCulture, $"       Kind={pkind}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"       FullName='{pfull}'");

            bool isSafetyProj =
                pfull.EndsWith(".splcproj", StringComparison.OrdinalIgnoreCase) ||
                pname.IndexOf("saf", StringComparison.OrdinalIgnoreCase) >= 0;

            object? obj = null;
            try { obj = project.Object as object; }
            catch (Exception ex) when (ComCall.NotBusy(ex)) { sb.AppendLine(CultureInfo.InvariantCulture, $"       Object: <threw: {ex.Message}>"); }
            if (obj is null) { sb.AppendLine("       Object: <null>"); continue; }
            sb.AppendLine(CultureInfo.InvariantCulture, $"       Object CLR type: {obj.GetType().FullName}");

            // Is project.Object itself a tree item (the project root node)?
            bool objIsTreeItem = false;
            string objName = SafeStr(() => (string)((dynamic)obj).Name, ref objIsTreeItem);
            int objItemType = SafeInt(() => (int)((dynamic)obj).ItemType, ref objIsTreeItem);
            int objChildCount = SafeInt(() => (int)((dynamic)obj).ChildCount, ref objIsTreeItem);
            if (objIsTreeItem)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"       Object-as-treeitem: name='{objName}' itemType={objItemType} childCount={objChildCount}");
                if (isSafetyProj && objChildCount > 0)
                    safetyTargets.Add(($"project[{i}].Object '{objName}'", obj));
            }

            // Probe ITcSysManager root shortcuts on this Object.
            foreach (var root in SafetyReconRootProbes)
            {
                dynamic? node = LookupTreeItemOrNull(obj, root);
                if (node is null) continue;
                string nname = SafeStr(() => (string)node.Name);
                int it = -1; try { it = (int)node.ItemType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                int cc = 0; try { cc = (int)node.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                sb.AppendLine(CultureInfo.InvariantCulture, $"       LookupTreeItem('{root}') -> '{nname}' itemType={it} childCount={cc}");

                bool rootLooksSafety =
                    root.IndexOf("saf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nname.IndexOf("saf", StringComparison.OrdinalIgnoreCase) >= 0;
                if (rootLooksSafety && cc > 0)
                    safetyTargets.Add(($"project[{i}] LookupTreeItem('{root}')='{nname}'", (object)node));
            }
        }

        sb.AppendLine();
        if (safetyTargets.Count == 0)
        {
            sb.AppendLine("[Subtree dumps] No safety-relevant tree root found via Projects/Object/shortcut probes.");
            sb.AppendLine("  If the safety project IS open, its AI surface is not a sysmanager shortcut —");
            sb.AppendLine("  use the per-project 'Object CLR type' rows above as the entry point to drive next.");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Subtree dumps] {safetyTargets.Count} safety-relevant root(s):");
            foreach (var (label, node) in safetyTargets)
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"--- {label} ---");
                DumpSafetyNode(node, sb, depth: 0, maxDepth: 5);
            }
        }
        return sb.ToString();
    }, OpenTimeout);

    private static string SafeStr(Func<string?> get)
    {
        try { return get() ?? "<null>"; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return "<?>"; }
    }

    // Overload that also records "this property existed" so callers can tell a
    // real tree item (has Name/ItemType/ChildCount) from a plain sysmanager.
    private static string SafeStr(Func<string?> get, ref bool any)
    {
        try { var v = get() ?? "<null>"; any = true; return v; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return "<?>"; }
    }

    private static int SafeInt(Func<int> get, ref bool any)
    {
        try { var v = get(); any = true; return v; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return -1; }
    }

    private static void DumpSafetyNode(object nodeObj, System.Text.StringBuilder sb, int depth, int maxDepth)
    {
        dynamic node = nodeObj;
        string name = "<?>"; try { name = (string)node.Name; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        string path = name; try { path = (string)node.PathName; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        int itemType = -1; try { itemType = (int)node.ItemType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        int subType = -1; try { subType = (int)node.ItemSubType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { try { subType = (int)node.SubType; } catch (Exception comEx2) when (ComCall.NotBusy(comEx2)) { } }
        int childCount = 0; try { childCount = (int)node.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        string indent = new string(' ', depth * 2);
        sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}- '{name}' itemType={itemType} subType={subType} children={childCount} path='{path}'");

        string? xml = null;
        try { xml = (string)node.ProduceXml(false); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        if (!string.IsNullOrEmpty(xml))
        {
            const int cap = 6000;
            string body = xml!.Length > cap ? xml[..cap] + $"\n…[truncated, {xml.Length} chars total]" : xml;
            sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}  ProduceXml:");
            foreach (var line in body.Split('\n'))
                sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}  | {line.TrimEnd('\r')}");
        }

        if (depth >= maxDepth) return;
        for (int i = 1; i <= childCount; i++)
        {
            dynamic? child = null;
            try
            {
                try { child = node.Child(i); }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (child is not null) DumpSafetyNode((object)child, sb, depth + 1, maxDepth);
            }
            finally
            {
                if (child is not null)
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(child); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
            }
        }
    }

    // ===================================================================
    // Safety WRITE PROBE. Default mode is READ-ONLY: it resolves the
    // safety root (TISC) and the itemType-800 "Safety Project" node and
    // dumps ProduceXml(false) AND ProduceXml(true) — the key question is
    // whether the alias-device list appears in the COM tree at all (the
    // recon showed ProduceXml(false) on the 800 node is only project
    // metadata). Only when attemptWrite=true does it MUTATE in-memory:
    // a CreateChild subtype sweep + a ConsumeXml round-trip of the node's
    // own xml, each attempt's HRESULT captured, every created node rolled
    // back. NEVER calls File.SaveAll — nothing is persisted.
    // ===================================================================
    public string SafetyProbeCreateAliasDevice(
        string? targetPath, string? aliasName, string? subTypesCsv, bool attemptWrite) =>
        ComBusyRetry.Run(() =>
    {
        var sb = new System.Text.StringBuilder();
        string name = string.IsNullOrWhiteSpace(aliasName) ? "zz_AItest_Probe" : aliasName!.Trim();
        int[] subTypes = ParseSubTypes(subTypesCsv);

        dynamic dte = GetActiveTcDte();
        object? obj = ResolveSafetyProjectObjectOrNull(dte);
        if (obj is null) { sb.AppendLine("No project Object exposing LookupTreeItem(\"TISC\") found."); return sb.ToString(); }

        sb.AppendLine(CultureInfo.InvariantCulture, $"=== Safety write-probe (attemptWrite={attemptWrite}; NEVER saves) ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Probe node name: '{name}'");
        if (attemptWrite) sb.AppendLine(CultureInfo.InvariantCulture, $"CreateChild subType sweep: {string.Join(", ", subTypes)}");
        sb.AppendLine();

        var targets = new List<(string Label, object Node)>();
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            dynamic? t = LookupTreeItemOrNull(obj, targetPath!);
            if (t is not null) targets.Add(($"explicit '{targetPath}'", (object)t));
            else sb.AppendLine(CultureInfo.InvariantCulture, $"(explicit targetPath '{targetPath}' did not resolve)");
        }

        dynamic? root = LookupTreeItemOrNull(obj, "TISC");
        if (root is null) { sb.AppendLine("TISC root not resolvable on the project Object."); return sb.ToString(); }
        targets.Add(($"TISC (SAFETY root, itemType {SafetyItemType.ConfigurationRoot})", (object)root));
        dynamic? proj = FindChildByItemType(root, SafetyItemType.SafetyProject);
        if (proj is not null) targets.Add(($"TISC^<Safety Project> (itemType {SafetyItemType.SafetyProject})", (object)proj));
        else sb.AppendLine(CultureInfo.InvariantCulture, $"(no itemType-{SafetyItemType.SafetyProject} 'Safety Project' child found under TISC)");

        foreach (var (label, node) in targets)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"--- TARGET: {label} ---");
            DescribeNode(node, sb);
            DumpProduce(node, false, sb);
            DumpProduce(node, true, sb);

            if (!attemptWrite) { sb.AppendLine(); continue; }

            sb.AppendLine("  CreateChild sweep:");
            foreach (int st in subTypes)
            {
                string probeName = $"{name}_{st}";
                try
                {
                    object? raw = ((dynamic)node).CreateChild(probeName, st, string.Empty, null);
                    // Post-mutation reads in their own busy scope: a busy XAE here must not replay
                    // the whole probe (and its CreateChild) through the outer retry (issue #153).
                    dynamic? created = UnwrapSingle(raw) ?? AfterMutation<object?>(() => FindChildByName((dynamic)node, probeName));
                    if (created is not null)
                    {
                        int cit = AfterMutation(() => ComCall.ReadProp(() => (int)created.ItemType, -1));
                        string cpath = AfterMutation(() => SafeStr(() => (string)created.PathName));
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    subType={st}: CREATED -> itemType={cit} path='{cpath}'  [rolling back]");
                        RollbackChild((dynamic)node, probeName, created, sb);
                    }
                    else
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    subType={st}: returned null (no node, no throw)");
                    }
                }
                catch (Exception ex) when (ComCall.NotBusy(ex)) {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    subType={st}: {DescribeComError(ex)}");
                }
            }

            try
            {
                string own = (string)((dynamic)node).ProduceXml(false);
                ((dynamic)node).ConsumeXml(own);
                sb.AppendLine("  ConsumeXml(own ProduceXml(false)): ACCEPTED (no throw)");
            }
            catch (Exception ex) when (ComCall.NotBusy(ex)) {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  ConsumeXml(own): {DescribeComError(ex)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("NOTE: File.SaveAll was NOT called; any created node was rolled back.");
        return sb.ToString();
    }, OpenTimeout);

    // ===================================================================
    // Safety production tools — list / read_io_map / read_program /
    // add_alias_device / link_alias_device.
    // ===================================================================

    // The .splcproj path is the only COM fact these read tools need; everything else is parsed from
    // disk by COM-free code (SafetyProjectSnapshot) so it is unit-tested on the Beckhoff templates.
    private string? SafetyProjectPath() =>
        ComBusyRetry.Run(() => ResolveSafetyProjectPathOrNull(GetActiveTcDte()), OpenTimeout);

    private const string NoSafetyProject = "No TwinCAT Safety project (.splcproj) found in the open solution.";

    public string SafetyList()
    {
        string? splcprojPath = SafetyProjectPath();
        if (splcprojPath is null) return NoSafetyProject;
        var snap = SafetyProjectSnapshot.Load(splcprojPath);
        var proj = snap.Structure;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Safety project: {Path.GetFileName(splcprojPath)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"NextSdsId: {(proj.NextSdsId is int n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(no <NextSdsId> element)")}");
        sb.AppendLine();

        sb.AppendLine(CultureInfo.InvariantCulture, $"Groups ({proj.Groups.Count}):");
        foreach (var g in proj.Groups) sb.AppendLine(CultureInfo.InvariantCulture, $"  {g}");

        sb.AppendLine(CultureInfo.InvariantCulture, $"User FBs ({proj.UserFBs.Count}):");
        foreach (var f in proj.UserFBs) sb.AppendLine(CultureInfo.InvariantCulture, $"  {f}");

        sb.AppendLine(CultureInfo.InvariantCulture, $"Alias devices ({snap.AliasDevices.Count}):");
        foreach (var entry in snap.AliasDevices)
        {
            string folder = entry.Kind switch
            {
                AliasDeviceKind.Hardware => "folder:hardware",
                AliasDeviceKind.ProjectVariable => "folder:project-variable",
                AliasDeviceKind.SystemVariable => "folder:system-variable",
                _ => "folder:-",
            };
            if (entry.Info is { } dev)
            {
                string sds = dev.SdsId is int id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(none)";
                string type = dev.Type is int t
                    ? (t == SafetySdsAuthor.StandardAliasDeviceType
                        ? $"standard {SafetySdsAuthor.DescribeSubType(dev.SubType)}"
                        : $"Type {t}/SubType {dev.SubType}")
                    : "(no AliasDeviceType)";
                string io = dev.IoName is not null ? $"  IO={dev.IoName}" : string.Empty;
                string mode = dev.LinkingMode is not null ? $"  linking={dev.LinkingMode}" : string.Empty;
                string signal = dev.SignalName is not null ? "  [signal-name convention]" : string.Empty;
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {dev.Name}  SDSID={sds}  {type}{io}{mode}{signal}  ({folder}, {entry.IncludePath})");
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {entry.IncludePath}  ({folder}; {entry.Problem})");
            }
        }
        return sb.ToString();
    }

    public string SafetyReadIoMap()
    {
        string? splcprojPath = SafetyProjectPath();
        return splcprojPath is null
            ? NoSafetyProject
            : SafetyIoMapReport.Render(SafetyProjectSnapshot.Load(splcprojPath));
    }

    public string SafetyReadProgram() => ComBusyRetry.Run(() =>
    {
        dynamic dte = GetActiveTcDte();
        string? splcprojPath = ResolveSafetyProjectPathOrNull(dte);
        if (splcprojPath is null)
            return "No TwinCAT Safety project (.splcproj) found in the open solution.";

        string dir = Path.GetDirectoryName(splcprojPath)!;
        string xml = File.ReadAllText(splcprojPath, System.Text.Encoding.UTF8);
        SplcprojStructure proj = SafetySplcprojParser.Parse(xml);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Safety FBD Program ===");

        if (proj.Groups.Count == 0)
        {
            sb.AppendLine("No .sal group files found in .splcproj.");
            return sb.ToString();
        }

        foreach (var salRel in proj.Groups)
        {
            string salPath = Path.Combine(dir, salRel);
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"--- Group: {Path.GetFileNameWithoutExtension(salRel)} ({salRel}) ---");

            if (!File.Exists(salPath))
            {
                sb.AppendLine("  (file not found)");
                continue;
            }

            string salXml = File.ReadAllText(salPath, System.Text.Encoding.UTF8);
            var groups = SafetySalParser.ParseGroups(salXml);

            if (groups.Count == 0 || groups.All(g => g.Networks.Count == 0))
            {
                sb.AppendLine("  (no networks parsed — schema may differ from expected format)");
                continue;
            }

            int netIdx = 1;
            foreach (var grp in groups)
            {
                foreach (var net in grp.Networks)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  Network {netIdx++}{(net.Id is not null ? $" (id={net.Id})" : "")}:");

                    foreach (var fb in net.Instances)
                    {
                        string infer = fb.Inferred ? " [inferred]" : "";
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    FB  {fb.InstanceName}  : {fb.TypeName ?? "??"}{infer}");
                        foreach (var p in fb.Params)
                            sb.AppendLine(CultureInfo.InvariantCulture, $"      param {p.Name} = {p.Value}");
                    }

                    foreach (var conn in net.Connections)
                    {
                        string infer = conn.Inferred ? " [inferred]" : "";
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    -> {conn.SourceMoniker} => {conn.TargetMoniker}{infer}");
                    }
                }
            }
        }

        return sb.ToString();
    }, OpenTimeout);

    public string SafetyAddAliasDevice(string signalName, int subType) =>
        SafetyAddAliasDevice(signalName, subType, group: null);

    // Issue #153 (D-8/D-16): validation + the file transaction live in the COM-free
    // SafetyAliasDeviceWriter (temp files, rollback, read-back); only the path lookup and the reload
    // touch COM, each in its own busy scope so a busy XAE during the reload can never replay the
    // file writes (the old single ComBusyRetry scope would have answered "already exists").
    public string SafetyAddAliasDevice(string signalName, int subType, string? group)
    {
        if (SafetyAliasRules.Validate(signalName, subType) is { } invalid)
            throw new InvalidOperationException(invalid.StartsWith("[Error] ", StringComparison.Ordinal) ? invalid[8..] : invalid);

        string splcprojPath = SafetyProjectPath()
            ?? throw new InvalidOperationException(NoSafetyProject);

        SafetyAliasAddResult result = SafetyAliasDeviceWriter.Add(splcprojPath, signalName, subType, group);
        string reload = ReloadSafetyProject(splcprojPath);
        return SafetyAliasDeviceWriter.Describe(result) + $"  Reload: {reload}";
    }

    public string SafetyLinkAliasDevice(string aliasDevicePath, string plcSymbolPath) =>
        ComBusyRetry.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(aliasDevicePath))
            throw new ArgumentException("aliasDevicePath is required.", nameof(aliasDevicePath));
        if (string.IsNullOrWhiteSpace(plcSymbolPath))
            throw new ArgumentException("plcSymbolPath is required.", nameof(plcSymbolPath));

        dynamic dte = GetActiveTcDte();
        object? obj = ResolveSafetyProjectObjectOrNull(dte);
        if (obj is null)
            return "No TwinCAT Safety project with TISC root found in the open solution.";

        // The alias device's EL1918 connection-image symbol only exists after the
        // safety project is mapped/built. Try to resolve the symbol first to give
        // a clear precondition error rather than a cryptic COM failure.
        dynamic? sysman = null;
        try { sysman = GetActiveTcSysManager(); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        if (sysman is not null)
        {
            dynamic? aliasNode = LookupTreeItemOrNull(sysman, aliasDevicePath);
            if (aliasNode is null)
                return $"Precondition failed: alias device symbol '{aliasDevicePath}' not found. " +
                       $"The safety project may need to be mapped/built first.";
        }

        // Attempt the link via ITcSysManager.LinkVariables.
        try
        {
            if (sysman is not null)
                sysman.LinkVariables(aliasDevicePath, plcSymbolPath);
            else
                ((dynamic)obj).LinkVariables(aliasDevicePath, plcSymbolPath);

            return $"Linked '{aliasDevicePath}' → '{plcSymbolPath}' (LinkVariables succeeded).";
        }
        catch (Exception ex) when (ComCall.NotBusy(ex)) {
            return $"LinkVariables failed: {DescribeComError(ex)}\n" +
                   $"  alias: {aliasDevicePath}\n  plc: {plcSymbolPath}\n" +
                   $"  Ensure the safety project is mapped and the target symbol exists in the standard PLC.";
        }
    }, OpenTimeout);

    // ── helpers for production safety tools ───────────────────────────────────

    private static string? ResolveSafetyProjectPathOrNull(dynamic dte)
    {
        try
        {
            dynamic projects = dte.Solution.Projects;
            int count = (int)projects.Count;
            for (int i = 1; i <= count; i++)
            {
                dynamic? project; try { project = projects.Item(i); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (project is null) continue;
                string fullName;
                try { fullName = (string)project.FullName; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (fullName.EndsWith(".splcproj", StringComparison.OrdinalIgnoreCase))
                    return fullName;
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        return null;
    }

    private dynamic GetActiveTcSysManager()
    {
        dynamic dte = GetActiveTcDte();
        dynamic projects = dte.Solution.Projects;
        int count = (int)projects.Count;
        for (int i = 1; i <= count; i++)
        {
            dynamic? project; try { project = projects.Item(i); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
            if (project is null) continue;
            object? obj; try { obj = project.Object as object; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
            if (obj is null) continue;
            if (LookupTreeItemOrNull(obj, "TIPC") is not null) return (dynamic)obj;
        }
        throw new InvalidOperationException("No TwinCAT system manager (TIPC) found.");
    }

    // D-16: never remove the project before its file is known to load. Preflight parses the
    // .splcproj and checks every registered file exists; only then Remove + AddFromFile, each in its
    // own busy scope (a busy reject means the call did not run, so retrying one step is safe), and
    // the re-add is verified by re-resolving the project. If AddFromFile still fails the result says
    // exactly what state the in-memory solution is in and how to recover — the solution is not saved.
    // NOTE: not verified against a live TwinSAFE project (none can be created on the dev VM); the
    // tool description marks the reload as experimental.
    private string ReloadSafetyProject(string splcprojPath)
    {
        var snap = SafetyProjectSnapshot.Load(splcprojPath);
        var missing = snap.AliasDevices.Where(a => a.Info is null).Select(a => a.IncludePath)
            .Concat(snap.MissingGroups).ToList();
        if (missing.Count > 0)
            return "not attempted — the .splcproj references files that cannot be read " +
                   $"({string.Join(", ", missing)}); reopen the solution after fixing them.";

        object? project = ComBusyRetry.Run(() => FindSolutionProject(GetActiveTcDte(), splcprojPath), OpenTimeout);
        if (project is null)
            return "not attempted — the safety project is not a top-level project of the open solution; " +
                   "close and reopen the solution (without saving) to pick up the new alias device.";

        try { ComBusyRetry.Run(() => { GetActiveTcDte().Solution.Remove((dynamic)project); }, OpenTimeout); }
        catch (Exception ex) when (ComCall.NotBusy(ex))
        {
            return $"not attempted — removing the project for the reload failed ({ex.Message}); " +
                   "the project is unchanged in the solution. Reopen the solution to pick up the new alias device.";
        }

        Exception? addError = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                ComBusyRetry.Run(() => { GetActiveTcDte().Solution.AddFromFile(splcprojPath, false); }, OpenTimeout);
                addError = null;
                break;
            }
            catch (Exception ex) when (ComCall.NotBusy(ex)) { addError = ex; }
        }

        bool back = AfterMutation(() => FindSolutionProject(GetActiveTcDte(), splcprojPath) is not null);
        if (back) return "safety project reloaded in XAE (re-resolved in the solution after AddFromFile).";
        return "[WARN] the safety project was removed from the in-memory solution but could NOT be re-added" +
               (addError is not null ? $" ({addError.Message})" : string.Empty) +
               ". The solution was not saved: close it WITHOUT saving and reopen it to restore the project.";
    }

    private static object? FindSolutionProject(dynamic dte, string path)
    {
        dynamic projects = dte.Solution.Projects;
        int count = ComCall.ReadProp(() => (int)projects.Count, 0);
        for (int i = 1; i <= count; i++)
        {
            int idx = i;
            object? p = ComCall.ReadProp<object?>(() => (object?)projects.Item(idx), null);
            if (p is null) continue;
            string fn = ComCall.ReadProp(() => (string)((dynamic)p).FullName, string.Empty) ?? string.Empty;
            if (string.Equals(fn, path, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return null;
    }

    private static object? ResolveSafetyProjectObjectOrNull(dynamic dte)
    {
        try
        {
            dynamic projects = dte.Solution.Projects;
            int count = (int)projects.Count;
            for (int i = 1; i <= count; i++)
            {
                dynamic? project; try { project = projects.Item(i); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (project is null) continue;
                object? obj; try { obj = project.Object as object; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (obj is null) continue;
                if (LookupTreeItemOrNull(obj, "TISC") is not null) return obj;
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        return null;
    }

    private static dynamic? FindChildByItemType(dynamic node, int itemType)
    {
        int count = 0; try { count = (int)node.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return null; }
        for (int i = 1; i <= count; i++)
        {
            dynamic? child = null; try { child = node.Child(i); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
            if (child is null) continue;
            int it = -1; try { it = (int)child.ItemType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
            if (it == itemType) return child;
        }
        return null;
    }

    private static void DescribeNode(object nodeObj, System.Text.StringBuilder sb)
    {
        dynamic node = nodeObj;
        string nm = SafeStr(() => (string)node.Name);
        string path = SafeStr(() => (string)node.PathName);
        int it = -1; try { it = (int)node.ItemType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        int stp = -1; try { stp = (int)node.ItemSubType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        int cc = 0; try { cc = (int)node.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        sb.AppendLine(CultureInfo.InvariantCulture, $"  node: name='{nm}' itemType={it} subType={stp} children={cc} path='{path}'");
    }

    private static void DumpProduce(object nodeObj, bool recursive, System.Text.StringBuilder sb)
    {
        dynamic node = nodeObj;
        string? xml = null;
        try { xml = (string)node.ProduceXml(recursive); }
        catch (Exception ex) when (ComCall.NotBusy(ex)) { sb.AppendLine(CultureInfo.InvariantCulture, $"  ProduceXml({recursive}): {DescribeComError(ex)}"); return; }
        if (string.IsNullOrEmpty(xml)) { sb.AppendLine(CultureInfo.InvariantCulture, $"  ProduceXml({recursive}): <empty>"); return; }
        const int cap = 9000;
        string body = xml!.Length > cap ? xml[..cap] + $"\n…[truncated, {xml.Length} chars total]" : xml;
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ProduceXml({recursive}) [{xml.Length} chars]:");
        foreach (var line in body.Split('\n')) sb.AppendLine(CultureInfo.InvariantCulture, $"    | {line.TrimEnd('\r')}");
    }

    private static void RollbackChild(dynamic parent, string childName, dynamic created, System.Text.StringBuilder sb)
    {
        try { parent.DeleteChild(childName); return; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        try { created.Delete(); return; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        sb.AppendLine(CultureInfo.InvariantCulture, $"      [WARN] rollback of '{childName}' failed; not saved → vanishes on close-without-save.");
    }

    private static int[] ParseSubTypes(string? csv)
    {
        int[] dflt = SafetyItemType.ProbeCreateChildSubTypes;
        if (string.IsNullOrWhiteSpace(csv)) return dflt;
        var list = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var v)) list.Add(v);
        return list.Count > 0 ? list.ToArray() : dflt;
    }

    private static string DescribeComError(Exception ex)
    {
        int hr = ex switch
        {
            System.Runtime.InteropServices.COMException c => c.HResult,
            System.Reflection.TargetInvocationException t
                when t.InnerException is System.Runtime.InteropServices.COMException ic => ic.HResult,
            _ => ex.HResult
        };
        var inner = ex is System.Reflection.TargetInvocationException tie && tie.InnerException is not null
            ? tie.InnerException : ex;
        return $"THREW hr=0x{hr:X8} {inner.GetType().Name}: {inner.Message}";
    }
}
