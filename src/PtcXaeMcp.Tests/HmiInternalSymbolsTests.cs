using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// hmi-internal-symbols (#165). Invariants: nothing reaches
/// the Automation Interface unless input validated AND the engineer confirmed; an existing name is
/// never added twice; success is decided ONLY by reading back the persisted state —
/// Properties\tchmiconfig.json for client symbols, the server config for TcHmiSrv symbols —
/// never by the interface's return value (RemoveInternalSymbol is always false).
/// </summary>
public class HmiInternalSymbolsTests : IDisposable
{
    private const string Project = "HMI_Fixture";

    private readonly string _root;
    private readonly string _projectDir;

    /// <summary>The shape of a real project's tchmiconfig.json (template + one user symbol).</summary>
    private const string ClientConfig =
        """
        {
          "$schema": "./tchmi.project.Schema.json",
          "startupView": "Desktop.view",
          "symbols": {
            "internal": {
              "BackgroundImage": { "value": 0, "type": "tchmi:general#/definitions/INT16", "persist": true, "readonly": false },
              "NavigationEnable": { "value": true, "type": "tchmi:general#/definitions/Boolean", "persist": false, "readonly": false }
            }
          }
        }
        """;

    /// <summary>A server config with one of the server's own hidden function symbols, an ADS
    /// mapping and one user offline symbol in the allOf form.</summary>
    private const string ServerConfig =
        """
        {
          "CACHEENABLED": true,
          "SYMBOLS": {
            "ListSymbols": { "ACCESS": 3, "DOMAIN": "TcHmiSrv", "HIDDEN": true, "MAPPING": "ListSymbols", "OPTIONS": {}, "SCHEMA": { "function": true }, "USEMAPPING": true },
            "ADS.PLC1.MAIN.bStart": { "ACCESS": 3, "DOMAIN": "ADS", "MAPPING": "PLC1::MAIN::bStart", "OPTIONS": {}, "SCHEMA": { "$ref": "tchmi:general#/definitions/BOOL" }, "USEMAPPING": true },
            "Recipe.Selected": { "ACCESS": 3, "DOMAIN": "TcHmiSrv", "MAPPING": "", "OPTIONS": { "persistedOfflineValue": 2 },
                                 "SCHEMA": { "allOf": [ { "$ref": "tchmi:general#/definitions/INT16" }, { "default": 2 } ] }, "USEMAPPING": false }
          }
        }
        """;

    public HmiInternalSymbolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ptc-isym-{Guid.NewGuid():N}");
        _projectDir = Path.Combine(_root, "sln", Project);
        Directory.CreateDirectory(Path.Combine(_projectDir, "Properties"));
        Directory.CreateDirectory(Path.Combine(_projectDir, "Server", "TcHmiSrv"));
        File.WriteAllText(Path.Combine(_projectDir, HmiInternalSymbols.ClientConfigRelativePath), ClientConfig);
        File.WriteAllText(Path.Combine(_projectDir, HmiInternalSymbols.ServerConfigRelativePath), ServerConfig);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private FakeTwinCatSession Session() => new()
    {
        IsConnected = true,
        SolutionOpen = true,
        ActiveSlnPath = Path.Combine(_root, "sln", "Fixture.sln"),
        HmiProjects = new HmiProjectList("Beckhoff.TcHmi.1.12", 1,
            [new HmiProjectEntry(Project, _projectDir, true, false)], null),
    };

    private static HmiCoordinator Coordinator(FakeTwinCatSession fake, IHmiWriteConfirmer? confirmer) =>
        new(fake, confirmer) { ReadBackDelays = [TimeSpan.Zero] };

    private static bool Wrote(FakeTwinCatSession fake) =>
        fake.CallLog.Any(c => c.StartsWith("AddHmiInternalSymbol", StringComparison.Ordinal)
                           || c.StartsWith("RemoveHmiInternalSymbol", StringComparison.Ordinal)
                           || c.StartsWith("MapHmiOfflineSymbol", StringComparison.Ordinal));

