using PtcXaeMcp.WinForms.TwinCat.Schema;
using PtcXaeMcp.WinForms.TwinCat.Tree;

namespace PtcXaeMcp.WinForms.TwinCat.Apply;

/// <summary>Outcome of <see cref="GvlWriter.SetAlwaysBind"/>; <c>Xml</c> is the last XML read (for diagnostics).</summary>
internal sealed record AlwaysBindOutcome(bool Verified, string Message, string Xml);

/// <summary>
/// Creates the apply_schema GVL on the <see cref="ISysManTree"/> seam (issue #155 step 4, R6).
///
/// E-2: the runner used to delete and recreate an existing GVL — all its declarations and every
/// link onto it — on <i>any</i> CreateChild exception, and found the victim with a solution-wide
/// fuzzy name search. Now:
/// <list type="bullet">
/// <item>an existing GVL at the exact target path is a collision: <see cref="InvalidOperationException"/>
/// naming that path, nothing is deleted — unless <c>overwriteExistingGvl</c> is set, or this
/// writer created that GVL earlier in the same run (idempotent re-apply);</item>
/// <item>even then, only the node at exactly <see cref="GvlTarget.GvlPath"/> is deleted, and only
/// when it really is a GVL (ItemType 615);</item>
/// <item>a CreateChild failure is reported, never answered with a delete.</item>
/// </list>
/// </summary>
internal sealed class GvlWriter
{
    /// <summary>How many nodes the read-only diagnostic search for a same-named object may visit.</summary>
    internal const int DiagnosticSearchLimit = 20000;

    private readonly ISysManTree _tree;
    private readonly Action<string> _info;
    private readonly Action<string> _warn;
    private readonly HashSet<string> _createdThisRun = new(StringComparer.OrdinalIgnoreCase);

