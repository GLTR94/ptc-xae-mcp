using System.Globalization;
namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // ===================================================================
    // nc-axes (Motion). The NC tree must be
    // reached by browsing — node labels are localized (German on this
    // box). LookupTreeItem is used ONLY for the stable 'TINC' root anchor
    // (Infosys 242735499); every level below it is discovered by
    // enumerating children and matching on ItemType/ItemSubType.
    //
    // DumpTincTree is the read-only diagnostic that mirrors DumpTipcTree;
    // it is also the empirical-capture vehicle for the type discriminators
    // and the axis ProduceXml schema the spec flags as unknown.
    // ===================================================================

    public string DumpTincTree() => ComBusyRetry.Run(() =>
    {
        var sb = new System.Text.StringBuilder();
        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);

        dynamic? tinc = LookupTreeItemOrNull(sysMan, "TINC");
        if (tinc is null) { sb.AppendLine("No NC (TINC) root found."); return sb.ToString(); }

        sb.AppendLine("=== NC (TINC) tree dump ===");
        DumpNcNode(tinc, sb, depth: 0, maxDepth: 5);
        return sb.ToString();
    }, OpenTimeout);

    // Mirrors DumpNode (TwinCatComSession.PlcCode.cs) but also emits the
    // full ProduceXml(false) for every node that exposes it, so the axis
    // scaling/limits/encoder schema can be captured from the live fixture.
    private static void DumpNcNode(dynamic node, System.Text.StringBuilder sb, int depth, int maxDepth)
    {
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
            const int cap = 8000;
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
                if (child is not null) DumpNcNode(child, sb, depth + 1, maxDepth);
            }
            finally
            {
                if (child is not null)
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(child); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
            }
        }
    }

    // ---- browse-by-type helpers (never match on the localized label) ----

    private static int ItemTypeOf(dynamic node)
    {
        try { return (int)node.ItemType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return -1; }
    }

    private static string PathOf(dynamic node)
    {
        try { return (string)node.PathName ?? NameOf(node); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return NameOf(node); }
    }

    private static IEnumerable<dynamic> ChildrenByType(dynamic node, int itemType)
    {
        int count = 0;
        try { count = (int)node.ChildCount; }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { yield break; }
        for (int i = 1; i <= count; i++)
        {
            dynamic? child = null;
            try { child = node.Child(i); }
            catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
            if (child is not null && ItemTypeOf(child) == itemType)
                yield return child!;
        }
    }

    private static dynamic RequireTinc(object? sysMan)
        => LookupTreeItemOrNull(sysMan!, "TINC")
           ?? throw new InvalidOperationException(
               "NC (TINC/Motion) root not found. The project has no Motion node.");

    // Resolve a caller-supplied NC-Task path by ENUMERATING TINC's
    // NC-Task children and matching PathName — never by reconstructing a
    // caret path (spec hard constraint).
    private static dynamic ResolveNcTask(object? sysMan, string ncTaskPath)
    {
        foreach (var task in ChildrenByType(RequireTinc(sysMan), NcItemType.NcTask))
            if (string.Equals(PathOf(task), ncTaskPath, StringComparison.OrdinalIgnoreCase))
                return task;
        throw new InvalidOperationException($"NC-Task not found by browsing: '{ncTaskPath}'.");
    }

    private static dynamic RequireAxesContainer(dynamic ncTask)
    {
        foreach (var c in ChildrenByType(ncTask, NcItemType.AxesList))
            return c;
        throw new InvalidOperationException(
            $"NC-Task '{PathOf(ncTask)}' has no axes container (ItemType {NcItemType.AxesList}).");
    }

    private static dynamic ResolveAxis(object? sysMan, string axisPath)
    {
        foreach (var task in ChildrenByType(RequireTinc(sysMan), NcItemType.NcTask))
        foreach (var container in ChildrenByType(task, NcItemType.AxesList))
        foreach (var axis in ChildrenByType(container, NcItemType.Axis))
            if (string.Equals(PathOf(axis), axisPath, StringComparison.OrdinalIgnoreCase))
                return axis;
        throw new InvalidOperationException($"Axis not found by browsing: '{axisPath}'.");
    }

    public IReadOnlyList<NcTaskEntry> ListNcTasksAxes() => ComBusyRetry.Run(() =>
    {
        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);

        dynamic? tinc = LookupTreeItemOrNull(sysMan, "TINC");
        if (tinc is null) return (IReadOnlyList<NcTaskEntry>)Array.Empty<NcTaskEntry>();

        var tasks = new List<NcTaskEntry>();
        foreach (var task in ChildrenByType(tinc, NcItemType.NcTask))
        {
            var axes = new List<NcAxisEntry>();
            foreach (var container in ChildrenByType(task, NcItemType.AxesList))
                foreach (var axis in ChildrenByType(container, NcItemType.Axis))
                    axes.Add(new NcAxisEntry(PathOf(axis), NameOf(axis)));
            tasks.Add(new NcTaskEntry(PathOf(task), NameOf(task), axes));
        }
        return (IReadOnlyList<NcTaskEntry>)tasks;
    }, OpenTimeout);

    public string CreateNcTask(string name) => ComBusyRetry.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("name is required.");

        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);
        dynamic tinc = RequireTinc(sysMan);

        // Infosys 242735499: TINC.CreateChild("NC-Task", 1).
        dynamic created = tinc.CreateChild(name.Trim(), NcItemType.CreateSubType);
        // Read the ACTUAL name/path back — TwinCAT may auto-suffix a duplicate.
        // NOTE: we deliberately do NOT File.SaveAll here. The mutation lives in
        // the in-memory XAE session; persistence is the caller's explicit
        // choice (twincat_save_project). This keeps integration runs from
        // permanently polluting the shared fixture project (there is no
        // NC-delete tool to undo it) — see the never-save-the-user's-project
        // safety rule and ConfigureEtherCatSlave's contrasting behaviour.
        return AfterMutation(() => PathOf(created)); // never replay the CreateChild
    }, OpenTimeout);

    public string CreateAxis(string ncTaskPath, string axisName) => ComBusyRetry.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(ncTaskPath))
            throw new InvalidOperationException("ncTaskPath is required.");
        if (string.IsNullOrWhiteSpace(axisName))
            throw new InvalidOperationException("axisName is required.");

        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);

        dynamic task = ResolveNcTask(sysMan, ncTaskPath);
        dynamic container = RequireAxesContainer(task);

        // Infosys 242735499: axes.CreateChild("Axis 1", 1).
        // Not persisted automatically — see CreateNcTask note.
        dynamic created = container.CreateChild(axisName.Trim(), NcItemType.CreateSubType);
        return AfterMutation(() => PathOf(created)); // never replay the CreateChild
    }, OpenTimeout);

    public string ConfigureAxis(string axisPath, IReadOnlyDictionary<string, string> fields)
        => ComBusyRetry.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(axisPath))
            throw new InvalidOperationException("axisPath is required.");
        if (fields is null || fields.Count == 0)
            throw new InvalidOperationException("At least one axis field is required.");

        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);
        dynamic axis = ResolveAxis(sysMan, axisPath);

        string current;
        try { current = (string)axis.ProduceXml(false); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ProduceXml failed on axis '{axisPath}': {ex.Message}", ex);
        }

        var (updated, applied) = NcAxisXml.ApplyFields(current, fields);

        try { axis.ConsumeXml(updated); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ConsumeXml failed on axis '{axisPath}': {ex.Message}", ex);
        }

        // Not persisted automatically — see CreateNcTask note.
        return string.Join(", ", applied);
    }, OpenTimeout);

    public NcAxisParams ReadAxisParams(string axisPath) => ComBusyRetry.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(axisPath))
            throw new InvalidOperationException("axisPath is required.");

        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);
        dynamic axis = ResolveAxis(sysMan, axisPath);

        string xml;
        try { xml = (string)axis.ProduceXml(false); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ProduceXml failed on axis '{axisPath}': {ex.Message}", ex);
        }

        return new NcAxisParams(xml, NcAxisXml.ReadFields(xml));
    }, OpenTimeout);
}
