namespace PtcXaeMcp.McpTests;

/// <summary>plc-project-lifecycle tools over the wire: argument contract, refusals before the XAE is touched.
/// All tools mutate, so they only run when the server reports no open solution.</summary>
public partial class McpServerTests
{
    [SkippableFact]
    public async Task PlcProjectCreate_WithoutSolution_IsAToolError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_plc_project_create",
            new Dictionary<string, object?> { ["name"] = "Plc2" });

        var text = GetText(result);
        Assert.DoesNotContain("An error occurred invoking", text);
        Assert.StartsWith("[Error]", text);
    }

    [SkippableFact]
    public async Task PlcProjectCreate_TemplatePathOutsideAllowedRoots_IsRefused()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_plc_project_create",
            new Dictionary<string, object?> { ["name"] = "Plc2", ["template"] = @"Z:\nowhere\Mine.plcproj" });

        var text = GetText(result);
        Assert.StartsWith("[Error] template:", text);
    }

    [SkippableFact]
    public async Task PlcProjectAddExisting_PathOutsideAllowedRoots_IsRefused()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_plc_project_add_existing",
            new Dictionary<string, object?> { ["path"] = @"Z:\nowhere\P.plcproj", ["mode"] = "COPY" });

        Assert.StartsWith("[Error] path:", GetText(result));
    }

    [SkippableFact]
    public async Task PlcProjectAddExisting_WithoutMode_IsAToolAnswer()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_plc_project_add_existing",
            new Dictionary<string, object?> { ["path"] = Path.Combine(Path.GetTempPath(), "missing.plcproj"), ["mode"] = "" });

        var text = GetText(result);
        Assert.DoesNotContain("An error occurred invoking", text);
        Assert.StartsWith("[Error]", text);
    }

    [SkippableFact]
    public async Task PlcProjectExport_PathOutsideAllowedRoots_IsRefused()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_plc_project_export",
            new Dictionary<string, object?> { ["plcProjectName"] = "P", ["tpzipPath"] = @"Z:\nowhere\P.tpzip" });

        Assert.StartsWith("[Error] tpzipPath:", GetText(result));
    }

    [SkippableFact]
    public async Task PlcProjectImport_PathOutsideAllowedRoots_IsRefused()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_plc_project_import",
            new Dictionary<string, object?> { ["tpzipPath"] = @"Z:\nowhere\P.tpzip", ["name"] = "P2" });

        Assert.StartsWith("[Error] tpzipPath:", GetText(result));
    }

    [SkippableFact]
    public async Task SolutionSaveArchive_PathOutsideAllowedRoots_IsRefused()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_solution_save_archive",
            new Dictionary<string, object?> { ["tszipPath"] = @"Z:\nowhere\S.tszip" });

        Assert.StartsWith("[Error] tszipPath:", GetText(result));
    }

    [SkippableFact]
    public async Task SolutionOpenArchive_MissingArchive_IsAToolError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_solution_open_archive",
            new Dictionary<string, object?>
            {
                ["tszipPath"] = Path.Combine(Path.GetTempPath(), "ptc-missing-archive.tszip"),
                ["destDir"] = Path.Combine(Path.GetTempPath(), "ptc-missing-archive-dest"),
                ["name"] = "Opened",
            });

        Assert.StartsWith("[Error] tszipPath: file not found", GetText(result));
    }

    [SkippableFact]
    public async Task PlcInstanceFromTmc_PathOutsideAllowedRoots_IsRefused()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_plc_instance_from_tmc",
            new Dictionary<string, object?> { ["tmcPath"] = @"Z:\nowhere\M.tmc" });

        Assert.StartsWith("[Error] tmcPath:", GetText(result));
    }
}