    public GvlWriter(ISysManTree tree, Action<string>? info = null, Action<string>? warn = null)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _info = info ?? (_ => { });
        _warn = warn ?? (_ => { });
    }

    /// <summary>True when this writer created the GVL at <paramref name="gvlPath"/> in this run.</summary>
    public bool CreatedThisRun(string gvlPath) => _createdThisRun.Contains(gvlPath);

    /// <summary>
    /// Pre-flight (no tree changes): throws the collision error when <see cref="Write"/> would
    /// refuse. Lets the runner stop before it builds any hardware.
    /// </summary>
    public void CheckWritable(GvlTarget target, bool overwriteExistingGvl)
    {
        ArgumentNullException.ThrowIfNull(target);
        ITreeNode? existing = _tree.Lookup(target.GvlPath);
        if (existing is not null) AssertReplaceable(target, existing, overwriteExistingGvl);
    }

    /// <summary>
    /// Creates the GVL at <paramref name="target"/> with <paramref name="declarationText"/>.
    /// Returns the created node.
    /// </summary>
    public ITreeNode Write(GvlTarget target, string declarationText, bool overwriteExistingGvl)
    {
        ArgumentNullException.ThrowIfNull(target);
        ITreeNode folder = _tree.Lookup(target.FolderPath)
            ?? throw new InvalidOperationException($"GVL folder '{target.FolderPath}' not found; nothing was written.");

        ITreeNode? existing = _tree.Lookup(target.GvlPath);
        if (existing is not null)
        {
            AssertReplaceable(target, existing, overwriteExistingGvl);
            _warn(CreatedThisRun(target.GvlPath)
                ? $"GVL '{target.GvlPath}' was created earlier in this run and is replaced."
                : $"GVL '{target.GvlPath}' exists and is replaced (overwriteExistingGvl=true): its previous declarations and links are discarded.");
            _tree.Delete(existing);
            if (_tree.Lookup(target.GvlPath) is not null)
                throw new InvalidOperationException(
                    $"GVL '{target.GvlPath}' is still present after the delete; not recreating it.");
        }

        _info($"Creating GVL: {target.GvlPath}");
        ITreeNode created;
        try
        {
            created = folder.CreateChild(target.GvlName, PlcItemType.Gvl, null, declarationText);
        }
        catch (Exception ex) when (ComCall.Recoverable(ex))
        {
            throw new InvalidOperationException(
                $"Creating GVL '{target.GvlPath}' failed: {ex.Message}. Nothing was deleted." +
                DescribeSameNamedObjects(target), ex);
        }

        _createdThisRun.Add(target.GvlPath);
        return created;
    }

    /// <summary>
    /// Sets the GVL's "LinkAlways" (Immer binden) property to true through its XML
    /// (<c>VSProperties/VSProperty[Name=LinkAlways]</c> on TC 4026) and verifies it on a freshly
    /// looked-up node. Only the GVL at exactly <paramref name="gvlPath"/> is touched: the former
    /// solution-wide DTE project-item name search is gone (on TC 4026.22 the TwinCAT project
    /// exposes no ProjectItems at all, verified live, so it never found anything; elsewhere it
    /// could hit a same-named GVL of another PLC).
    /// </summary>
    public AlwaysBindOutcome SetAlwaysBind(string gvlPath)
    {
        ITreeNode gvl = _tree.Lookup(gvlPath)
            ?? throw new InvalidOperationException($"GVL '{gvlPath}' not found; 'LinkAlways' cannot be set.");
        string xml = gvl.ProduceXml();
        if (string.IsNullOrWhiteSpace(xml))
            return new AlwaysBindOutcome(false, $"'LinkAlways' not set for '{gvlPath}': ProduceXml returned nothing.", xml);

        AlwaysBindXml.ApplyResult applied = AlwaysBindXml.Apply(xml);
        if (!applied.Changed)
        {
            return AlwaysBindXml.TryReadState(xml) == true
                ? new AlwaysBindOutcome(true, $"GVL property already set: LinkAlways=True ({gvlPath})", xml)
                : new AlwaysBindOutcome(false, $"No LinkAlways/AlwaysBind field in the XML of '{gvlPath}'.", xml);
        }

        gvl.ConsumeXml(applied.Xml);
        string verifyXml = (_tree.Lookup(gvlPath) ?? gvl).ProduceXml();
        return AlwaysBindXml.TryReadState(verifyXml) == true
            ? new AlwaysBindOutcome(true, $"GVL property set: LinkAlways=True ({gvlPath})", verifyXml)
            : new AlwaysBindOutcome(false, $"'LinkAlways' of '{gvlPath}' is not true after ConsumeXml.", verifyXml);
    }

    private void AssertReplaceable(GvlTarget target, ITreeNode existing, bool overwriteExistingGvl)
    {
        if (CreatedThisRun(target.GvlPath))
        {
            RequireGvlKind(target, existing);
            return;
        }

        if (!overwriteExistingGvl)
            throw new InvalidOperationException(
                $"GVL '{target.GvlName}' already exists at '{target.GvlPath}'. Nothing was deleted or changed. " +
                "Choose another gvlName, or set overwriteExistingGvl=true (tool parameter or schema " +
                "twincat.overwriteExistingGvl) to replace exactly this GVL — its declarations and links are lost.");

        RequireGvlKind(target, existing);
    }

    private static void RequireGvlKind(GvlTarget target, ITreeNode existing)
    {
        int itemType = existing.ItemType;
        if (itemType != PlcItemType.Gvl)
            throw new InvalidOperationException(
                $"'{target.GvlPath}' exists but is not a plain GVL (ItemType {itemType}, expected {PlcItemType.Gvl}); " +
                "refusing to delete it.");
    }

    // Read-only: name where a same-named object lives when CreateChild is rejected (TwinCAT
    // object names are unique per PLC project). Bounded, and never used to pick a delete target.
    private string DescribeSameNamedObjects(GvlTarget target)
    {
        try
        {
            ITreeNode? root = string.IsNullOrWhiteSpace(target.Plc.ProjectNodePath) ? null : _tree.Lookup(target.Plc.ProjectNodePath);
            if (root is null) return string.Empty;

            var hits = new List<string>();
            var stack = new Stack<ITreeNode>();
            stack.Push(root);
            int seen = 0;
            while (stack.Count > 0 && seen++ < DiagnosticSearchLimit && hits.Count < 5)
            {
                ITreeNode node = stack.Pop();
                if (string.Equals(node.Name, target.GvlName, StringComparison.OrdinalIgnoreCase))
                    hits.Add(node.Path);
                foreach (ITreeNode child in node.Children()) stack.Push(child);
            }

            return hits.Count == 0
                ? string.Empty
                : $" An object with that name already exists at: {string.Join(", ", hits.Select(h => $"'{h}'"))}.";
        }
        catch (Exception ex) when (ComCall.Recoverable(ex))
        {
            return string.Empty;
        }
    }
}