    private static (string Summary, string Json) Add(FakeTwinCatSession fake, IHmiWriteConfirmer? confirmer,
        string? name = "SelectedPage", string? dataType = "tchmi:general#/definitions/INT16",
        string? defaultValueJson = "1", bool persist = false, bool readOnly = false, int timeout = 120) =>
        Coordinator(fake, confirmer).AddInternalSymbol(null, name, dataType, defaultValueJson, persist, readOnly, timeout);

    private static (string Summary, string Json) Remove(FakeTwinCatSession fake, IHmiWriteConfirmer? confirmer,
        string? name, int timeout = 120) =>
        Coordinator(fake, confirmer).RemoveInternalSymbol(null, name, timeout);

    private static (string Summary, string Json) MapOffline(FakeTwinCatSession fake, IHmiWriteConfirmer? confirmer,
        string? mapName = "Recipe.Mode", string? dataType = "tchmi:general#/definitions/BOOL",
        string? defaultValueJson = "false", bool online = false, bool persist = false, int timeout = 120) =>
        Coordinator(fake, confirmer).MapOfflineSymbol(null, mapName, dataType, defaultValueJson, online, persist, timeout);

    // ── pure helpers: validation ───────────────────────────────────────────────

    [Theory]
    [InlineData("SelectedPage", true)]
    [InlineData("_flag1", true)]
    [InlineData("1abc", false)]
    [InlineData("a.b", false)]
    [InlineData("a b", false)]
    [InlineData("a%b", false)]
    [InlineData("a::b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidClientName(string? name, bool expected) =>
        Assert.Equal(expected, HmiInternalSymbols.IsValidClientName(name));

    [Fact]
    public void IsValidClientName_TooLong_IsRefused() =>
        Assert.False(HmiInternalSymbols.IsValidClientName(new string('a', HmiInternalSymbols.MaxNameLength + 1)));

    [Theory]
    [InlineData("Recipe.Selected", true)]
    [InlineData("Mode", true)]
    [InlineData("Recipe..Selected", false)]
    [InlineData("Recipe.", false)]
    [InlineData("PLC1::MAIN::x", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void IsValidServerName(string name, bool expected) =>
        Assert.Equal(expected, HmiInternalSymbols.IsValidServerName(name));

    [Theory]
    [InlineData("tchmi:general#/definitions/Boolean", true)]
    [InlineData("tchmi:general#/definitions/INT16", true)]
    [InlineData("tchmi:framework#/definitions/TcHmi.Functions.BaseTemplate.MediaQueryList", true)]
    [InlineData("boolean", true)]
    [InlineData("integer", true)]
    [InlineData("Boolean", false)]
    [InlineData("BOOL", false)]
    [InlineData("object", false)]
    [InlineData("tchmi:general#/definitions/", false)]
    [InlineData("tchmi:general", false)]
    [InlineData("", false)]
    public void IsValidDataType(string type, bool expected) =>
        Assert.Equal(expected, HmiInternalSymbols.IsValidDataType(type));

    [Theory]
    [InlineData(null, "boolean", null)]
    [InlineData("  ", "boolean", null)]
    [InlineData("true", "tchmi:general#/definitions/Boolean", "true")]
    [InlineData("42", "tchmi:general#/definitions/INT16", "42")]
    [InlineData("3.5", "tchmi:general#/definitions/LREAL", "3.5")]
    [InlineData("3", "number", "3")]
    [InlineData("\"x\"", "tchmi:general#/definitions/String", "\"x\"")]
    [InlineData("null", "tchmi:general#/definitions/INT16", "null")]
    [InlineData("\"anything\"", "tchmi:framework#/definitions/Some.Unknown.Type", "\"anything\"")]
    public void TryNormalizeDefault_Accepts(string? json, string type, string? expected)
    {
        Assert.True(HmiInternalSymbols.TryNormalizeDefault(json, type, out string? normalized, out string? error));
        Assert.Null(error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("{", "boolean", "not valid JSON")]
    [InlineData("{\"a\":1}", "boolean", "JSON scalar")]
    [InlineData("[1,2]", "tchmi:general#/definitions/Some", "JSON scalar")]
    [InlineData("42", "tchmi:general#/definitions/Boolean", "does not fit")]
    [InlineData("3.5", "tchmi:general#/definitions/INT16", "does not fit")]
    [InlineData("true", "string", "does not fit")]
    [InlineData("\"1\"", "integer", "does not fit")]
    public void TryNormalizeDefault_Refuses(string json, string type, string expectedError)
    {
        Assert.False(HmiInternalSymbols.TryNormalizeDefault(json, type, out _, out string? error));
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void ToComValue_MapsScalarsToVariantFriendlyTypes()
    {
        Assert.Null(HmiInternalSymbols.ToComValue(null));
        Assert.Null(HmiInternalSymbols.ToComValue("null"));
        Assert.Equal(true, HmiInternalSymbols.ToComValue("true"));
        Assert.Equal(42, HmiInternalSymbols.ToComValue("42"));
        Assert.Equal(30_000_000_000L, HmiInternalSymbols.ToComValue("30000000000"));
        Assert.Equal(3.5, HmiInternalSymbols.ToComValue("3.5"));
        Assert.Equal("x", HmiInternalSymbols.ToComValue("\"x\""));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "null", true)]
    [InlineData("1", "1", true)]
    [InlineData("\"a\"", "\"a\"", true)]
    [InlineData("1", null, false)]
    [InlineData("true", "false", false)]
    public void SameJson(string? a, string? b, bool expected) =>
        Assert.Equal(expected, HmiInternalSymbols.SameJson(a, b));

    // ── pure helpers: parsing the persisted files ──────────────────────────────

    [Fact]
    public void ParseClientSymbols_RealShape()
    {
        var symbols = HmiInternalSymbols.ParseClientSymbols(ClientConfig);

        Assert.NotNull(symbols);
        Assert.Equal(["BackgroundImage", "NavigationEnable"], symbols.Select(s => s.Name));
        HmiClientInternalSymbol bg = symbols[0];
        Assert.Equal("tchmi:general#/definitions/INT16", bg.Type);
        Assert.Equal("0", bg.ValueJson);
        Assert.True(bg.Persist);
        Assert.False(bg.ReadOnly);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"symbols\":{}}")]
    [InlineData("{\"symbols\":null}")]
    public void ParseClientSymbols_NoTable_IsEmpty(string json) =>
        Assert.Empty(HmiInternalSymbols.ParseClientSymbols(json)!);

    [Theory]
    [InlineData("")]
    [InlineData("{ \"symbols\": ")]
    [InlineData("[]")]
    [InlineData("{\"symbols\":[]}")]
    [InlineData("{\"symbols\":{\"internal\":[]}}")]
    [InlineData("{\"symbols\":{\"internal\":{\"a\":1}}}")]
    public void ParseClientSymbols_WrongShape_IsNull(string json) =>
        Assert.Null(HmiInternalSymbols.ParseClientSymbols(json));

    [Fact]
    public void ParseClientSymbols_ToleratesCommentsAndTrailingCommas() =>
        Assert.Single(HmiInternalSymbols.ParseClientSymbols(
            "{ // c\n \"symbols\": { \"internal\": { \"a\": { \"type\": \"boolean\", }, }, }, }")!);

    [Fact]
    public void ParseServerConfig_RealShape()
    {
        var all = HmiInternalSymbols.ParseServerConfig(ServerConfig);

        Assert.NotNull(all);
        Assert.Equal(3, all.Count);
        var user = all.Where(HmiInternalSymbols.IsUserServerSymbol).ToList();
        HmiServerSymbolEntry recipe = Assert.Single(user);
        Assert.Equal("Recipe.Selected", recipe.Name);
        Assert.False(recipe.UseMapping);
        Assert.Equal("", recipe.Mapping);
        Assert.Equal("tchmi:general#/definitions/INT16", recipe.DataType);
        Assert.Equal("2", recipe.DefaultJson);
        Assert.True(recipe.Persist);
        Assert.Equal("2", recipe.PersistedValueJson);
        Assert.Equal(3, recipe.Access);
    }

    [Fact]
    public void ParseServerConfig_EmptyTemplateFile_HasNoSymbols()
    {
        Assert.Empty(HmiInternalSymbols.ParseServerConfig("")!);
        Assert.Empty(HmiInternalSymbols.ParseServerConfig("{\"CACHEENABLED\":true}")!);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"SYMBOLS\":[]}")]
    [InlineData("{\"SYMBOLS\":{\"a\":1}}")]
    public void ParseServerConfig_WrongShape_IsNull(string json) =>
        Assert.Null(HmiInternalSymbols.ParseServerConfig(json));

    [Fact]
    public void ParseServerSymbolEntry_PrimitiveSchema()
    {
        var entry = HmiInternalSymbols.ParseServerSymbolEntry("Mode",
            "{\"DOMAIN\":\"TcHmiSrv\",\"MAPPING\":\"\",\"OPTIONS\":{},\"SCHEMA\":{\"type\":\"boolean\",\"default\":true},\"USEMAPPING\":false}");

        Assert.NotNull(entry);
        Assert.Equal("boolean", entry.DataType);
        Assert.Equal("true", entry.DefaultJson);
        Assert.False(entry.Persist);
        Assert.Null(HmiInternalSymbols.ParseServerSymbolEntry("x", "\"text\""));
        Assert.Null(HmiInternalSymbols.ParseServerSymbolEntry("x", null));
    }

    // ── list ───────────────────────────────────────────────────────────────────

    [Fact]
    public void List_ReportsClientAndUserServerSymbols()
    {
        var fake = Session();
        var (summary, json) = Coordinator(fake, null).ListInternalSymbols(null);

        Assert.Contains("2 client internal symbol(s)", summary);
        Assert.Contains("1 user server symbol(s)", summary);
        Assert.DoesNotContain("ListSymbols", json); // the server's own hidden functions are left out
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("client").GetProperty("count").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("server").GetProperty("offline").GetInt32());
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("ReadHmiSymbols", StringComparison.Ordinal));
    }

    [Fact]
    public void List_ServerFileMissing_IsUnknownNotNone()
    {
        File.Delete(Path.Combine(_projectDir, HmiInternalSymbols.ServerConfigRelativePath));
        var (summary, json) = Coordinator(Session(), null).ListInternalSymbols(null);

        Assert.False(McpErrors.IsError(summary));
        Assert.Contains("UNKNOWN", summary);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("server").GetProperty("readable").GetBoolean());
    }

