using System.Globalization;
using System.Text.Json;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Live tests of the hmi-views group against a REAL XAE with the
/// TEMP copy of <see cref="HmiFixture"/>. Writes go through the engineer popup: with a host
/// started with PTC_XAE_MCP_HMI_AUTOCONFIRM_TEMP=1 (and
/// <see cref="HmiFixture.ExpectAutoConfirmEnvVar"/>=1) they are auto-confirmed for the TEMP
/// copy and must succeed with a read-back; otherwise nobody answers and they must expire
/// without writing anything.
/// </summary>
[Collection("Stage HMI")]
[Trait("Stage", "HMI")]
public class HmiViewsIntegrationTests(HmiFixture fx)
{
    // Unique per test run: the fixture copy may be reused within 12 h.
    private static readonly string Run = DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture);

    private static JsonElement Json(string text) =>
        HmiFixture.JsonAfterSummary(text) ?? throw new Xunit.Sdk.XunitException("Expected JSON after the summary, got: " + text);

    [SkippableFact(Timeout = 180_000)]
    public async Task ReadFile_StartupView_IsATcHmiView()
    {
        fx.Guard();
        string info = await fx.CallAsync("twincat_hmi_get_project_info", new() { ["hmiProject"] = fx.ProjectName });
        string startup = Json(info).GetProperty("config").GetProperty("StartupView").GetString()!;

        string text = await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = startup, ["hmiProject"] = fx.ProjectName });

        JsonElement json = Json(text);
        Assert.Contains("TcHmi.Controls.System.TcHmiView", json.GetProperty("source").GetString());
        Assert.True(json.GetProperty("lines").GetInt32() > 0);
    }

    [SkippableFact(Timeout = 240_000)]
    public async Task AddItem_Html_HasItsOwnName_AndWriteFileReadsBack()
    {
        fx.Guard();
        string name = "McpItDoc" + Run, path = $"McpIt/{name}.html";
        string add = await fx.CallAsync("twincat_hmi_add_item", new()
        {
            ["kind"] = "html", ["folder"] = "McpIt", ["name"] = name, ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(1));

        if (!HmiFixture.ExpectAutoConfirm)
        {
            Assert.StartsWith("[Error] Not confirmed within 10 s", add);
            Assert.StartsWith("[Error]", await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = path }));
            return;
        }

        // hmi-views V7: the TE2000 template would have created "<name>.html.html".
        Assert.StartsWith($"Created '{path}' as an empty file (imported)", add);
        Assert.StartsWith($"'{path}'", await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = path }));
        Assert.StartsWith("[Error]", await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = path + ".html" }));

        string write = await fx.CallAsync("twincat_hmi_write_file", new()
        {
            ["path"] = path, ["source"] = "<!doctype html><title>MCP integration</title>", ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(1));
        Assert.StartsWith($"Wrote '{path}'", write);
        Assert.Contains("verified by read-back", write);

        string again = await fx.CallAsync("twincat_hmi_write_file", new()
        {
            ["path"] = path, ["source"] = "<!doctype html><title>MCP integration</title>",
        });
        Assert.StartsWith("Nothing to do", again);
    }

    [SkippableFact(Timeout = 240_000)]
    public async Task AddItem_UserControl_BringsItsJsonWithTheProjectSchema()
    {
        fx.Guard();
        string name = "McpItUc" + Run;
        string add = await fx.CallAsync("twincat_hmi_add_item", new()
        {
            ["kind"] = "usercontrol", ["folder"] = "UserControls/McpIt", ["name"] = name, ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(1));

        if (!HmiFixture.ExpectAutoConfirm)
        {
            Assert.StartsWith("[Error] Not confirmed within 10 s", add);
            return;
        }

        Assert.StartsWith($"Created 'UserControls/McpIt/{name}.usercontrol' from template UserControl", add);
        string json = await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = $"UserControls/McpIt/{name}.usercontrol.json" });
        Assert.Contains("\"$schema\"", Json(json).GetProperty("source").GetString());
    }

    [SkippableFact(Timeout = 240_000)]
    public async Task DeleteItem_RemovesWhatAddItemCreated_FromProjectAndDisk()
    {
        fx.Guard();
        string refused = await fx.CallAsync("twincat_hmi_delete_item", new() { ["path"] = "Server" });
        Assert.StartsWith("[Error] 'Server' is part of the project infrastructure", refused);

        if (!HmiFixture.ExpectAutoConfirm)
            throw new SkipException($"Needs a host started with PTC_XAE_MCP_HMI_AUTOCONFIRM_TEMP=1 and " +
                                    $"{HmiFixture.ExpectAutoConfirmEnvVar}=1 (nobody answers the popup in an unattended run).");

        string name = "McpItDel" + Run, path = $"McpIt/{name}.txt";
        string add = await fx.CallAsync("twincat_hmi_add_item", new()
        {
            ["kind"] = "text", ["folder"] = "McpIt", ["name"] = name, ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(1));
        Assert.StartsWith($"Created '{path}'", add);
        string onDisk = Path.Combine(fx.ProjectDirectory, "McpIt", name + ".txt");
        Assert.True(File.Exists(onDisk), onDisk);

        string delete = await fx.CallAsync("twincat_hmi_delete_item", new()
        {
            ["path"] = path, ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(1));

        Assert.StartsWith($"Deleted '{path}' (file)", delete);
        Assert.Contains("verified: no longer in the project", delete);
        Assert.StartsWith("[Error]", await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = path }));
        Assert.False(File.Exists(onDisk), "DeleteChild should remove the file from the disk too: " + onDisk);

        string again = await fx.CallAsync("twincat_hmi_delete_item", new() { ["path"] = path });
        Assert.StartsWith($"[Error] '{path}' not found", again);
    }

    // #247: TE2000 raises a WPF thread error for a .js although the item was added. The import counts
    // only with the file in the project and — when that error came — byte-identical on disk; the session
    // makes TE2000 rewrite tchmiconfig.json, which lists the file as JavaScript.
    [SkippableFact(Timeout = 240_000)]
    public async Task ImportFile_Js_LandsVerifiedAndIsListedAsJavaScript()
    {
        fx.Guard();
        if (!HmiFixture.ExpectAutoConfirm)
            throw new SkipException($"Needs a host started with PTC_XAE_MCP_HMI_AUTOCONFIRM_TEMP=1 and " +
                                    $"{HmiFixture.ExpectAutoConfirmEnvVar}=1 (nobody answers the popup in an unattended run).");

        string name = "mcpit-lib-" + Run + ".js", path = "McpIt/" + name;
        string src = TempSource(name, "window.McpItLib = '" + Run + "';\n");

        string import = await fx.CallAsync("twincat_hmi_import_file", new()
        {
            ["sourcePath"] = src, ["folder"] = "McpIt", ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(2));

        Assert.StartsWith("Imported", import);
        JsonElement json = Json(import);
        if (json.GetProperty("engineError").ValueKind != JsonValueKind.Null)
            Assert.True(json.GetProperty("verifiedOnDisk").GetBoolean(), import);
        Assert.Equal("JavaScript", json.GetProperty("dependencyType").GetString());
        Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(Path.Combine(fx.ProjectDirectory, "McpIt", name)));
        Assert.StartsWith($"'{path}'", await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = path }));
    }

    // The .js import makes TE2000 rewrite tchmiconfig.json from its in-memory model (#247/#248). What went
    // in through the model — here an internal symbol via AddInternalSymbol (#165) — must survive that.
    [SkippableFact(Timeout = 300_000)]
    public async Task ImportFile_Js_KeepsAnInternalSymbolAddedThroughTheModel()
    {
        fx.Guard();
        if (!HmiFixture.ExpectAutoConfirm)
            throw new SkipException($"Needs a host started with PTC_XAE_MCP_HMI_AUTOCONFIRM_TEMP=1 and " +
                                    $"{HmiFixture.ExpectAutoConfirmEnvVar}=1 (nobody answers the popup in an unattended run).");

        string symbol = "McpItKeep" + Run;
        string add = await fx.CallAsync("twincat_hmi_add_internal_symbol", new()
        {
            ["name"] = symbol, ["dataType"] = "tchmi:general#/definitions/Number", ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(2));
        Assert.StartsWith($"Client internal symbol %i%{symbol}%/i% added", add);

        string name = "mcpit-keep-" + Run + ".js";
        string import = await fx.CallAsync("twincat_hmi_import_file", new()
        {
            ["sourcePath"] = TempSource(name, "window.McpItKeep = 1;\n"), ["folder"] = "McpIt", ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(2));
        Assert.StartsWith("Imported", import);
        Assert.Equal("JavaScript", Json(import).GetProperty("dependencyType").GetString());

        string list = await fx.CallAsync("twincat_hmi_list_internal_symbols", new() { ["hmiProject"] = fx.ProjectName });
        Assert.Contains(Json(list).GetProperty("client").GetProperty("symbols").EnumerateArray(),
            s => s.GetProperty("name").GetString() == symbol);

        await fx.CallAsync("twincat_hmi_remove_internal_symbol", new() { ["name"] = symbol, ["confirmTimeoutSeconds"] = 10 },
            TimeSpan.FromMinutes(2));
    }

    private static string TempSource(string name, string content)
    {
        string src = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp-it-" + Run, name);
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, content);
        return src;
    }

    [SkippableFact(Timeout = 900_000)]
    public async Task ApplyBundle_MiniPackage_ThenBuild()
    {
        fx.Guard();
        string dir = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp-hmi-bundle-" + Run);
        Directory.CreateDirectory(dir);
        string page = $"McpItPage{Run}";
        File.WriteAllText(Path.Combine(dir, "doc.html"), "<!doctype html><title>MCP bundle</title><p>Integration</p>");
        File.WriteAllText(Path.Combine(dir, "page.content"),
            $"<div id=\"{page}\" data-tchmi-type=\"TcHmi.Controls.System.TcHmiContent\" data-tchmi-top=\"0\" data-tchmi-left=\"0\" " +
            "data-tchmi-width=\"800\" data-tchmi-height=\"600\">\n</div>\n");
        string manifest = Path.Combine(dir, "bundle.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            name = "McpItBundle",
            entries = new object[]
            {
                new { path = $"McpIt/Bundle{Run}/doc.html", kind = "html", source = "doc.html" },
                new { path = $"McpIt/Bundle{Run}/{page}.content", kind = "content", source = "page.content" },
            },
        }));

        string apply = await fx.CallAsync("twincat_hmi_apply_bundle", new()
        {
            ["manifestPath"] = manifest, ["confirmTimeoutSeconds"] = 10,
        }, TimeSpan.FromMinutes(2));

        if (!HmiFixture.ExpectAutoConfirm)
        {
            Assert.StartsWith("[Error] Not confirmed within 10 s", apply);
            return;
        }

        Assert.StartsWith($"Applied bundle 'McpItBundle' to '{fx.ProjectName}': 2 file(s)", apply);
        string doc = await fx.CallAsync("twincat_hmi_read_file", new() { ["path"] = $"McpIt/Bundle{Run}/doc.html" });
        Assert.Contains("<p>Integration</p>", Json(doc).GetProperty("source").GetString());

        string build = await fx.CallAsync("twincat_hmi_build", new() { ["hmiProject"] = fx.ProjectName }, TimeSpan.FromMinutes(12));
        Assert.StartsWith($"Built HMI project '{fx.ProjectName}'", build);
    }
}
