using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>PLC code tools: POU/GVL read/write, members, delete/rename, PLCopen, editor open/close.</summary>
public partial class McpServerTests
{
    // ── Test 19 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T19_ListPous_RunsAndReturnsString()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_list_pous");

        var text = GetText(result);
        bool ok = text.StartsWith("Found", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("No POUs", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(ok, $"Unexpected response: {text}");
        if (result.IsError == true)
        {
            Assert.StartsWith("[Error]", text, StringComparison.Ordinal);
            Assert.True(text.Length > 10, $"Error response must not be empty: '{text}'");
        }
    }

    // ── Test 20 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T20_ReadPou_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_pou",
            new Dictionary<string, object?> { ["treePath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 21 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T21_ReadPou_UnknownPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_pou",
            new Dictionary<string, object?> { ["treePath"] = "TIPC^Nope^DoesNotExist" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    // ── Test 22 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T22_ReadGvl_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_gvl",
            new Dictionary<string, object?> { ["treePath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 23 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T23_ReadGvl_UnknownPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_gvl",
            new Dictionary<string, object?> { ["treePath"] = "TIPC^Nope^DoesNotExist" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    // ── Test 24 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T24_WritePouBody_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_write_pou_body",
            new Dictionary<string, object?>
            {
                ["treePath"] = "",
                ["implementation"] = ";",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 25 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T25_WritePouBody_BothSectionsNull_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_write_pou_body",
            new Dictionary<string, object?>
            {
                ["treePath"] = "TIPC^PLC^PLC Projekt^POUs^Nope",
                // declaration and implementation both omitted → both null
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("At least one", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 26 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T26_WritePouBody_UnknownPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_write_pou_body",
            new Dictionary<string, object?>
            {
                ["treePath"] = "TIPC^Nope^DoesNotExist",
                ["implementation"] = ";",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    // ── Test 27 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T27_CreatePou_EmptyParent_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_create_pou",
            new Dictionary<string, object?>
            {
                ["parentPath"] = "",
                ["name"] = "FB_X",
                ["pouType"] = "FUNCTION_BLOCK",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("parentPath", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 28 ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T28_CreatePou_UnknownType_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_create_pou",
            new Dictionary<string, object?>
            {
                ["parentPath"] = "TIPC^PLC^PLC Projekt^POUs",
                ["name"] = "FB_X",
                ["pouType"] = "INTERFACE",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("Unknown pouType", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 43 — pou-handling: list_pou_members empty path ────────────────────

    [SkippableFact]
    public async Task T43_ListPouMembers_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_list_pou_members",
            new Dictionary<string, object?> { ["pouPath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("pouPath", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 44 — add_pou_member unknown kind ──────────────────────────────────

    [SkippableFact]
    public async Task T44_AddPouMember_UnknownKind_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_add_pou_member",
            new Dictionary<string, object?>
            {
                ["pouPath"] = "TIPC^PLC^PLC Projekt^POUs^FB_Nope",
                ["kind"] = "WIDGET",
                ["name"] = "X",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("WIDGET", text);
    }

    // ── Test 45 — read_pou_member empty path ───────────────────────────────────

    [SkippableFact]
    public async Task T45_ReadPouMember_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_read_pou_member",
            new Dictionary<string, object?> { ["memberPath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("memberPath", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 46 — write_pou_member needs a section ─────────────────────────────

    [SkippableFact]
    public async Task T46_WritePouMember_NoSections_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_write_pou_member",
            new Dictionary<string, object?>
            {
                ["memberPath"] = "TIPC^PLC^PLC Projekt^POUs^FB_Nope^Calc",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    // ── Test 47 — delete_plc_object empty path ─────────────────────────────────

    [SkippableFact]
    public async Task T47_DeletePlcObject_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_delete_plc_object",
            new Dictionary<string, object?> { ["treePath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
    }

    // ── Test 48 — rename_plc_object empty newName ──────────────────────────────

    [SkippableFact]
    public async Task T48_RenamePlcObject_EmptyNewName_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_rename_plc_object",
            new Dictionary<string, object?>
            {
                ["treePath"] = "TIPC^PLC^PLC Projekt^POUs^FB_Nope",
                ["newName"] = "",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("newName", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 49 — plcopen_export missing selector ──────────────────────────────

    [SkippableFact]
    public async Task T49_PlcOpenExport_MissingSelector_ReturnsError()
    {
        SkipIfNotReachable();

        // filePath must resolve under an allowed PathPolicy root (#173) so this test
        // actually reaches the selector check instead of failing on the path itself.
        var filePath = Path.Combine(Path.GetTempPath(), "export_" + Guid.NewGuid().ToString("N") + ".xml");
        var result = await CallToolAsync("twincat_plcopen_export",
            new Dictionary<string, object?>
            {
                ["filePath"] = filePath,
                ["selector"] = "",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("selector", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 50 — plcopen_import unknown conflictMode ──────────────────────────

    [SkippableFact]
    public async Task T50_PlcOpenImport_UnknownConflictMode_ReturnsError()
    {
        SkipIfNotReachable();

        // filePath must resolve under an allowed PathPolicy root (#173) so this test
        // actually reaches the conflictMode check instead of failing on the path itself.
        var filePath = Path.Combine(Path.GetTempPath(), "import_" + Guid.NewGuid().ToString("N") + ".xml");
        var result = await CallToolAsync("twincat_plcopen_import",
            new Dictionary<string, object?>
            {
                ["filePath"] = filePath,
                ["conflictMode"] = "clobber",
            });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("clobber", text);
    }

    // ── Test 51 — live smoke: list_pou_members runs end-to-end ─────────────────
    // Per the feedback_mcp_smoke_test_pattern memory: one live [Fact] that drives
    // the tool through the running server against whatever project is open.

    [SkippableFact]
    public async Task T51_ListPouMembers_LiveSmoke_ReturnsString()
    {
        SkipIfNotReachable();

        // Unknown path → must come back as a clean [Error], never empty / hang.
        var result = await CallToolAsync("twincat_list_pou_members",
            new Dictionary<string, object?>
            {
                ["pouPath"] = "TIPC^PLC^PLC Projekt^POUs^FB_DoesNotExist_Smoke",
            });

        var text = GetText(result);
        bool ok = text.StartsWith("[Error]", StringComparison.Ordinal)
            || text.StartsWith("Found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no METHOD/ACTION/PROPERTY", StringComparison.OrdinalIgnoreCase);
        Assert.True(ok, $"Unexpected response: {text}");
        Assert.True(text.Length > 5, $"Response swallowed: '{text}'");
    }

    // ── visible-for-video ───────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Vfv_OpenPlcObject_IsListed()
    {
        SkipIfNotReachable();

        var tools = await _client!.ListToolsAsync();
        Assert.Contains("twincat_open_plc_object", tools.Select(t => t.Name).ToHashSet());
    }

    [SkippableFact]
    public async Task Vfv_OpenPlcObject_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_open_plc_object",
            new Dictionary<string, object?> { ["treePath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Vfv_OpenPlcObject_UnknownPath_ReturnsError()
    {
        SkipIfNotReachable();

        // No solution / unresolved path must come back as a clean structured
        // [Error], never an empty string or a transport fault.
        var result = await CallToolAsync("twincat_open_plc_object",
            new Dictionary<string, object?> { ["treePath"] = "TIPC^Nope^DoesNotExist" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.True(text.Length > 8, $"Error must be structured: '{text}'");
    }

    // ── open-object (generalization of the visible-for-video tools) ─────────────

    [SkippableFact]
    public async Task OpenObject_AndCloseObject_AreListed()
    {
        SkipIfNotReachable();

        var names = (await _client!.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        Assert.Contains("twincat_open_object", names);
        Assert.Contains("twincat_close_object", names);
        // Deprecated aliases stay listed for back-compat.
        Assert.Contains("twincat_open_plc_object", names);
        Assert.Contains("twincat_close_plc_object", names);
    }

    [SkippableFact]
    public async Task OpenObject_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_open_object",
            new Dictionary<string, object?> { ["treePath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task CloseObject_EmptyPath_ReturnsError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_close_object",
            new Dictionary<string, object?> { ["treePath"] = "" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("treePath", text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task OpenObject_UnknownPath_ReturnsStructuredError()
    {
        SkipIfNotReachable();

        // An unresolvable, non-TIID/TIPC path: the leaf can't be type-resolved and is not
        // in the SE tree, so the UIAutomation select fails → clean structured [Error],
        // never an empty string or a transport fault. (A 2-segment TIID^* path would be
        // type-resolved to the EtherCAT master by LookupTreeItemWithFallback, so it is
        // deliberately NOT used here.)
        var result = await CallToolAsync("twincat_open_object",
            new Dictionary<string, object?> { ["treePath"] = "ZZZ^NoSuchNode 9999" });

        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.True(text.Length > 8, $"Error must be structured: '{text}'");
    }
}
