using PtcXaeMcp.WinForms.TwinCat.Tree;

namespace PtcXaeMcp.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ISysManTree"/> (issue #155 step 3): nodes in a path-keyed dictionary,
/// children in insertion order. Records every mutation so tests can assert what was — and was
/// not — deleted or created.
/// </summary>
internal sealed class FakeSysManTree : ISysManTree
{
    // Case-sensitive keys so a test can build names that differ only in case (to model an
    // ambiguous folder); lookups fall back to a case-insensitive match like TwinCAT's.
    private readonly Dictionary<string, FakeTreeNode> _nodes = new(StringComparer.Ordinal);

    /// <summary>Mutations in order: "create &lt;path&gt;", "delete &lt;path&gt;", "link &lt;a&gt; &lt;b&gt;".</summary>
    public List<string> Mutations { get; } = [];

    /// <summary>Paths passed to <see cref="Delete"/> that were removed.</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>When set, CreateChild throws this for the child name it is keyed on.</summary>
    public Dictionary<string, Exception> CreateChildThrows { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>vInfo strings CreateChild rejects like a missing ESI: nothing is created, it throws.</summary>
    public HashSet<string> RejectedVInfos { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Child names for which CreateChild adds the node and THEN throws (issue #155 E-19: TwinCAT
    /// reports an error although the box was added).
    /// </summary>
    public HashSet<string> CreateChildThrowsAfterCreating { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ProductRevision a created EtherCAT box reports, per child name (default: the vInfo passed).</summary>
    public Dictionary<string, string> ProductRevisionOverride { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Node names whose next Name read throws a busy COMException (RPC_E_CALL_REJECTED) once —
    /// the XAE turning busy right after a CreateChild (issue #190).
    /// </summary>
    public HashSet<string> NameReadBusyOnce { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many Child(i) reads all nodes served (to assert per-run caching, E-16).</summary>
    public int ChildReads { get; internal set; }

    /// <summary>Every CreateChild call as "name|vInfo", including the ones that threw.</summary>
    public List<string> CreateAttempts { get; } = [];

    /// <summary>Adds a node (and any missing ancestors as plain nodes). Returns it.</summary>
    /// <param name="enumerable">False adds a node that <see cref="Lookup"/> finds but its parent's
    /// Child(i) does not list (like the PLC source-project node, which hangs off NestedProject).</param>
    public FakeTreeNode Add(string path, int itemType = -1, int subType = -1, bool enumerable = true)
    {
        if (_nodes.TryGetValue(path, out var existing))
        {
            existing.ItemType = itemType;
            existing.SubType = subType;
            return existing;
        }

        string parentPath = TreeNodeExtensions.ParentPath(path);
        FakeTreeNode? parent = parentPath.Length == 0
            ? null
            : _nodes.TryGetValue(parentPath, out var p) ? p : Add(parentPath);

        var node = new FakeTreeNode(this, path, itemType, subType, parent);
        _nodes[path] = node;
        if (enumerable) parent?.ChildList.Add(node);
        return node;
    }

    public bool Exists(string path) => Find(path) is not null;

    public FakeTreeNode Get(string path) => Find(path) ?? throw new KeyNotFoundException(path);

    public ITreeNode? Lookup(string path) => Find(path);

    private FakeTreeNode? Find(string path)
        => _nodes.TryGetValue(path, out var node)
            ? node
            : _nodes.FirstOrDefault(kv => string.Equals(kv.Key, path, StringComparison.OrdinalIgnoreCase)).Value;

    public void Delete(ITreeNode node)
    {
        if (!_nodes.TryGetValue(node.Path, out var fake) || !ReferenceEquals(fake, node))
            throw new InvalidOperationException($"fake: '{node.Path}' is not in the tree");
        fake.Parent?.ChildList.Remove(fake);
        RemoveSubtree(fake);
        Deleted.Add(node.Path);
        Mutations.Add($"delete {node.Path}");
    }

    public void LinkVariables(string pathA, string pathB) => Mutations.Add($"link {pathA} {pathB}");

    internal FakeTreeNode CreateChildOf(FakeTreeNode parent, string name, int subType, object? vInfo)
    {
        CreateAttempts.Add($"{name}|{vInfo}");
        if (CreateChildThrows.TryGetValue(name, out var ex)) throw ex;
        bool isBox = subType == EtherCatBoxSubType;
        if (isBox && vInfo is string rejected && RejectedVInfos.Contains(rejected))
            throw new InvalidOperationException($"fake: no device description for vInfo '{rejected}'");
        string path = $"{parent.Path}^{name}";
        if (Find(path) is not null)
            throw new InvalidOperationException($"fake: '{path}' already exists (TwinCAT rejects duplicate names)");

        FakeTreeNode node;
        if (isBox)
        {
            // An EtherCAT box: ItemType 5, SubType 9099, XML carrying the ProductRevision.
            node = Add(path, itemType: 5, subType: subType);
            string revision = ProductRevisionOverride.TryGetValue(name, out var r) ? r : vInfo as string ?? string.Empty;
            node.Xml = BoxXml(name, revision, physAddr: 1000 + CreateAttempts.Count);
        }
        else
        {
            // CreateChild's nSubType is the TREEITEMTYPE for PLC objects (615 = GVL).
            node = Add(path, itemType: subType);
            node.Xml = vInfo as string ?? string.Empty;
        }
        Mutations.Add($"create {path}");
        if (CreateChildThrowsAfterCreating.Contains(name))
            throw new InvalidOperationException($"fake: CreateChild('{name}') failed after adding the node");
        return node;
    }

    /// <summary>EtherCAT box CreateChild subtype (EtherCatVInfo.BoxItemSubType).</summary>
    public const int EtherCatBoxSubType = 9099;

    /// <summary>Minimal ProduceXml of an EtherCAT box.</summary>
    public static string BoxXml(string name, string productRevision, int physAddr = 1001) =>
        $"<TreeItem><ItemName>{name}</ItemName><EtherCAT><Slave><Info><Name>{name}</Name><PhysAddr>{physAddr}</PhysAddr>" +
        $"<ProductRevision>{productRevision}</ProductRevision></Info></Slave></EtherCAT></TreeItem>";

    private void RemoveSubtree(FakeTreeNode node)
    {
        foreach (var child in node.ChildList.ToList()) RemoveSubtree(child);
        _nodes.Remove(node.Path);
    }
}

internal sealed class FakeTreeNode : ITreeNode
{
    private readonly FakeSysManTree _tree;

    public FakeTreeNode(FakeSysManTree tree, string path, int itemType, int subType, FakeTreeNode? parent)
    {
        _tree = tree;
        Path = path;
        ItemType = itemType;
        SubType = subType;
        Parent = parent;
    }

    public string Name
    {
        get
        {
            string leaf = TreeNodeExtensions.Leaf(Path);
            if (_tree.NameReadBusyOnce.Remove(leaf))
                throw new System.Runtime.InteropServices.COMException(
                    "fake: call rejected", unchecked((int)0x80010001));
            return leaf;
        }
    }
    public string Path { get; }
    public int ItemType { get; set; }
    public int SubType { get; set; }
    public FakeTreeNode? Parent { get; }
    public List<FakeTreeNode> ChildList { get; } = [];

    /// <summary>The declaration text a GVL was created with / the XML of the node.</summary>
    public string Xml { get; set; } = string.Empty;

    public int ChildCount => ChildList.Count;
    public ITreeNode? Child(int index)
    {
        _tree.ChildReads++;
        return index >= 1 && index <= ChildList.Count ? ChildList[index - 1] : null;
    }

    public ITreeNode CreateChild(string name, int subType, string? before, object? vInfo)
        => _tree.CreateChildOf(this, name, subType, vInfo);

    public string ProduceXml(bool recursive = true) => Xml;
    public void ConsumeXml(string xml)
    {
        Xml = xml;
        _tree.Mutations.Add($"consume {Path}");
    }

    public void ExportChild(string childName, string filePath) => _tree.Mutations.Add($"export {Path}^{childName} {filePath}");
    public object Raw => this;
}