    [Fact]
    public void List_ClientFileMissing_IsAnError()
    {
        File.Delete(Path.Combine(_projectDir, HmiInternalSymbols.ClientConfigRelativePath));
        var (summary, _) = Coordinator(Session(), null).ListInternalSymbols(null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("tchmiconfig.json", summary);
    }

    [Fact]
    public void List_NoSolution_IsRefused()
    {
        var fake = Session();
        fake.SolutionOpen = false;
        var (summary, _) = Coordinator(fake, null).ListInternalSymbols(null);
        Assert.StartsWith("[Error]", summary);
    }

    // ── add (client) ───────────────────────────────────────────────────────────

    [Fact]
    public void Add_Confirmed_IsVerifiedFromTheFile()
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, json) = Add(fake, confirmer, persist: true);

        Assert.False(McpErrors.IsError(summary), summary);
        Assert.Contains("%i%SelectedPage%/i%", summary);
        Assert.Contains("verified in", summary);
        Assert.DoesNotContain("WARNING", summary);
        Assert.Contains("AddHmiInternalSymbol(HMI_Fixture, SelectedPage, tchmi:general#/definitions/INT16, 1, persist=True, readOnly=False)", fake.CallLog);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("confirmedByEngineer", doc.RootElement.GetProperty("confirmation").GetString());

