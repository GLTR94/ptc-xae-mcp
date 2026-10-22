namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- plc-project-lifecycle ----
    /// <summary>Children of TIPC.</summary>
    public List<string> PlcProjects { get; } = ["FakePlc"];
    public Exception? CreatePlcProjectChildThrows { get; set; }
    public Exception? ExportPlcProjectThrows { get; set; }
    public Exception? SaveSolutionArchiveThrows { get; set; }
    public Exception? OpenSolutionArchiveThrows { get; set; }

    /// <summary>What CreatePlcProjectChild adds (null = the name passed, or the file name for an empty name).</summary>
    public string? CreatedChildName { get; set; }

    public IReadOnlyList<string> ListPlcProjectNames()
    {
        CallLog.Add("ListPlcProjectNames()");
        return PlcProjects.ToList();
    }

    public string CreatePlcProjectChild(string name, int subType, string source, bool vInfoNull)
    {
        CallLog.Add($"CreatePlcProjectChild({name},{subType},{source},vInfoNull={vInfoNull})");
        if (CreatePlcProjectChildThrows is not null) throw CreatePlcProjectChildThrows;
        string created = CreatedChildName ?? (name.Length > 0 ? name : Path.GetFileNameWithoutExtension(source));
        if (!NoOpMutations.Contains(nameof(CreatePlcProjectChild))) PlcProjects.Add(created);
        return created;
    }

    public void ExportPlcProject(string plcProjectName, string tpzipPath)
    {
        CallLog.Add($"ExportPlcProject({plcProjectName},{tpzipPath})");
        if (ExportPlcProjectThrows is not null) throw ExportPlcProjectThrows;
        if (!NoOpMutations.Contains(nameof(ExportPlcProject))) File.WriteAllText(tpzipPath, "tpzip");
    }

    public void SaveSolutionArchive(string tszipPath)
    {
        CallLog.Add($"SaveSolutionArchive({tszipPath})");
        if (SaveSolutionArchiveThrows is not null) throw SaveSolutionArchiveThrows;
        if (!NoOpMutations.Contains(nameof(SaveSolutionArchive))) File.WriteAllText(tszipPath, "tszip");
    }

    public string OpenSolutionArchive(string tszipPath, string destDir, string name)
    {
        CallLog.Add($"OpenSolutionArchive({tszipPath},{destDir},{name})");
        if (OpenSolutionArchiveThrows is not null) throw OpenSolutionArchiveThrows;
        if (NoOpMutations.Contains(nameof(OpenSolutionArchive))) return string.Empty;
        Directory.CreateDirectory(destDir);
        string sln = Path.Combine(destDir, name + ".sln");
        File.WriteAllText(sln, "sln");
        SolutionOpen = true;
        ActiveSlnPath = sln;
        return sln;
    }
}
