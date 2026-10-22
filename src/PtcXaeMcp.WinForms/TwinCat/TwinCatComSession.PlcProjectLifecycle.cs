namespace PtcXaeMcp.WinForms.TwinCat;

// ---- plc-project-lifecycle ------------------------------------------------------
//
// PLC projects are the children of TIPC (ItemType 56). Infosys TE1000 "Wie ein PLC-Projekt angelegt
// wird" / "Bestehendes SPS-Projekt öffnen": ITcSmTreeItem.CreateChild on TIPC with
//   - a Beckhoff template NAME ("Standard PLC Template") or a .plcproj path   → new project (subType 0)
//   - an existing .plcproj, subType 0/1/2 = copy / move / in place            → project taken in
//   - a .tpzip, vInfo null                                                    → project imported
//   - a .tmc                                                                  → PLC instance only
// and ExportChild(name, .tpzip); ITcSysManager9.SaveAsArchive(.tszip); dte.Solution.AddFromTemplate(.tszip).
//
// Verified live against the neutral fixture on TC3 4026 (2026-10-07): all calls work. CreateChild
// IGNORES the name for a .plcproj (the project keeps its <Name>) and for a .tmc (the instance gets
// the module name); it honours it for a template and a .tpzip. Every failure (unknown template,
// name collision) raises a MODAL dialog before the COM error returns — the coordinator's pre-checks
// exist so these cases never get here.
internal sealed partial class TwinCatComSession
{
    private static readonly TimeSpan PlcProjectTimeout = TimeSpan.FromMinutes(3);

    public IReadOnlyList<string> ListPlcProjectNames() => ComBusyRetry.Run<IReadOnlyList<string>>(() =>
    {
        dynamic tipc = RequireTipc();
        var names = new List<string>();
        foreach (object child in Children(tipc))
            names.Add(NameOf(child));
        return names;
    }, OpenTimeout);

    public string CreatePlcProjectChild(string name, int subType, string source, bool vInfoNull) =>
        // A busy reject (RPC_E_CALL_REJECTED) means the call did not run, so the busy retry cannot replay a create.
        ComBusyRetry.Run(() =>
        {
            dynamic tipc = RequireTipc();
            dynamic? created;
            try
            {
                created = vInfoNull
                    ? tipc.CreateChild(name, subType, null, source)
                    : tipc.CreateChild(name, subType, string.Empty, source);
            }
            catch (Exception ex) when (ComCall.NotBusy(ex))
            {
                throw new InvalidOperationException(
                    $"TIPC.CreateChild('{name}', {subType}, …, '{source}') failed: {Innermost(ex)}", ex);
            }
            return created is null ? string.Empty : NameOf(created);
        }, PlcProjectTimeout);

    public void ExportPlcProject(string plcProjectName, string tpzipPath) => ComBusyRetry.Run(() =>
    {
        dynamic tipc = RequireTipc();
        try { tipc.ExportChild(plcProjectName, tpzipPath); }
        catch (Exception ex) when (ComCall.NotBusy(ex))
        {
            throw new InvalidOperationException(
                $"TIPC.ExportChild('{plcProjectName}', '{tpzipPath}') failed: {Innermost(ex)}", ex);
        }
    }, PlcProjectTimeout);

    public void SaveSolutionArchive(string tszipPath) => ComBusyRetry.Run(() =>
    {
        dynamic dte = GetActiveTcDte();
        dynamic sysMan = RequireSystemManager(dte);
        try { sysMan.SaveAsArchive(tszipPath); }
        catch (Exception ex) when (ComCall.NotBusy(ex))
        {
            throw new InvalidOperationException(
                $"ITcSysManager9.SaveAsArchive('{tszipPath}') failed: {Innermost(ex)}", ex);
        }
    }, PlcProjectTimeout);

    public string OpenSolutionArchive(string tszipPath, string destDir, string name) => ComBusyRetry.Run(() =>
    {
        dynamic dte = GetActiveTcDte();
        try { dte.Solution.AddFromTemplate(tszipPath, destDir, name, false); }
        catch (Exception ex) when (ComCall.NotBusy(ex))
        {
            throw new InvalidOperationException(
                $"dte.Solution.AddFromTemplate('{tszipPath}', '{destDir}', '{name}') failed: {Innermost(ex)}", ex);
        }
        string sln = ComCall.ReadProp(() => (string?)dte.Solution.FullName ?? string.Empty, string.Empty);
        if (!string.IsNullOrEmpty(sln)) SetExpectedSolutionPath(sln);
        return sln;
    }, OpenTimeout);

    private dynamic RequireTipc()
    {
        dynamic dte = GetActiveTcDte();
        object sysMan = RequireSystemManager(dte);
        return LookupTreeItemOrNull(sysMan, "TIPC")
            ?? throw new InvalidOperationException("TIPC (PLC root) not found in the open solution.");
    }

    // The late-bound call wraps the XAE's error in a TargetInvocationException whose own message
    // says nothing — report the innermost cause (same as CreateScopeProject).
    private static string Innermost(Exception ex)
    {
        Exception inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        return $"{inner.GetType().Name} hr=0x{inner.HResult:X8}: {inner.Message}";
    }
}