        var (request, _) = Assert.Single(confirmer.Requests);
        Assert.Equal(HmiWriteKind.AddInternalSymbol, request.Kind);
        Assert.Equal("SelectedPage", request.Symbol);
        Assert.Contains("persist", request.NewValue);
        Assert.False(request.WritesToPlc);
        Assert.Contains("client internal symbol", HmiConfirmText.Headline(request));
        Assert.Contains("%i% symbol  : SelectedPage", HmiConfirmText.Details(request));
    }

    [Theory]
    [InlineData("bad name", "tchmi:general#/definitions/INT16", "1", "not a valid internal symbol name")]
    [InlineData("Ok", "BOOL", "true", "dataType must be")]
    [InlineData("Ok", "tchmi:general#/definitions/Boolean", "1", "does not fit")]
    [InlineData("Ok", "boolean", "{\"a\":1}", "JSON scalar")]
    public void Add_BadInput_NeverReachesTheConfirmer(string name, string type, string value, string expected)
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = Add(fake, confirmer, name, type, value);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains(expected, summary);
        Assert.Empty(confirmer.Requests);
        Assert.False(Wrote(fake));
    }

    [Theory]
    [InlineData("NavigationEnable")]
    [InlineData("navigationenable")]
    public void Add_ExistingName_IsRefusedBeforeThePopup(string name)
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = Add(fake, confirmer, name, "tchmi:general#/definitions/Boolean", "true");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("already has the internal symbol 'NavigationEnable'", summary);
        Assert.Empty(confirmer.Requests);
        Assert.False(Wrote(fake));
    }

    [Theory]
    [InlineData("Rejected", "rejected")]
    [InlineData("TimedOut", "Not confirmed within")]
    [InlineData("Unavailable", "No confirmation channel")]
    public void Add_NotConfirmed_WritesNothing(string outcome, string expected)
    {
        var fake = Session();
        var (summary, _) = Add(fake, new FakeHmiConfirmer(Enum.Parse<HmiConfirmOutcome>(outcome)));
        Assert.Contains(expected, summary);
        Assert.False(Wrote(fake));
    }

    [Fact]
    public void Add_NoConfirmer_WritesNothing()
    {
        var fake = Session();
        var (summary, _) = Add(fake, null);
        Assert.Contains("No confirmation channel", summary);
        Assert.False(Wrote(fake));
    }

    [Fact]
    public void Add_TimeoutOutOfRange_IsRefused()
    {
        var fake = Session();
        var (summary, _) = Add(fake, new FakeHmiConfirmer(), timeout: 5);
        Assert.Contains("confirmTimeoutSeconds", summary);
        Assert.False(Wrote(fake));
    }

    [Fact]
    public void Add_NotOnReadBack_IsNotSuccess()
    {
        var fake = Session();
        fake.HmiInternalAddDoesNothing = true;
        var (summary, _) = Add(fake, new FakeHmiConfirmer());

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("not in", summary);
        Assert.Contains("not treated as success", summary);
    }

    [Fact]
    public void Add_UnreadableAfterwards_IsUnknownNotSuccess()
    {
        var fake = Session();
        fake.HmiInternalCorruptsConfig = true;
        var (summary, _) = Add(fake, new FakeHmiConfirmer());

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("whether it took effect is unknown", summary);
    }

    [Fact]
    public void Add_StoredDifferently_IsReportedAsWarning()
    {
        var fake = Session();
        fake.HmiInternalStoresType = "tchmi:general#/definitions/INT32";
        var (summary, json) = Add(fake, new FakeHmiConfirmer());

        Assert.False(McpErrors.IsError(summary));
        Assert.Contains("WARNING", summary);
        Assert.Contains("INT32", json);
    }

    [Fact]
    public void Add_ComFailure_IsAnError()
    {
        var fake = Session();
        fake.AddHmiInternalSymbolThrows = new InvalidOperationException("boom");
        var (summary, _) = Add(fake, new FakeHmiConfirmer());
        Assert.StartsWith("[Error] AddInternalSymbol failed: boom", summary);
    }

    [Fact]
    public void Add_ClientFileMissing_WritesNothing()
    {
        File.Delete(Path.Combine(_projectDir, HmiInternalSymbols.ClientConfigRelativePath));
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = Add(fake, confirmer);

        Assert.StartsWith("[Error]", summary);
        Assert.Empty(confirmer.Requests);
        Assert.False(Wrote(fake));
    }

    // ── remove (client) ────────────────────────────────────────────────────────

    [Fact]
    public void Remove_Confirmed_IsVerifiedByAbsence()
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, json) = Remove(fake, confirmer, "BackgroundImage");

        Assert.False(McpErrors.IsError(summary), summary);
        Assert.Contains("removed", summary);
        Assert.Contains("(1 left)", summary);
        Assert.Contains("RemoveHmiInternalSymbol(HMI_Fixture, BackgroundImage)", fake.CallLog);
        Assert.Equal(HmiWriteKind.RemoveInternalSymbol, Assert.Single(confirmer.Requests).Request.Kind);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("removed").GetBoolean());
        Assert.DoesNotContain("BackgroundImage",
            File.ReadAllText(Path.Combine(_projectDir, HmiInternalSymbols.ClientConfigRelativePath)));
    }

    [Fact]
    public void Remove_Missing_IsNothingToDoWithoutPopup()
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = Remove(fake, confirmer, "backgroundimage");

        Assert.StartsWith("Nothing to do", summary);
        Assert.Contains("did you mean 'BackgroundImage'", summary);
        Assert.Empty(confirmer.Requests);
        Assert.False(Wrote(fake));
    }

    [Fact]
    public void Remove_StillThereOnReadBack_IsNotSuccess()
    {
        var fake = Session();
        fake.HmiInternalRemoveDoesNothing = true;
        var (summary, _) = Remove(fake, new FakeHmiConfirmer(), "BackgroundImage");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("still in", summary);
    }

    [Fact]
    public void Remove_Rejected_WritesNothing()
    {
        var fake = Session();
        var (summary, _) = Remove(fake, new FakeHmiConfirmer(HmiConfirmOutcome.Rejected), "BackgroundImage");
        Assert.Contains("rejected", summary);
        Assert.False(Wrote(fake));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Remove_NoName_IsRefused(string? name)
    {
        var fake = Session();
        var (summary, _) = Remove(fake, new FakeHmiConfirmer(), name);
        Assert.StartsWith("[Error] name is required", summary);
        Assert.False(Wrote(fake));
    }

    // ── map offline (server) ───────────────────────────────────────────────────

    [Fact]
    public void MapOffline_Confirmed_IsVerifiedFromTheLiveConfig()
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, json) = MapOffline(fake, confirmer, persist: true);

        Assert.False(McpErrors.IsError(summary), summary);
        Assert.Contains("Server symbol 'Recipe.Mode' created", summary);
        Assert.Contains("offline", summary);
        Assert.Contains("live server config", summary);
        Assert.DoesNotContain("WARNING", summary);
        Assert.Contains("MapHmiOfflineSymbol(HMI_Fixture, Recipe.Mode, tchmi:general#/definitions/BOOL, false, online=False, persist=True)", fake.CallLog);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("TcHmiSrv", doc.RootElement.GetProperty("domain").GetString());

        var (request, _) = Assert.Single(confirmer.Requests);
        Assert.Equal(HmiWriteKind.MapOfflineSymbol, request.Kind);
        Assert.Equal("TcHmiSrv", request.Domain);
        Assert.Contains("offline", request.NewValue);
        Assert.Contains("server symbol", HmiConfirmText.Headline(request));
    }

    [Fact]
    public void MapOffline_LiveConfigUnreadable_FallsBackToTheFile()
    {
        var fake = Session();
        fake.HmiOfflineMapNoLiveConfig = true;
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer(), dataType: "integer", defaultValueJson: "5");

        Assert.False(McpErrors.IsError(summary), summary);
        Assert.Contains("TcHmiSrv.Config.default.json", summary);
    }

    [Fact]
    public void MapOffline_NowhereOnReadBack_IsNotSuccess()
    {
        var fake = Session();
        fake.HmiOfflineMapNoLiveConfig = true;
        fake.HmiOfflineMapNoFile = true;
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer());

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("not treated as success", summary);
    }

    [Fact]
    public void MapOffline_ServerSaysTrueButKeepsNothing_IsNotSuccess()
    {
        var fake = Session();
        fake.HmiOfflineMapDoesNothing = true;
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer());
        Assert.StartsWith("[Error] MapOfflineSymbol returned true", summary);
    }

    [Fact]
    public void MapOffline_UseMappingDiffers_IsNotSuccess()
    {
        var fake = Session();
        fake.HmiOfflineMapStoresUseMapping = true;
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer());
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("USEMAPPING=True", summary);
    }

    [Fact]
    public void MapOffline_WrongDomain_IsNotSuccess()
    {
        var fake = Session();
        fake.HmiOfflineMapStoresDomain = "ADS";
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer());
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("domain 'ADS'", summary);
    }

    [Fact]
    public void MapOffline_RefusedByServer_ReportsLastError()
    {
        var fake = Session();
        fake.HmiOfflineMapResult = new HmiOperationResult(false, "server request");
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer());
        Assert.StartsWith("[Error] MapOfflineSymbol('Recipe.Mode'", summary);
        Assert.Contains("server request", summary);
    }

    [Theory]
    [InlineData("Recipe.Selected")]
    [InlineData("ADS.PLC1.MAIN.bStart")]
    [InlineData("recipe.selected")]
    public void MapOffline_NameInSavedConfig_IsRefusedBeforeThePopup(string name)
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = MapOffline(fake, confirmer, mapName: name);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("already exists in the server configuration", summary);
        Assert.Empty(confirmer.Requests);
        Assert.False(Wrote(fake));
    }

    [Fact]
    public void MapOffline_LiveServerKnowsTheName_IsRefused()
    {
        var fake = Session();
        fake.HmiSymbolValues["Recipe.Mode"] = "True";
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = MapOffline(fake, confirmer);

        Assert.Contains("already exists in the running HMI server", summary);
        Assert.Empty(confirmer.Requests);
    }

    [Fact]
    public void MapOffline_LiveStateUnknown_IsRefused()
    {
        var fake = Session();
        fake.HmiSymbolErrors["Recipe.Mode"] = "server not running";
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = MapOffline(fake, confirmer);

        Assert.Contains("Cannot determine whether", summary);
        Assert.Empty(confirmer.Requests);
        Assert.False(Wrote(fake));
    }

    [Fact]
    public void MapOffline_ServerFileMissing_StillWorksThroughTheLiveProbe()
    {
        File.Delete(Path.Combine(_projectDir, HmiInternalSymbols.ServerConfigRelativePath));
        var fake = Session();
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer());
        Assert.False(McpErrors.IsError(summary), summary);
    }

    [Theory]
    [InlineData("PLC1::x", "boolean", "true", "not a valid server symbol name")]
    [InlineData("Mode", "bool", "true", "dataType must be")]
    [InlineData("Mode", "boolean", "\"yes\"", "does not fit")]
    public void MapOffline_BadInput_NeverReachesTheConfirmer(string name, string type, string value, string expected)
    {
        var fake = Session();
        var confirmer = new FakeHmiConfirmer();
        var (summary, _) = MapOffline(fake, confirmer, name, type, value);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains(expected, summary);
        Assert.Empty(confirmer.Requests);
        Assert.False(Wrote(fake));
    }

    [Fact]
    public void MapOffline_Rejected_WritesNothing()
    {
        var fake = Session();
        var (summary, _) = MapOffline(fake, new FakeHmiConfirmer(HmiConfirmOutcome.Rejected));
        Assert.Contains("rejected", summary);
        Assert.False(Wrote(fake));
    }
}
