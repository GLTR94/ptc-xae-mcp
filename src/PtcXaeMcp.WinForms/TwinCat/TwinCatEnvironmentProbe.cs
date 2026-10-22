using PtcXaeMcp.WinForms.TwinCat.HostSelection;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed record TwinCatEnvironmentStatus(
    string ProjectName,
    string PlcProjectName,
    IReadOnlyList<string> PlcProjectNames,
    IReadOnlyList<string> MasterNames);

internal sealed record TwinCatProjectInfo(string ProjectName, object SystemManager);

internal static class TwinCatEnvironmentProbe
{
    public static TwinCatEnvironmentStatus Probe()
    {
        dynamic dte = GetActiveTcDte();
        TwinCatProjectInfo projectInfo = GetSystemManagerFromSolution(dte);
        List<string> plcProjectNames = GetPlcProjectNames(projectInfo.SystemManager);
        string plcProjectName = plcProjectNames.Count > 0 ? plcProjectNames[0] : "";
        List<string> masters = GetMasterNames(projectInfo.SystemManager);
        if (masters.Count == 0)
        {
            masters.Add("<no EtherCAT masters found>");
        }
        return new TwinCatEnvironmentStatus(
            string.IsNullOrWhiteSpace(projectInfo.ProjectName) ? "<unnamed>" : projectInfo.ProjectName,
            plcProjectName,
            plcProjectNames.Count > 0 ? plcProjectNames : ["<no PLC found>"],
            masters);
    }

    // Single attach path (issue #149 A-11): the session's. Busy/blocked propagates to the
    // caller's retry scope (TwinCatComSession.Probe wraps this in ComBusyRetry).
    private static dynamic GetActiveTcDte() =>
        TwinCatComSession.GetActiveTcDteOrNull()
        ?? throw new InvalidOperationException(TwinCatComSession.DescribeUnreachableXae());

    private static TwinCatProjectInfo GetSystemManagerFromSolution(dynamic dte)
    {
        dynamic projects = dte.Solution.Projects;
        int count = (int)projects.Count;
        for (int i = 1; i <= count; i++)
        {
            TwinCatProjectInfo? found = TryFindSystemManagerRecursive(projects.Item(i));
            if (found is not null) return found;
        }
        throw new InvalidOperationException("Kein TwinCAT Project (System Manager) in der aktuellen Solution gefunden.");
    }

    private static TwinCatProjectInfo? TryFindSystemManagerRecursive(dynamic project)
    {
        if (project is null)
        {
            return null;
        }

        string projectName = "";
        try { projectName = (string)project.Name; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        try
        {
            object? obj = project.Object as object;
            if (obj is not null && CanActAsSystemManager(obj))
            {
                return new TwinCatProjectInfo(projectName, obj);
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) {
        }
        try
        {
            dynamic items = project.ProjectItems;
            int count = (int)items.Count;
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    dynamic subProject = items.Item(i).SubProject;
                    TwinCatProjectInfo? found = TryFindSystemManagerRecursive(subProject);
                    if (found is not null) return found;
                }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) {
                }
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) {
        }
        return null;
    }

    private static bool CanActAsSystemManager(object obj)
    {
        try
        {
            dynamic dyn = obj;
            _ = dyn.LookupTreeItem("TIID");
            return true;
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) {
            return false;
        }
    }

    /// <summary>
    /// PLC project names, read from the DIRECT children of TIPC only (issue
    /// #154, A-6/A-15) — a PLC project shows up as a direct TIPC child named
    /// e.g. "Untitled1 Project" (NormalizePlcProjectName strips the " Project"/
    /// " Instance"/German equivalents suffix), never nested deeper than that.
    /// The previous implementation depth-first-walked the ENTIRE TIPC subtree
    /// (capped at 250 000 nodes) plus a second full walk of TIRS+TIPC (capped
    /// at 300 000 nodes each) just to collect this handful of names — on a
    /// large brownfield project that alone could take the environment probe
    /// well past what a periodic/initial status check should ever cost.
    /// </summary>
    private static List<string> GetPlcProjectNames(object sysMan)
    {
        var candidates = new List<string>();

        dynamic? tipc = LookupOrNull(sysMan, "TIPC");
        if (tipc is null) return candidates;

        int count = GetChildCountSafe(tipc);
        for (int i = 1; i <= count; i++)
        {
            dynamic? child = GetChildAtSafe(tipc, i);
            if (child is null) continue;
            string normalized = NormalizePlcProjectName(GetItemNameSafe(child));
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }
        }

        return candidates;
    }

    private static string NormalizePlcProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        if (name.EndsWith(" Instance", StringComparison.OrdinalIgnoreCase))
            name = name[..^" Instance".Length].Trim();
        if (name.EndsWith(" Instanz", StringComparison.OrdinalIgnoreCase))
            name = name[..^" Instanz".Length].Trim();
        if (name.EndsWith(" Project", StringComparison.OrdinalIgnoreCase))
            return name[..^" Project".Length].Trim();
        if (name.EndsWith(" Projekt", StringComparison.OrdinalIgnoreCase))
            return name[..^" Projekt".Length].Trim();
        if (string.Equals(name, "PLC Projekt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "PLC Project", StringComparison.OrdinalIgnoreCase))
            return "PLC";
        if (name.Contains("PLC", StringComparison.OrdinalIgnoreCase))
            return name.Trim();
        return "";
    }

    private static List<string> GetMasterNames(object sysMan)
    {
        var names = new List<string>();
        dynamic? ioRoot = LookupOrNull(sysMan, "TIID");
        if (ioRoot is null) return names;
        int count = GetChildCountSafe(ioRoot);
        for (int i = 1; i <= count; i++)
        {
            dynamic? child = GetChildAtSafe(ioRoot, i);
            if (child is null) continue;
            int subtype = GetItemSubTypeSafe(child);
            if (subtype == 111)
            {
                string name = GetItemNameSafe(child);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static dynamic? LookupOrNull(object sysMan, string path)
    {
        try { return ((dynamic)sysMan).LookupTreeItem(path); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return null; }
    }

    private static int GetChildCountSafe(dynamic item)
    {
        try { return (int)item.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return 0; }
    }

    private static dynamic? GetChildAtSafe(dynamic parent, int index)
    {
        try { return parent.Child(index); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return null; }
    }

    private static int GetItemSubTypeSafe(dynamic item)
    {
        try { return (int)item.ItemSubType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        try { return (int)item.SubType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        return -1;
    }

    private static string GetItemNameSafe(dynamic item)
    {
        try
        {
            string value = (string)item.Name;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) {
        }
        try
        {
            string value = (string)item.ItemName;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) {
        }
        try
        {
            string path = (string)item.PathName;
            if (!string.IsNullOrWhiteSpace(path)) return path.Split('^')[^1];
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) {
        }
        return "<unknown>";
    }
}
