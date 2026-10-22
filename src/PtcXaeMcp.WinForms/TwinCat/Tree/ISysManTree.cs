namespace PtcXaeMcp.WinForms.TwinCat.Tree;

/// <summary>
/// One node of the TwinCAT System Manager tree (ITcSmTreeItem), as seen through the
/// <see cref="ISysManTree"/> seam (issue #155 step 3). The real implementation wraps the COM
/// object (<see cref="ComSysManTree"/>); tests use an in-memory dictionary tree.
/// </summary>
internal interface ITreeNode
{
    /// <summary>Item name (the last path segment). Empty when it cannot be read.</summary>
    string Name { get; }

    /// <summary>Full <c>^</c>-separated tree path (ITcSmTreeItem.PathName). Empty when unreadable.</summary>
    string Path { get; }

    /// <summary>TREEITEMTYPE (e.g. 600 PLC project, 601 folder, 615 GVL); -1 when unknown.</summary>
    int ItemType { get; }

    /// <summary>ItemSubType (e.g. 111 EtherCAT master); -1 when unknown.</summary>
    int SubType { get; }

    /// <summary>Direct child count. A read failure throws — it is uncertainty, never "empty".</summary>
    int ChildCount { get; }

    /// <summary>1-based direct child; null when that child cannot be read.</summary>
    ITreeNode? Child(int index);

    /// <summary>ITcSmTreeItem.CreateChild. Returns the created node (resolved by path when the call returns nothing).</summary>
    ITreeNode CreateChild(string name, int subType, string? before, object? vInfo);

    /// <summary>ITcSmTreeItem.ProduceXml.</summary>
    string ProduceXml(bool recursive = true);

    /// <summary>ITcSmTreeItem.ConsumeXml.</summary>
    void ConsumeXml(string xml);

    /// <summary>ITcSmTreeItem.ExportChild: writes the named child (e.g. an EtherCAT master) to an .xti file.</summary>
    void ExportChild(string childName, string filePath);

    /// <summary>
    /// The underlying COM object, for runner code that has not been moved onto the seam yet
    /// (later steps of #155). Fakes return themselves.
    /// </summary>
    object Raw { get; }
}

/// <summary>
/// Seam over the System Manager tree primitives <see cref="NativeTwinCatRunner"/> uses
/// (issue #155 step 3, R3): lookup by exact path, children, name/type/subtype, create, delete,
/// XML round-trip and variable linking. Components built on it (<c>ApplyTargetResolver</c>,
/// <c>GvlWriter</c>, later the topology builder and the linker) are unit-testable with a fake.
/// </summary>
internal interface ISysManTree
{
    /// <summary>
    /// The node at exactly <paramref name="path"/>, or null. Never a "close" match: a node whose
    /// readable path differs from the requested one is treated as absent.
    /// </summary>
    ITreeNode? Lookup(string path);

    /// <summary>
    /// Deletes exactly <paramref name="node"/> (by its parent and leaf name). Refuses a node
    /// whose path is unknown or has no parent. Throws when the delete fails.
    /// </summary>
    void Delete(ITreeNode node);

    /// <summary>ITcSysManager.LinkVariables(a, b).</summary>
    void LinkVariables(string pathA, string pathB);
}

/// <summary>Enumeration helpers over <see cref="ITreeNode"/> shared by the seam components.</summary>
internal static class TreeNodeExtensions
{
    /// <summary>Direct children in index order; unreadable children are skipped.</summary>
    public static IEnumerable<ITreeNode> Children(this ITreeNode node)
    {
        int count = node.ChildCount;
        for (int i = 1; i <= count; i++)
        {
            ITreeNode? child = node.Child(i);
            if (child is not null) yield return child;
        }
    }

    /// <summary>Last segment of a <c>^</c>-separated tree path.</summary>
    public static string Leaf(string path)
    {
        int caret = path.LastIndexOf('^');
        return caret < 0 ? path : path[(caret + 1)..];
    }

    /// <summary>Parent part of a <c>^</c>-separated tree path; empty for a root.</summary>
    public static string ParentPath(string path)
    {
        int caret = path.LastIndexOf('^');
        return caret <= 0 ? string.Empty : path[..caret];
    }
}
