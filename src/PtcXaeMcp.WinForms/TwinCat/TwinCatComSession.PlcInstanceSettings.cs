namespace PtcXaeMcp.WinForms.TwinCat;

// ---- plc-instance-settings ----------------------------------------------------------
//
// Generic tree-item XML access used by PlcInstanceSettingsCoordinator: list children by ItemType,
// ProduceXml(false) and ConsumeXml on a node looked up fresh for every call, so a read-back never
// reuses the COM node a write went through. Verified live on TC3 4026 (2026-10-08): ConsumeXml
// returns without error for content it does not apply (unknown symbol, non-init symbol, Infosys
// <Instance><SymbolInit> form) — only the read-back proves a write.
internal sealed partial class TwinCatComSession
{
    public IReadOnlyList<TreeChildInfo> ListTreeChildren(string treePath) => ComBusyRetry.Run<IReadOnlyList<TreeChildInfo>>(() =>
    {
        dynamic node = RequireTreeItem(treePath);
        var list = new List<TreeChildInfo>();
        int count = ChildCountOf(node);
        for (int i = 1; i <= count; i++)
        {
            dynamic? child = ChildAt(node, i);
            if (child is null) continue;
            list.Add(new TreeChildInfo(NameOf(child), ItemTypeOf(child)));
        }
        return list;
    }, OpenTimeout);

    public string ProduceTreeItemXml(string treePath) => ComBusyRetry.Run(() =>
    {
        dynamic node = RequireTreeItem(treePath);
        try { return (string)node.ProduceXml(false); }
        catch (Exception ex) when (ComCall.NotBusy(ex))
        {
            throw new InvalidOperationException($"ProduceXml failed on '{treePath}': {Innermost(ex)}", ex);
        }
    }, OpenTimeout);

    public void ConsumeTreeItemXml(string treePath, string xml) => ComBusyRetry.Run(() =>
    {
        dynamic node = RequireTreeItem(treePath);
        try { node.ConsumeXml(xml); }
        catch (Exception ex) when (ComCall.NotBusy(ex))
        {
            throw new InvalidOperationException($"ConsumeXml failed on '{treePath}': {Innermost(ex)}", ex);
        }
    }, OpenTimeout);

    private dynamic RequireTreeItem(string treePath)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            throw new InvalidOperationException("treePath is required.");
        dynamic dte = GetActiveTcDte();
        object sysMan = RequireSystemManager(dte);
        return LookupTreeItemOrNull(sysMan, treePath)
            ?? throw new InvalidOperationException($"Tree path not found: {treePath}");
    }
}
