namespace PtcXaeMcp.WinForms.TwinCat;

// hmi-ads-tmc #166 — the PLC projects of the open solution with the raw XML their TMC path
// lives in. Same walk as TE2000's "Browse solution for TMC file" dialog (decompiled:
// BrowseSolutionForTmcFileWindow): every solution project whose Object is an ITcSysManager →
// LookupTreeItem("TIPC") → each child's ProduceXml. Plain system-manager COM, no TcHmiAutomation,
// so it is not behind TCHMI. Read-only; the whole walk is idempotent, so a busy XAE retries it.
// NOT verified live (written without access to the XAE): in particular whether ProduceXml(false)
// already carries PlcProjectDef — hence the recursive fallback below.
internal sealed partial class TwinCatComSession
{
    public IReadOnlyList<PlcProjectXml> ListPlcProjectXml() =>
        ComBusyRetry.Run(() =>
        {
            dynamic dte = GetActiveTcDte();
            var result = new List<PlcProjectXml>();
            dynamic? projects = ComCall.ReadProp<object?>(() => dte.Solution.Projects, null);
            if (projects is null) return result;

            int count = ComCall.ReadProp(() => (int)projects.Count, 0);
            for (int i = 1; i <= count; i++)
            {
                dynamic? project = ComCall.ReadProp<object?>(() => projects.Item(i), null);
                if (project is null) continue;
                CollectPlcProjectXml(project, result);

                // A TwinCAT project inside a solution folder is a SubProject of the folder's items.
                dynamic? items = ComCall.ReadProp<object?>(() => project.ProjectItems, null);
                if (items is null) continue;
                int itemCount = ComCall.ReadProp(() => (int)items.Count, 0);
                for (int j = 1; j <= itemCount; j++)
                {
                    dynamic? sub = ComCall.ReadProp<object?>(() => items.Item(j).SubProject, null);
                    if (sub is not null) CollectPlcProjectXml(sub, result);
                }
            }
            return result;
        }, OpenTimeout);

    private static void CollectPlcProjectXml(dynamic project, List<PlcProjectXml> result)
    {
        object? sysMan = ComCall.ReadProp<object?>(() => project.Object as object, null);
        if (sysMan is null || !CanLookupTreeItem(sysMan)) return;

        dynamic? tipc = LookupTreeItemOrNull(sysMan, "TIPC");
        if (tipc is null) return;

        string tcProject = ComCall.ReadProp(() => (string?)project.Name ?? string.Empty, string.Empty);
        string? tcProjectFile = ComCall.ReadProp<string?>(() => (string?)project.FullName, null);

        foreach (object child in Children(tipc))
        {
            string plcProject = NameOf(child);
            // The same TwinCAT project reached twice (top level and as a SubProject) is listed once.
            if (result.Any(r => string.Equals(r.TcProjectFile, tcProjectFile, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(r.TcProject, tcProject, StringComparison.Ordinal)
                             && string.Equals(r.PlcProject, plcProject, StringComparison.Ordinal)))
                continue;
            string? xml = null;
            string? error = null;
            try
            {
                xml = (string?)((dynamic)child).ProduceXml(false);
                if (xml is null || !xml.Contains("<" + HmiTmcCandidates.PlcProjectDefElement, StringComparison.Ordinal))
                    xml = (string?)((dynamic)child).ProduceXml(true);
            }
            catch (Exception ex) when (ComCall.NotBusy(ex))
            {
                error = $"ProduceXml failed on TIPC^{plcProject}: {ex.Message}";
                xml = null;
            }
            result.Add(new PlcProjectXml(tcProject, string.IsNullOrWhiteSpace(tcProjectFile) ? null : tcProjectFile,
                plcProject, xml, error));
        }
    }
}
