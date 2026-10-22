using PtcXaeMcp.WinForms.TwinCat.Schema;
using PtcXaeMcp.WinForms.TwinCat.Tree;
using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat.Apply;

/// <summary>Where the linker links: the resolved PLC, GVL and EtherCAT master of one apply run.</summary>
/// <param name="Strict">Fail-closed mode: only the exact process-image symbol is linked.</param>
/// <param name="GvlPath">Exact tree path of the run's GVL (lenient fallback only); empty when unknown.</param>
internal sealed record LinkTarget(string PlcName, string GvlName, string MasterPath, bool Strict, string GvlPath = "");

/// <summary>Outcome of <see cref="Linker.LinkAll"/>.</summary>
internal sealed record LinkOutcome(List<LinkResult> Links, List<string> Unresolved, bool HadUnresolvedFailure);

/// <summary>
/// Links the schema's IO points on the <see cref="ISysManTree"/> seam (issue #155 step 6, R8).
/// <list type="bullet">
/// <item>E-3: a PLC variable is the exact process-image symbol <c>&lt;gvl&gt;.&lt;var&gt;</c> in the
/// exact image folders of the resolved PLC's instance (<c>&lt;task&gt; Inputs/Outputs</c>). The former
/// fuzzy scoring over the whole TIPC tree (<c>EndsWith("." + var)</c>, bare variable names) could
/// link a same-named variable of another GVL, e.g. when the new GVL was not built into the process
/// image yet; it is gone. Two hits are an ambiguity error, never a pick. Only in lenient mode
/// (GUI) the variable node of the run's own GVL is a last resort.</item>
/// <item>E-17: both sides are resolved by lookup and <c>LinkVariables</c> is called exactly once per
/// point (it used to double as the probe and then run a second time).</item>
/// <item>E-16: the master subtree (terminal names) and the task list are read once per run and
/// cached, instead of 200k–300k-node walks per IO point.</item>
/// </list>
/// </summary>
internal sealed class Linker
{
    /// <summary>Node budget for the one-time master-subtree name index.</summary>
    internal const int MasterIndexLimit = 200_000;

    private readonly ISysManTree _tree;
    private readonly LinkTarget _target;
    private readonly IReadOnlyDictionary<string, string> _schemaTerminalPaths;
    private readonly IReadOnlyDictionary<string, string> _createdPaths;
    private readonly Action<string> _info;
    private readonly Action<string> _warn;
    private readonly CancellationToken _cancellationToken;

    private readonly Dictionary<string, string> _plcPathCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ioddCheckedTerminals = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>>? _masterNameIndex;
    private List<(string Path, string Direction)>? _imageFolderCache;

    public Linker(
        ISysManTree tree,
        LinkTarget target,
        EthercatNode schemaRoot,
        IReadOnlyDictionary<string, string>? createdPaths = null,
        Action<string>? info = null,
        Action<string>? warn = null,
        CancellationToken cancellationToken = default)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _schemaTerminalPaths = BuildSchemaTerminalPathIndex(schemaRoot, target.MasterPath);
        _createdPaths = createdPaths ?? new Dictionary<string, string>();
        _info = info ?? (_ => { });
        _warn = warn ?? (_ => { });
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Links every point. A point that cannot be resolved or linked is collected with its reason;
    /// <paramref name="failIfUnresolved"/> decides whether that fails the run (E-11).
    /// </summary>
    public LinkOutcome LinkAll(IReadOnlyList<IoPoint> points, bool failIfUnresolved)
    {
        var links = new List<LinkResult>();
        var unresolved = new List<string>();
        foreach (var point in points)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            string direction = point.Direction.Trim().ToUpperInvariant();
            try
            {
                string ioPath = ResolveIoLeafPath(point);
                string plcPath = ResolvePlcVariablePath(point.VarName, direction);
                _tree.LinkVariables(plcPath, ioPath);
                _info($"Linked: {point.VarName} <-> {ioPath}");
                links.Add(new LinkResult
                {
                    VarName = point.VarName,
                    Direction = direction,
                    Type = string.IsNullOrWhiteSpace(point.Type) ? "BOOL" : point.Type!,
                    Terminal = point.TerminalName,
                    Channel = point.Channel,
                    Group = point.Group ?? string.Empty,
                    Description = point.Description ?? string.Empty,
                    PlcPath = plcPath,
                    IoPath = ioPath,
                });
            }
            catch (Exception ex) when (ComCall.Recoverable(ex))
            {
                string reason = $"{point.VarName} ({point.TerminalName} Ch{point.Channel} {direction}): {ex.Message}";
                unresolved.Add(reason);
                _warn(failIfUnresolved
                    ? $"Not resolved (failIfUnresolved=true, reported at the end of the run): {reason}"
                    : $"Not resolved, skipped (failIfUnresolved=false): {reason}");
            }
        }

