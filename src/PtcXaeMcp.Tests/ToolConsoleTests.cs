using System.Text.Json;
using System.Text.Json.Nodes;
using PtcXaeMcp.WinForms.Mcp;
using PtcXaeMcp.WinForms.ToolConsole;

namespace PtcXaeMcp.Tests;

/// <summary>Tool console logic (issue #271): schema to fields, fields to arguments, grouping, presets.</summary>
public class ToolConsoleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ptc-console-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly string SampleSchema = """
        {
          "type": "object",
          "properties": {
            "slnPath": { "type": "string", "description": "Path to the solution" },
            "force": { "type": "boolean", "default": true },
            "dry": { "type": "boolean" },
            "mode": { "type": "string", "enum": ["a", "b"], "default": "b" },
            "level": { "type": "integer", "enum": [1, 2] },
            "count": { "type": "integer" },
            "ratio": { "type": ["number", "null"] },
            "name": { "anyOf": [ { "type": "null" }, { "type": "string" } ] },
            "items": { "type": "array" },
            "opts": { "type": "object" },
            "any": {}
          },
          "required": ["slnPath", "count"]
        }
        """;

    // ── schema → fields ──────────────────────────────────────────────────

    [Fact]
    public void FromSchema_MapsEachTypeInSchemaOrder()
    {
        var fields = ToolFormModel.FromSchema(Schema(SampleSchema));
        fields.Select(f => (f.Name, f.Kind)).Should().Equal(
            ("slnPath", ToolFieldKind.Text),
            ("force", ToolFieldKind.Boolean),
            ("dry", ToolFieldKind.Boolean),
            ("mode", ToolFieldKind.Choice),
            ("level", ToolFieldKind.Choice),
            ("count", ToolFieldKind.Integer),
            ("ratio", ToolFieldKind.Number),
            ("name", ToolFieldKind.Text),
            ("items", ToolFieldKind.Json),
            ("opts", ToolFieldKind.Json),
            ("any", ToolFieldKind.Json));
    }

    [Fact]
    public void FromSchema_ReadsRequiredDescriptionDefaultsAndEnums()
    {
        var fields = ToolFormModel.FromSchema(Schema(SampleSchema)).ToDictionary(f => f.Name);
        fields["slnPath"].Required.Should().BeTrue();
        fields["slnPath"].Description.Should().Be("Path to the solution");
        fields["count"].Required.Should().BeTrue();
        fields["dry"].Required.Should().BeFalse();
        fields["force"].DefaultText.Should().Be("true");
        fields["mode"].DefaultText.Should().Be("b");
        fields["mode"].Choices.Should().Equal("a", "b");
        fields["mode"].ChoiceBase.Should().Be(ToolFieldKind.Text);
        fields["level"].Choices.Should().Equal("1", "2");
        fields["level"].ChoiceBase.Should().Be(ToolFieldKind.Integer);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"type":"object"}""")]
    [InlineData("""{"type":"object","properties":{}}""")]
    public void FromSchema_NoProperties_YieldsNoFields(string schema) =>
        ToolFormModel.FromSchema(Schema(schema)).Should().BeEmpty();

    [Fact]
    public void InitialValues_UsesDefaults_BooleansFallBackToFalse()
    {
        var fields = ToolFormModel.FromSchema(Schema(SampleSchema));
        var initial = ToolFormModel.InitialValues(fields);
        initial["force"].Should().Be("true");
        initial["dry"].Should().Be("false");
        initial["mode"].Should().Be("b");
        initial["slnPath"].Should().BeEmpty();
    }

    // ── fields → arguments ───────────────────────────────────────────────

    private static ToolArgumentsResult Build(IReadOnlyDictionary<string, string?> values) =>
        ToolArgumentBuilder.Build(ToolFormModel.FromSchema(Schema(SampleSchema)), values);

    [Fact]
    public void Build_ConvertsTypes()
    {
        var r = Build(new Dictionary<string, string?>
        {
            ["slnPath"] = @"C:\x\a.sln",
            ["force"] = "false",
            ["dry"] = "true",
            ["mode"] = "a",
            ["level"] = "2",
            ["count"] = " 42 ",
            ["ratio"] = "1.5",
            ["name"] = "n",
            ["items"] = "[1,2]",
            ["opts"] = """{"k":true}""",
        });
        r.IsValid.Should().BeTrue();
        r.Arguments["slnPath"]!.GetValue<string>().Should().Be(@"C:\x\a.sln");
        r.Arguments["force"]!.GetValue<bool>().Should().BeFalse();
        r.Arguments["dry"]!.GetValue<bool>().Should().BeTrue();
        r.Arguments["mode"]!.GetValue<string>().Should().Be("a");
        r.Arguments["level"]!.GetValue<long>().Should().Be(2);
        r.Arguments["count"]!.GetValue<long>().Should().Be(42);
        r.Arguments["ratio"]!.GetValue<double>().Should().Be(1.5);
        r.Arguments["items"]!.AsArray().Count.Should().Be(2);
        r.Arguments["opts"]!["k"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Build_OmitsEmptyOptionalFields_AndUnchangedBooleans()
    {
        var r = Build(new Dictionary<string, string?>
        {
            ["slnPath"] = "s",
            ["count"] = "1",
            ["force"] = "true",   // equals the default: omitted
            ["dry"] = "false",    // equals the implicit default: omitted
            ["mode"] = "",
            ["ratio"] = "  ",
            ["items"] = "",
        });
        r.IsValid.Should().BeTrue();
        r.Arguments.Select(p => p.Key).Should().BeEquivalentTo("slnPath", "count");
    }

    [Fact]
    public void Build_ReportsMissingRequiredAndInvalidValues()
    {
        var r = Build(new Dictionary<string, string?>
        {
            ["slnPath"] = "",
            ["count"] = "abc",
            ["ratio"] = "x",
            ["items"] = "[1,",
        });
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.StartsWith("slnPath: required"));
        r.Errors.Should().Contain(e => e.StartsWith("count:") && e.Contains("not an integer"));
        r.Errors.Should().Contain(e => e.StartsWith("ratio:") && e.Contains("not a number"));
        r.Errors.Should().Contain(e => e.StartsWith("items: invalid JSON"));
    }

    [Fact]
    public void Build_RejectsFractionForInteger()
    {
        var r = Build(new Dictionary<string, string?> { ["slnPath"] = "s", ["count"] = "1.5" });
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ToFieldValues_RoundTripsArgumentsAndReportsUnknown()
    {
        var fields = ToolFormModel.FromSchema(Schema(SampleSchema));
        var args = JsonNode.Parse("""{"slnPath":"s","force":false,"count":7,"opts":{"a":1},"stale":1}""")!.AsObject();
        var replay = ToolArgumentBuilder.Replay(fields, args);
        var values = replay.Values;
        values["slnPath"].Should().Be("s");
        values["force"].Should().Be("false");
        values["count"].Should().Be("7");
        JsonNode.Parse(values["opts"])!["a"]!.GetValue<int>().Should().Be(1);
        replay.Unknown.Should().Equal("stale");
        values.Should().NotContainKey("stale"); // unknown keys are never pre-filled
        replay.Incomplete.Should().BeTrue();
        replay.HasMismatch.Should().BeTrue();
        replay.Warnings.Should().Contain(w => w.Contains("stale"));

        var rebuilt = ToolArgumentBuilder.Build(fields, values.ToDictionary(kv => kv.Key, kv => (string?)kv.Value));
        rebuilt.IsValid.Should().BeTrue();
        rebuilt.Arguments["count"]!.GetValue<long>().Should().Be(7);
    }

    [Fact]
    public void Replay_ExactSchemaMatch_IsNotIncomplete()
    {
        var fields = ToolFormModel.FromSchema(Schema(SampleSchema));
        var replay = ToolArgumentBuilder.Replay(fields,
            JsonNode.Parse("""{"slnPath":"s","count":3,"mode":"a"}""")!.AsObject());
        replay.Incomplete.Should().BeFalse();
        replay.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]                              // nothing recorded
    [InlineData("""{"count":3}""")]                 // required slnPath missing
    [InlineData("""{"count":3,"extra":1}""")]       // key not in schema
    public void Replay_GapsAreFlaggedIncomplete(string recorded)
    {
        var fields = ToolFormModel.FromSchema(Schema(SampleSchema));
        ToolArgumentBuilder.Replay(fields, JsonNode.Parse(recorded)!.AsObject()).Incomplete.Should().BeTrue();
    }

    [Fact]
    public void Replay_NoFieldsAndNoArguments_IsComplete() =>
        ToolArgumentBuilder.Replay([], new JsonObject()).Incomplete.Should().BeFalse();

    [Fact]
    public void Replay_RequiredMissingOnly_IsIncompleteButNotAMismatch()
    {
        var fields = ToolFormModel.FromSchema(Schema(SampleSchema));
        var replay = ToolArgumentBuilder.Replay(fields, JsonNode.Parse("""{"count":3}""")!.AsObject());
        replay.Incomplete.Should().BeTrue();
        replay.HasMismatch.Should().BeFalse(); // a favourite without slnPath is fine, it is just not filled in yet
    }

    private static IReadOnlyList<ToolField> EnumFields() => ToolFormModel.FromSchema(Schema("""
        { "type":"object", "properties": { "mode": {"type":"string","enum":["Alpha","Beta"]} }, "required":["mode"] }
        """));

    [Fact]
    public void Replay_Choice_MatchesCaseInsensitively()
    {
        var replay = ToolArgumentBuilder.Replay(EnumFields(), JsonNode.Parse("""{"mode":"beta"}""")!.AsObject());
        replay.Values["mode"].Should().Be("Beta");
        replay.Incomplete.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"mode":1}""")]       // a number is never guessed to be an enum index
    [InlineData("""{"mode":"Gamma"}""")]
    public void Replay_Choice_NoMatch_LeavesUnselectedAndWarns(string recorded)
    {
        var replay = ToolArgumentBuilder.Replay(EnumFields(), JsonNode.Parse(recorded)!.AsObject());
        replay.Values.Should().NotContainKey("mode");
        replay.Incomplete.Should().BeTrue();
        replay.HasMismatch.Should().BeTrue();
        replay.Warnings.Should().Contain(w => w.Contains("mode") && w.Contains("unselected"));
    }

    [Fact]
    public void Choice_RequiredWithoutDefault_StartsEmpty_SoTheRequiredCheckFires()
    {
        var fields = EnumFields();
        ToolFormModel.InitialValues(fields)["mode"].Should().BeEmpty();
        var r = ToolArgumentBuilder.Build(fields, new Dictionary<string, string?> { ["mode"] = "" });
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainSingle(e => e.StartsWith("mode: required"));
    }

    [Fact]
    public void Text_QuotedEmptyToken_SendsEmptyString_AndPlainEmptyIsOmitted()
    {
        var fields = ToolFormModel.FromSchema(Schema(
            """{"type":"object","properties":{"initialValue":{"type":"string"}}}"""));
        var sent = ToolArgumentBuilder.Build(fields, new Dictionary<string, string?> { ["initialValue"] = "\"\"" });
        sent.Arguments["initialValue"]!.GetValue<string>().Should().BeEmpty();
        sent.Arguments.ContainsKey("initialValue").Should().BeTrue();

        ToolArgumentBuilder.Build(fields, new Dictionary<string, string?> { ["initialValue"] = "" })
            .Arguments.ContainsKey("initialValue").Should().BeFalse();
    }

    [Fact]
    public void Replay_RecordedEmptyString_ReproducesTheQuotedToken()
    {
        var fields = ToolFormModel.FromSchema(Schema(
            """{"type":"object","properties":{"initialValue":{"type":"string"}}}"""));
        var replay = ToolArgumentBuilder.Replay(fields, JsonNode.Parse("""{"initialValue":""}""")!.AsObject());
        replay.Values["initialValue"].Should().Be("\"\"");
        var rebuilt = ToolArgumentBuilder.Build(fields, replay.Values.ToDictionary(kv => kv.Key, kv => (string?)kv.Value));
        rebuilt.Arguments["initialValue"]!.GetValue<string>().Should().BeEmpty();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("{}", true)]
    [InlineData("""{"a":1}""", true)]
    [InlineData("[1]", false)]
    [InlineData("""{"a":""", false)]
    public void TryParseArguments_OnlyObjects(string json, bool ok) =>
        (ToolArgumentBuilder.TryParseArguments(json) is not null).Should().Be(ok);

    // ── grouping ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("twincat_plc_create", "PLC")]
    [InlineData("twincat_build_plc", "PLC")]
    [InlineData("twincat_list_pous", "PLC")]
    [InlineData("twincat_hmi_build", "HMI")]
    [InlineData("twincat_create_scope_project", "Scope")]
    [InlineData("twincat_get_scope_status", "Scope")]
    [InlineData("ptc_test_marker", "Diagnostics")]
    [InlineData("twincat_read_ads_variable", "ADS")]
    [InlineData("twincat_ibn_arm", "Approvals")]
    [InlineData("twincat_get_status", "General")]
    public void GroupOf_DerivesAreaFromName(string tool, string group) =>
        ToolGrouping.GroupOf(tool).Should().Be(group);

    private static ToolSummary Tool(string name, string description = "") =>
        new(name, description, Schema("{}"), true, false);

    [Fact]
    public void Build_GroupsSortedWithGeneralLast_AndFilters()
    {
        var tools = new[]
        {
            Tool("twincat_get_status", "Status of XAE"),
            Tool("twincat_plc_list"),
            Tool("twincat_hmi_build", "Build the HMI"),
            Tool("twincat_plc_create"),
            Tool("ptc_test_marker"),
        };

        var all = ToolGrouping.Build(tools, null);
        all.Select(g => g.Title).Should().Equal("Diagnostics", "HMI", "PLC", "General");
        all.Single(g => g.Title == "PLC").Tools.Should().Equal("twincat_plc_create", "twincat_plc_list");

        var filtered = ToolGrouping.Build(tools, "build");
        filtered.Should().ContainSingle().Which.Tools.Should().Equal("twincat_hmi_build");

        ToolGrouping.Build(tools, "STATUS").Single().Tools.Should().Equal("twincat_get_status");
        ToolGrouping.Build(tools, "nothing-matches").Should().BeEmpty();
    }

    // ── presets ──────────────────────────────────────────────────────────

    private PresetStore Store() => new(Path.Combine(_dir, "presets.json"));

    [Fact]
    public void Load_FirstRun_ReturnsAndPersistsGettingStarted()
    {
        var store = Store();
        var presets = store.Load();
        presets.Select(p => p.Tool).Should().Equal(
            "twincat_get_status", "twincat_open_project", "twincat_plc_list",
            "twincat_build_plc", "twincat_get_error_list");
        presets.Select(p => p.Tool).Should().NotContain("twincat_list_pous"); // deprecated
        File.Exists(store.Path).Should().BeTrue();
        // slnPath stays empty for the user to fill in (an explicit "" would show up as the empty-string token).
        JsonNode.Parse(presets.Single(p => p.Tool == "twincat_open_project").Arguments)!.AsObject()
            .ContainsKey("slnPath").Should().BeFalse();
    }

    [Fact]
    public void Load_LockedFile_IsNotCorruption_FileUntouched_SaveRefused()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "presets.json");
        File.WriteAllText(path, """{"presets":[{"name":"keep","tool":"t","arguments":{}}]}""");
        var store = new PresetStore(path);

        (IReadOnlyList<ToolPreset> Presets, string? Warning) loaded;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            loaded = store.LoadWithWarning();

        loaded.Presets.Should().HaveCount(PresetStore.GettingStarted().Count);
        loaded.Warning.Should().NotBeNull().And.Contain("could not be read");
        Directory.GetFiles(_dir, "presets.json.corrupt-*").Should().BeEmpty();
        File.ReadAllText(path).Should().Contain("keep"); // not renamed, not overwritten

        // Saving is refused while the file was unreadable; a successful reload re-enables it.
        store.Upsert(loaded.Presets, new ToolPreset("n", "t", "{}"), out bool saved);
        saved.Should().BeFalse();
        File.ReadAllText(path).Should().Contain("keep");
        store.Load().Should().ContainSingle(p => p.Name == "keep");
        store.Upsert(store.Load(), new ToolPreset("n", "t", "{}"), out saved);
        saved.Should().BeTrue();
    }

    [Fact]
    public void Upsert_And_Remove_ReportAWriteFailure()
    {
        Directory.CreateDirectory(_dir);
        string blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");                       // a file where the directory should be
        var store = new PresetStore(Path.Combine(blocker, "presets.json"));

        store.Upsert([], new ToolPreset("a", "t", "{}"), out bool saved).Should().ContainSingle();
        saved.Should().BeFalse();
        store.Remove([new ToolPreset("a", "t", "{}")], "a", out saved).Should().BeEmpty();
        saved.Should().BeFalse();
    }

    [Fact]
    public void ConsoleSession_IsNotCountedAsAClient()
    {
        var reg = new McpSessionRegistry();
        reg.Touch(new McpClientSession("c1", ToolConsoleClient.ClientName, "1", "2025-06-18", DateTimeOffset.UnixEpoch));
        reg.Count.Should().Be(0);
        reg.Touch(new McpClientSession("s1", "claude-code", "1", "2025-06-18", DateTimeOffset.UnixEpoch));
        reg.Count.Should().Be(1);
        ToolConsoleClient.ClientName.Should().Be("ptc-xae-mcp console");
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var store = Store();
        var list = store.Upsert(store.Load(), new ToolPreset("mine", "twincat_plc_list", """{"a":[1,2],"b":"x"}"""), out _);
        var reloaded = Store().Load();
        reloaded.Should().HaveCount(list.Count);
        var mine = reloaded.Single(p => p.Name == "mine");
        mine.Tool.Should().Be("twincat_plc_list");
        JsonNode.Parse(mine.Arguments)!["a"]!.AsArray().Count.Should().Be(2);
    }

    [Fact]
    public void Upsert_SameNameReplaces_RemoveDeletes()
    {
        var store = Store();
        var l1 = store.Upsert([], new ToolPreset("x", "t1", "{}"), out _);
        var l2 = store.Upsert(l1, new ToolPreset("X", "t2", "{}"), out _);
        l2.Should().ContainSingle().Which.Tool.Should().Be("t2");
        store.Remove(l2, "x", out _).Should().BeEmpty();
        Store().Load().Should().BeEmpty(); // a saved empty list is respected, not reset to defaults
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("""{"presets":[{"name":"n"}]}""")]
    [InlineData("""{"presets":[{"name":5,"tool":"t"}]}""")]
    public void Load_CorruptFile_BacksUpAndReturnsDefaults(string content)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "presets.json");
        File.WriteAllText(path, content);

        var presets = new PresetStore(path).Load();

        presets.Should().HaveCount(PresetStore.GettingStarted().Count);
        var backup = Directory.GetFiles(_dir, "presets.json.corrupt-*").Should().ContainSingle().Subject;
        File.ReadAllText(backup).Should().Be(content);
        new PresetStore(path).Load().Should().HaveCount(presets.Count); // the file is valid again
    }
}