        _info($"Link summary: OK={links.Count}  skipped={unresolved.Count}");
        return new LinkOutcome(links, unresolved, failIfUnresolved && unresolved.Count > 0);
    }

    // ---- PLC side --------------------------------------------------------------------------

    /// <summary>
    /// Exact process-image path of <c>&lt;gvl&gt;.&lt;var&gt;</c> for <paramref name="direction"/>.
    /// Throws naming every folder tried when it is not there (or is there twice).
    /// </summary>
    internal string ResolvePlcVariablePath(string varName, string direction)
    {
        string dir = direction.Trim().ToUpperInvariant();
        string cacheKey = $"{varName}|{dir}";
        if (_plcPathCache.TryGetValue(cacheKey, out string? cached)) return cached;

        string symbol = LinkCandidates.GvlSymbol(_target.GvlName, varName);
        List<string> folders = ImageFolders(dir);
        var hits = folders
            .Select(folder => _tree.Lookup($"{folder}^{symbol}"))
            .Where(node => node is not null)
            .Select(node => node!.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hits.Count > 1)
            throw new InvalidOperationException(
                $"PLC variable '{symbol}' is ambiguous in the process image: {string.Join(", ", hits.Select(h => $"'{h}'"))}. Nothing was linked.");
        if (hits.Count == 1) return _plcPathCache[cacheKey] = hits[0];

        string tried = folders.Count == 0 ? "<no image folder found>" : string.Join(", ", folders.Select(f => $"'{f}'"));
        if (!_target.Strict)
        {
            string? declaration = FindGvlDeclarationNode(varName);
            if (declaration is not null)
            {
                _warn($"PLC variable '{symbol}' is not in the process image ({tried}); linking its GVL declaration node '{declaration}' (lenient mode).");
                return _plcPathCache[cacheKey] = declaration;
            }
        }

        throw new InvalidOperationException(
            $"PLC variable '{symbol}' not found in the process image of PLC '{_target.PlcName}' (tried {tried}). " +
            "Build the PLC so the GVL is in the process image (autoBuild=true, or twincat_build_plc), then link again. " +
            "A same-named variable of another GVL is never used.");
    }

    // "<instance>^<task> Inputs"/"... Outputs" folders that exist, for every task under TIRT and
    // every instance of the resolved PLC. Read once per run (E-16).
    private List<string> ImageFolders(string direction)
    {
        _imageFolderCache ??= ReadImageFolders();
        string dir = direction == "Q" ? "Q" : "I";
        return _imageFolderCache.Where(f => f.Direction == dir).Select(f => f.Path).ToList();
    }

    private List<(string Path, string Direction)> ReadImageFolders()
    {
        var tasks = new List<string>();
        ITreeNode? tirt = _tree.Lookup("TIRT");
        if (tirt is not null)
            tasks.AddRange(SafeChildren(tirt).Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
        if (!tasks.Contains("PlcTask", StringComparer.OrdinalIgnoreCase)) tasks.Add("PlcTask");

        string plcPath = $"TIPC^{_target.PlcName}";
        var instances = new List<string> { $"{plcPath}^{_target.PlcName} Instance" };
        ITreeNode? plc = _tree.Lookup(plcPath);
        if (plc is not null)
            instances.AddRange(SafeChildren(plc)
                .Where(c => c.Name.EndsWith(" Instance", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Path.Length > 0 ? c.Path : $"{plcPath}^{c.Name}"));

        var folders = new List<(string Path, string Direction)>();
        foreach (string instance in instances.Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (string task in tasks.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (string dir in new[] { "I", "Q" })
                    foreach (string name in LinkCandidates.ImageFolderNames(task, dir))
                    {
                        string path = $"{instance}^{name}";
                        if (!folders.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)) &&
                            _tree.Lookup(path) is not null)
                            folders.Add((path, dir));
                    }
        return folders;
    }

    // Children, or none when the child count cannot be read (a genuine failure, not busy).
    private static IEnumerable<ITreeNode> SafeChildren(ITreeNode node)
    {
        try { return node.Children().ToList(); }
        catch (Exception ex) when (ComCall.Recoverable(ex)) { return []; }
    }

    // Lenient last resort: the variable's node inside the run's own GVL (exact name, one level).
    private string? FindGvlDeclarationNode(string varName)
    {
        if (string.IsNullOrWhiteSpace(_target.GvlPath)) return null;
        ITreeNode? gvl = _tree.Lookup(_target.GvlPath);
        if (gvl is null) return null;
        ITreeNode? node = _tree.Lookup($"{_target.GvlPath}^{varName}")
            ?? SafeChildren(gvl).FirstOrDefault(c => string.Equals(c.Name, varName, StringComparison.OrdinalIgnoreCase));
        return node is null ? null : node.Path.Length > 0 ? node.Path : $"{_target.GvlPath}^{varName}";
    }

    // ---- IO side ---------------------------------------------------------------------------

    /// <summary>The IO leaf path of <paramref name="point"/>; throws naming every path tried.</summary>
    internal string ResolveIoLeafPath(IoPoint point)
    {
        if (point.IsIoLink) return ResolveIoLinkLeafPath(point);

        var tried = new List<string>();
        foreach (string candidate in SchemaLeafCandidates(point.TerminalName, point.Channel, point.Direction))
        {
            if (Probe(candidate, tried)) return candidate;
        }

        ITreeNode terminal = FindTerminal(point.TerminalName);
        var direct = new List<string>();
        LinkCandidates.AddIoCandidates(direct, terminal.Path, point.Channel, point.Direction);
        foreach (string candidate in direct)
        {
            if (Probe(candidate, tried)) return candidate;
        }

        string? leaf = FindIoLeafUnder(terminal, point.Channel, point.Direction, point.IoLeaf ?? string.Empty);
        if (leaf is not null) return leaf;

        throw new InvalidOperationException(
            $"IO leaf not found: terminal '{terminal.Path}', channel {point.Channel}, direction {point.Direction}. " +
            $"None of these paths exists: {string.Join(", ", tried.Select(t => $"'{t}'"))}. " +
            "Channel variables of a terminal are not listed as its tree children, only addressable by path — check " +
            "the channel number, or pass the leaf name as ioLeaf.");
    }

    private bool Probe(string path, List<string> tried)
    {
        if (tried.Contains(path, StringComparer.OrdinalIgnoreCase)) return false;
        tried.Add(path);
        return _tree.Lookup(path) is not null;
    }

    private IEnumerable<string> SchemaLeafCandidates(string terminalName, int channel, string direction)
    {
        var result = new List<string>();
        _schemaTerminalPaths.TryGetValue(terminalName, out string? path);
        if (path is not null) LinkCandidates.AddIoCandidates(result, path, channel, direction);
        if (_schemaTerminalPaths.TryGetValue("__KEY__" + LinkCandidates.TerminalNameKey(terminalName), out string? keyed) &&
            !string.Equals(keyed, path, StringComparison.OrdinalIgnoreCase))
            LinkCandidates.AddIoCandidates(result, keyed, channel, direction);
        return result;
    }

    // The terminal: the path this run created it at, else a unique exact-name match in the
    // master subtree (index read once per run, E-16).
    private ITreeNode FindTerminal(string terminalName)
    {
        foreach (string key in new[] { terminalName, "__KEY__" + LinkCandidates.TerminalNameKey(terminalName) })
        {
            if (_createdPaths.TryGetValue(key, out string? created) && _tree.Lookup(created) is ITreeNode hit)
                return hit;
        }

        _masterNameIndex ??= BuildMasterNameIndex();
        if (!_masterNameIndex.TryGetValue(terminalName, out var paths) || paths.Count == 0)
            throw new InvalidOperationException($"Terminal '{terminalName}' not found under EtherCAT master '{_target.MasterPath}'.");
        if (paths.Count > 1)
        {
            if (_target.Strict)
                throw new InvalidOperationException(
                    $"Terminal name '{terminalName}' is ambiguous under '{_target.MasterPath}': {string.Join(", ", paths.Select(p => $"'{p}'"))}.");
            _warn($"Terminal name '{terminalName}' occurs {paths.Count} times under '{_target.MasterPath}'; using '{paths[0]}' (lenient mode).");
        }
        return _tree.Lookup(paths[0])
            ?? throw new InvalidOperationException($"Terminal '{terminalName}' at '{paths[0]}' no longer resolves.");
    }

    private Dictionary<string, List<string>> BuildMasterNameIndex()
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        ITreeNode master = _tree.Lookup(_target.MasterPath)
            ?? throw new InvalidOperationException($"EtherCAT master '{_target.MasterPath}' not found.");
        var stack = new Stack<ITreeNode>();
        foreach (ITreeNode child in SafeChildren(master)) stack.Push(child);
        int seen = 0;
        while (stack.Count > 0 && seen++ < MasterIndexLimit)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            ITreeNode node = stack.Pop();
            string name = node.Name;
            if (!string.IsNullOrWhiteSpace(name) && node.Path.Length > 0)
            {
                if (!index.TryGetValue(name, out var list)) index[name] = list = [];
                list.Add(node.Path);
            }
            foreach (ITreeNode child in SafeChildren(node)) stack.Push(child);
        }
        return index;
    }

    // Channel node by name in the terminal's IO group folders, then a bounded scored scan of the
    // terminal's own subtree (never outside it).
    private string? FindIoLeafUnder(ITreeNode terminal, int channel, string direction, string ioLeafOverride)
    {
        string[] channelNames = [$"Channel {channel}", $"Kanal {channel}"];
        foreach (string folderName in LinkCandidates.IoGroupFolderNames(direction))
        {
            foreach (string channelName in channelNames)
            {
                ITreeNode? hit = _tree.Lookup($"{terminal.Path}^{folderName}^{channelName}");
                if (hit is not null) return hit.Path;
            }
        }

        string dir = direction.Trim().ToUpperInvariant();
        string? best = null;
        int bestScore = -1;
        var stack = new Stack<ITreeNode>();
        stack.Push(terminal);
        int seen = 0;
        while (stack.Count > 0 && seen++ < 10_000)
        {
            ITreeNode node = stack.Pop();
            string name = node.Name;
            string path = node.Path;
            if (!ReferenceEquals(node, terminal) && LinkCandidates.IsIoLeafChannelMatch(name, path, channel))
            {
                int score = LinkCandidates.ScoreIoLeafCandidate(name, channel, dir, ioLeafOverride);
                if (score > bestScore)
                {
                    best = path;
                    bestScore = score;
                }
            }
            foreach (ITreeNode child in SafeChildren(node)) stack.Push(child);
        }
        return best;
    }

    // Named IO-Link process-data entry under its master terminal.
    private string ResolveIoLinkLeafPath(IoPoint point)
    {
        EnsureIoLinkIoddPresent(point);

        var tried = new List<string>();
        if (_schemaTerminalPaths.TryGetValue(point.TerminalName, out string? schemaPath))
            foreach (string candidate in IoLinkLinkPaths.Candidates(schemaPath, point.IoLinkPort, point.IoLinkChannel))
                if (Probe(candidate, tried)) return candidate;

        ITreeNode terminal = FindTerminal(point.TerminalName);
        foreach (string candidate in IoLinkLinkPaths.Candidates(terminal.Path, point.IoLinkPort, point.IoLinkChannel))
            if (Probe(candidate, tried)) return candidate;

        // An explicit ioLeaf (full path) still wins when it exists.
        if (!string.IsNullOrWhiteSpace(point.IoLeaf) && _tree.Lookup(point.IoLeaf) is not null) return point.IoLeaf!;

        throw new InvalidOperationException(
            $"IO-Link PDO leaf not found for '{point.VarName}': port {point.IoLinkPort?.ToString(CultureInfo.InvariantCulture) ?? "?"} " +
            $"channel '{point.IoLinkChannel}' under terminal '{point.TerminalName}' (tried {string.Join(", ", tried.Select(t => $"'{t}'"))}). " +
            "Is the device assigned to the port and the IODD broken down?");
    }

    // Once per IO-Link master terminal: every configured port's IODD must be installed. A read
    // failure lets normal resolution surface the problem; a positively missing IODD stops the run.
    private void EnsureIoLinkIoddPresent(IoPoint point)
    {
        if (!_ioddCheckedTerminals.Add(point.TerminalName)) return;

        ITreeNode? node = _schemaTerminalPaths.TryGetValue(point.TerminalName, out string? sp) ? _tree.Lookup(sp) : null;
        if (node is null)
        {
            try { node = FindTerminal(point.TerminalName); }
            catch (Exception ex) when (ComCall.Recoverable(ex)) { return; }
        }

        IoLinkConfig cfg;
        try { cfg = IoLinkXmlParser.Parse(node.Path.Length > 0 ? node.Path : point.TerminalName, node.ProduceXml(false)); }
        catch (Exception ex) when (ComCall.Recoverable(ex)) { return; }

        string? folder = IoddFolder.ResolveDefault();
        if (string.IsNullOrWhiteSpace(folder)) return;

        string? report = IoddPresence.MissingReport(cfg, IoddCatalog.Scan(folder));
        if (report is not null) throw new InvalidOperationException(report);
    }

    /// <summary>
    /// Expected tree path of every schema node under the resolved master
    /// (<paramref name="masterPath"/>), by name and by designation key. Pure.
    /// </summary>
    internal static Dictionary<string, string> BuildSchemaTerminalPathIndex(EthercatNode root, string masterPath)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root is null || string.IsNullOrWhiteSpace(masterPath)) return index;

        void AddPath(string nodeName, string path)
        {
            if (string.IsNullOrWhiteSpace(nodeName)) return;
            index[nodeName] = path;
            string key = "__KEY__" + LinkCandidates.TerminalNameKey(nodeName);
            index.TryAdd(key, path);
        }

        void Walk(EthercatNode node, string parentPath)
        {
            string current = $"{parentPath}^{node.Name}";
            AddPath(node.Name, current);
            foreach (var child in node.Children) Walk(child, current);
        }

        if (EtherCatTopologyBuilder.ShouldFlattenPseudoRootNode("TIID", root))
        {
            AddPath(root.Name, masterPath);
            foreach (var child in root.Children) Walk(child, masterPath);
        }
        else
        {
            Walk(root, masterPath);
        }
        return index;
    }
}
