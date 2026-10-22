using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Exercises the PLC source-code MCP surface against the live MCP server and
/// XAE against the neutral fixture (<see cref="FixtureLayout"/>); the ST POU under
/// test defaults to its MAIN and can be overridden with PTC_XAE_MCP_FIXTURE_POU.
/// Round-trip tests capture the existing implementation before mutating, so they
/// restore in-memory state on a clean run; abort mid-test still leaves XAE
/// in-memory state for manual revert. Scenario 5 creates a uniquely-named POU
/// each run (FB_PlcCodeSpecTest_{timestamp}); cleanup is manual via XAE per spec.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class PlcCodeIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
    private const string FixtureEnvVar = "PTC_XAE_MCP_FIXTURE_SLN";
    private const string PouEnvVar = "PTC_XAE_MCP_FIXTURE_POU";
    private static string DefaultParentPath => FixtureLayout.PousFolder;
    private static string DutParentPath => FixtureLayout.DutsFolder;
    private static string GvlParentPath => FixtureLayout.GvlsFolder;

    private McpClient? _client;
    private bool _ready;
    private string _fixtureSln = string.Empty;
    private string _pouPath = string.Empty;

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is null) { LiveXae.Announce(false, string.Empty); return; }
        _fixtureSln = fixture;

        var pouOverride = Environment.GetEnvironmentVariable(PouEnvVar);
        _pouPath = string.IsNullOrWhiteSpace(pouOverride) ? FixtureLayout.MainPath : pouOverride;

        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(ServerUrl),
                ConnectionTimeout = TimeSpan.FromSeconds(5)
            });
            _client = await McpClient.CreateAsync(transport,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            _ready = true;
        }
        catch { _ready = false; }
        LiveXae.Announce(_ready, _fixtureSln);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private void SkipIfNotReady() => LiveXae.GuardReady(_ready);

    private async Task OpenFixtureAsync()
    {
        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        // The just-(re)opened solution can momentarily report no resolvable PLC
        // project: a sibling test's close/reopen, or the editor not being fully
        // loaded yet, leaves twincat_list_pous transiently returning "No POUs,
        // GVLs, or DUTs found" / an editor-not-ready [Error]. This is the
        // suite-order state-bleed window of issue #66 (same family as #56/#73/
        // #76). Block every scenario until the project settles so no test runs
        // against the transient empty view. The fixture always has POUs, so an
        // empty/error listing here is definitively the transient, never truth.
        await PlcProjectReadiness.AwaitAsync(_client!);
    }

    // ── Scenario 1: list_pous reports at least one POU ────────────────────────

    [Trait("Description", "POUs auflisten → Antwort enthält mindestens einen TIPC-Pfad-Eintrag.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task ListPous_AfterOpen_ReturnsAtLeastOnePou()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        var result = await _client!.CallToolAsync("twincat_list_pous");
        var text = GetText(result);

        Assert.False(result.IsError == true, $"list_pous error: {text}");
        Assert.Contains("POU", text); // header summary always mentions POU count
        // The default fixture has MAIN; for overridden POU paths, just assert at
        // least one POU line is present in the output.
        Assert.Matches(@"\bPOU\s+TIPC\^", text);
    }

    // ── Scenario 2: read_pou returns declaration + implementation ─────────────

    [Trait("Description", "Bekannten POU lesen → Antwort enthält \"--- Declaration ---\" und \"--- Implementation ---\" mit POU-Schlüsselwort.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task ReadPou_OnKnownPou_ReturnsDeclarationAndImplementation()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        var result = await _client!.CallToolAsync("twincat_read_pou",
            new Dictionary<string, object?> { ["treePath"] = _pouPath });
        var text = GetText(result);

        Assert.False(result.IsError == true, $"read_pou error: {text}");
        Assert.Contains("--- Declaration ---", text);
        Assert.Contains("--- Implementation ---", text);

        // Declaration body should start with one of the POU header keywords.
        var declStart = text.IndexOf("--- Declaration ---", StringComparison.Ordinal);
        var declTail = text.Substring(declStart);
        bool hasKeyword = declTail.Contains("PROGRAM ", StringComparison.OrdinalIgnoreCase)
                       || declTail.Contains("FUNCTION_BLOCK", StringComparison.OrdinalIgnoreCase)
                       || declTail.Contains("FUNCTION ", StringComparison.OrdinalIgnoreCase);
        Assert.True(hasKeyword, $"Declaration didn't contain POU keyword: {declTail}");
    }

    // ── Scenario 3+4: write_pou_body round-trip on a THROWAWAY POU ────────────
    // Never mutates the fixture's real MAIN (see
    // feedback_integration_never_mutate_shared_main). Creates its own PROGRAM,
    // round-trips its body, and deletes it in finally.

    [Trait("Description", "Wegwerf-PROGRAM erstellen, Rumpf überschreiben und zurücklesen → Round-Trip exakt identisch.")]
    [SkippableFact(Timeout = 300_000)]
    public async Task WritePouBody_RoundTrip_RestoresOriginal()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string prg = $"PRG_PlcCodeSpec_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string prgPath = $"{DefaultParentPath}^{prg}";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_pou",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = DefaultParentPath, ["name"] = prg,
                    ["pouType"] = "PROGRAM",
                });
            Assert.False(create.IsError == true, $"create error: {GetText(create)}");

            // Seed a known body, then read it back as our "original".
            string seed = "n := n + 1;";
            await _client.CallToolAsync("twincat_write_pou_body",
                new Dictionary<string, object?>
                {
                    ["treePath"] = prgPath,
                    ["declaration"] = $"PROGRAM {prg}\nVAR n : INT; END_VAR",
                    ["implementation"] = seed,
                });
            string originalImpl = ExtractImplementation(GetText(
                await _client.CallToolAsync("twincat_read_pou",
                    new Dictionary<string, object?> { ["treePath"] = prgPath })));

            // Overwrite with a sentinel; read-back asserts it landed.
            const string sentinel = "n := 42; // round-trip sentinel";
            var w1 = await _client.CallToolAsync("twincat_write_pou_body",
                new Dictionary<string, object?>
                { ["treePath"] = prgPath, ["implementation"] = sentinel });
            Assert.False(w1.IsError == true, $"sentinel write error: {GetText(w1)}");
            Assert.Contains("round-trip sentinel", GetText(
                await _client.CallToolAsync("twincat_read_pou",
                    new Dictionary<string, object?> { ["treePath"] = prgPath })));

            // Restore the captured original; re-read should match.
            var w2 = await _client.CallToolAsync("twincat_write_pou_body",
                new Dictionary<string, object?>
                { ["treePath"] = prgPath, ["implementation"] = originalImpl });
            Assert.False(w2.IsError == true, $"restore write error: {GetText(w2)}");

            string restoredImpl = ExtractImplementation(GetText(
                await _client.CallToolAsync("twincat_read_pou",
                    new Dictionary<string, object?> { ["treePath"] = prgPath })));
            Assert.Equal(Normalise(originalImpl), Normalise(restoredImpl));
        }
        finally
        {
            try
            {
                await _client!.CallToolAsync("twincat_delete_plc_object",
                    new Dictionary<string, object?> { ["treePath"] = prgPath });
            }
            catch { /* best-effort teardown */ }
        }
    }

    // ── Issue #148: destructive / silent-fallback inputs are refused ─────────

    [Trait("Description", "delete_plc_object auf PLC-Projekt-Root und POUs-Ordner wird verweigert; Projekt bleibt intakt.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task DeletePlcObject_ProjectRootAndFolder_AreRefused_ProjectIntact()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        foreach (var path in new[] { FixtureLayout.PlcRoot, DefaultParentPath })
        {
            var text = GetText(await _client!.CallToolAsync("twincat_delete_plc_object",
                new Dictionary<string, object?> { ["treePath"] = path }));
            Assert.StartsWith("[Error] Refusing to delete", text);
        }

        // LIVE-8: the project root delete used to remove the whole PLC project.
        var list = GetText(await _client!.CallToolAsync("twincat_list_pous"));
        Assert.Contains("MAIN", list);
    }

    [Trait("Description", "create_pou mit implLanguage=FDB → [Error], kein stilles ST, nichts angelegt.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task CreatePou_UnknownImplLanguage_IsError_NothingCreated()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"FB_Fdb148_{Guid.NewGuid():N}"[..20];
        var text = GetText(await _client!.CallToolAsync("twincat_create_pou",
            new Dictionary<string, object?>
            {
                ["parentPath"] = DefaultParentPath, ["name"] = name,
                ["pouType"] = "FUNCTION_BLOCK", ["implLanguage"] = "FDB",
            }));
        Assert.StartsWith("[Error] Unknown implLanguage 'FDB'", text);
        Assert.DoesNotContain(name, GetText(await _client!.CallToolAsync("twincat_list_pous")));
    }

    // ── Scenario 5: create_pou under POUs container ───────────────────────────

    [Trait("Description", "Neues PROGRAM erstellen → taucht in list_pous auf und ist per read_pou lesbar.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task CreatePou_NewProgram_ShowsUpInListAndIsReadable()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        // Unique name per run; deleted in finally now that pou-handling
        // provides twincat_delete_plc_object (no more manual XAE cleanup).
        string name = $"PRG_PlcCodeSpecTest_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string expectedPath = $"{DefaultParentPath}^{name}";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_pou",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = DefaultParentPath,
                    ["name"] = name,
                    ["pouType"] = "PROGRAM",
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_pou error: {createText}");
            Assert.Contains("Created PROGRAM", createText);
            Assert.Contains(name, createText);

            var read = await _client.CallToolAsync("twincat_read_pou",
                new Dictionary<string, object?> { ["treePath"] = expectedPath });
            var readText = GetText(read);
            Assert.False(read.IsError == true, $"post-create read error: {readText}");
            Assert.Contains("PROGRAM ", readText);
            Assert.Contains(name, readText);
        }
        finally
        {
            try
            {
                await _client!.CallToolAsync("twincat_delete_plc_object",
                    new Dictionary<string, object?> { ["treePath"] = expectedPath });
            }
            catch { /* best-effort teardown */ }
        }
    }

    // ── Scenario 6: create_dut (STRUCT) round-trips its declaration ───────────
    // The live regression gate for issue #104: create_dut/create_gvl passed 798
    // unit tests against the fake session while every live CreateChild call failed
    // (and the failure was masked as a transport -32603). These scenarios create
    // the object against real XAE, confirm it lists with the right kind, round-trip
    // its declaration via read_gvl, and clean up.

    [Trait("Description", "STRUCT-DUT mit Deklaration anlegen → erscheint als DUT in list_pous, Deklaration round-trippt über read_gvl.")]
    [SkippableFact(Timeout = 300_000)]
    public async Task CreateDut_Struct_ShowsAsDutAndDeclarationRoundTrips()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"ST_PlcCodeSpec_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string path = $"{DutParentPath}^{name}";
        string decl = $"TYPE {name} :\nSTRUCT\n\tField_RoundTrip : INT;\nEND_STRUCT\nEND_TYPE";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_dut",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = DutParentPath, ["name"] = name,
                    ["dutType"] = "STRUCT", ["declaration"] = decl,
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_dut error: {createText}");
            Assert.Contains("Created DUT STRUCT", createText);

            var list = GetText(await _client.CallToolAsync("twincat_list_pous"));
            Assert.Matches($@"\bDUT\s+{System.Text.RegularExpressions.Regex.Escape(path)}", list);

            var read = GetText(await _client.CallToolAsync("twincat_read_gvl",
                new Dictionary<string, object?> { ["treePath"] = path }));
            Assert.Contains("Field_RoundTrip", read);
        }
        finally { await TryDeleteAsync(path); }
    }

    // ── Scenario 7: create_dut (ENUM) shows up as a DUT ───────────────────────

    [Trait("Description", "ENUM-DUT anlegen (ohne Deklaration) → erscheint als DUT in list_pous.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task CreateDut_Enum_ShowsAsDut()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"E_PlcCodeSpec_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string path = $"{DutParentPath}^{name}";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_dut",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = DutParentPath, ["name"] = name, ["dutType"] = "ENUM",
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_dut error: {createText}");
            Assert.Contains("Created DUT ENUM", createText);

            var list = GetText(await _client.CallToolAsync("twincat_list_pous"));
            Assert.Matches($@"\bDUT\s+{System.Text.RegularExpressions.Regex.Escape(path)}", list);
        }
        finally { await TryDeleteAsync(path); }
    }

    // ── Scenario 7b: create_dut (UNION) shows up as a DUT ─────────────────────

    [Trait("Description", "UNION-DUT mit Deklaration anlegen → erscheint als DUT, Deklaration round-trippt.")]
    [SkippableFact(Timeout = 300_000)]
    public async Task CreateDut_Union_ShowsAsDutAndRoundTrips()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"U_PlcCodeSpec_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string path = $"{DutParentPath}^{name}";
        string decl = $"TYPE {name} :\nUNION\n\tAsInt : DINT;\n\tAsReal : REAL;\nEND_UNION\nEND_TYPE";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_dut",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = DutParentPath, ["name"] = name,
                    ["dutType"] = "UNION", ["declaration"] = decl,
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_dut error: {createText}");
            Assert.Contains("Created DUT UNION", createText);

            var list = GetText(await _client.CallToolAsync("twincat_list_pous"));
            Assert.Matches($@"\bDUT\s+{System.Text.RegularExpressions.Regex.Escape(path)}", list);

            var read = GetText(await _client.CallToolAsync("twincat_read_gvl",
                new Dictionary<string, object?> { ["treePath"] = path }));
            Assert.Contains("AsReal", read);
        }
        finally { await TryDeleteAsync(path); }
    }

    // ── Scenario 7c: create_dut (ALIAS) — the issue #106 live gate ─────────────
    // ALIAS uniquely needs the base type passed to CreateChild as vInfo; a null
    // vInfo throws "Base class not specified". The coordinator parses the base type
    // out of the declaration. This scenario is the regression that #105's suite
    // (STRUCT + ENUM only) lacked.

    [Trait("Description", "ALIAS-DUT mit Basistyp anlegen → erscheint als DUT, Deklaration round-trippt (issue #106).")]
    [SkippableFact(Timeout = 300_000)]
    public async Task CreateDut_Alias_ShowsAsDutAndRoundTrips()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"A_PlcCodeSpec_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string path = $"{DutParentPath}^{name}";
        string decl = $"TYPE {name} : UDINT; END_TYPE";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_dut",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = DutParentPath, ["name"] = name,
                    ["dutType"] = "ALIAS", ["declaration"] = decl,
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_dut error: {createText}");
            Assert.Contains("Created DUT ALIAS", createText);

            var list = GetText(await _client.CallToolAsync("twincat_list_pous"));
            Assert.Matches($@"\bDUT\s+{System.Text.RegularExpressions.Regex.Escape(path)}", list);

            var read = GetText(await _client.CallToolAsync("twincat_read_gvl",
                new Dictionary<string, object?> { ["treePath"] = path }));
            Assert.Contains("UDINT", read);
        }
        finally { await TryDeleteAsync(path); }
    }

    // ── Scenario 7d: create_dut (ALIAS) without declaration → clean [Error] ────

    [Trait("Description", "ALIAS-DUT ohne Deklaration → sauberer [Error], kein halb angelegtes Objekt (issue #106).")]
    [SkippableFact(Timeout = 240_000)]
    public async Task CreateDut_AliasWithoutDeclaration_ReturnsCleanError()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        var result = await _client!.CallToolAsync("twincat_create_dut",
            new Dictionary<string, object?>
            {
                ["parentPath"] = DutParentPath,
                ["name"] = "A_NoDeclProbe",
                ["dutType"] = "ALIAS",
            });
        var text = GetText(result);
        Assert.Contains("[Error]", text);
        Assert.Contains("ALIAS", text);
    }

    // ── Scenario 8: create_gvl round-trips its declaration ────────────────────

    [Trait("Description", "GVL mit Deklaration anlegen → erscheint als GVL in list_pous, Deklaration round-trippt über read_gvl.")]
    [SkippableFact(Timeout = 300_000)]
    public async Task CreateGvl_WithDeclaration_ShowsAsGvlAndRoundTrips()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"GVL_PlcCodeSpec_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string path = $"{GvlParentPath}^{name}";
        string decl = "VAR_GLOBAL\n\tg_RoundTrip : BOOL;\nEND_VAR";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_gvl",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = GvlParentPath, ["name"] = name, ["declaration"] = decl,
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_gvl error: {createText}");
            Assert.Contains("Created GVL", createText);

            var list = GetText(await _client.CallToolAsync("twincat_list_pous"));
            Assert.Matches($@"\bGVL\s+{System.Text.RegularExpressions.Regex.Escape(path)}", list);

            var read = GetText(await _client.CallToolAsync("twincat_read_gvl",
                new Dictionary<string, object?> { ["treePath"] = path }));
            Assert.Contains("g_RoundTrip", read);
        }
        finally { await TryDeleteAsync(path); }
    }

    // ── Scenario 9: create_gvl persistent carries PERSISTENT ──────────────────

    [Trait("Description", "Persistent-GVL anlegen → erscheint als GVL, Deklaration enthält PERSISTENT.")]
    [SkippableFact(Timeout = 300_000)]
    public async Task CreateGvl_Persistent_ShowsAsGvlWithPersistentKeyword()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"GVL_Pers_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string path = $"{GvlParentPath}^{name}";
        string decl = "VAR_GLOBAL PERSISTENT\n\tg_PersRoundTrip : INT;\nEND_VAR";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_gvl",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = GvlParentPath, ["name"] = name,
                    ["persistent"] = true, ["declaration"] = decl,
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_gvl persistent error: {createText}");
            Assert.Contains("Created persistent GVL", createText);

            var list = GetText(await _client.CallToolAsync("twincat_list_pous"));
            Assert.Matches($@"\bGVL\s+{System.Text.RegularExpressions.Regex.Escape(path)}", list);

            var read = GetText(await _client.CallToolAsync("twincat_read_gvl",
                new Dictionary<string, object?> { ["treePath"] = path }));
            Assert.Contains("PERSISTENT", read);
            Assert.Contains("g_PersRoundTrip", read);
        }
        finally { await TryDeleteAsync(path); }
    }

    // ── Scenario 9a: create_gvl persistent WITHOUT a declaration → ordinary GVL ────
    // Regression for fix/gvl-persistent-629: the scenario above writes its own declaration, so it
    // could not tell WHICH object was created. nSubType 629 is a Parameter List — live 2026-10-06 its
    // default skeleton is `VAR_GLOBAL CONSTANT` (saved file ParameterList="True"). This test reads the
    // skeleton the SERVER produced, so a Parameter List fails it.

    [Trait("Description", "Persistent-GVL ohne Deklaration → normale GVL mit leerem 'VAR_GLOBAL PERSISTENT'-Abschnitt, keine Parameterliste (kein 'CONSTANT').")]
    [SkippableFact(Timeout = 300_000)]
    public async Task CreateGvl_PersistentWithoutDeclaration_IsOrdinaryGvlNotParameterList()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"GVL_PersEmpty_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string path = $"{GvlParentPath}^{name}";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_gvl",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = GvlParentPath, ["name"] = name, ["persistent"] = true,
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_gvl persistent error: {createText}");
            Assert.Contains("Created persistent GVL", createText);

            var read = GetText(await _client.CallToolAsync("twincat_read_gvl",
                new Dictionary<string, object?> { ["treePath"] = path }));
            Assert.Contains("PERSISTENT", read);
            Assert.DoesNotContain("CONSTANT", read);
        }
        finally { await TryDeleteAsync(path); }
    }

    // ── Scenario 9b: create_interface — the live nSubType=618 (PLCITF) gate ────
    // The one empirically-unconfirmed value in create_interface is CreateChild's
    // nSubType (618). This is its live gate: if 618 is wrong, create surfaces a
    // clean [Error] naming the rejected code; if right, the node reads back as an
    // INTERFACE. Methods are added separately via add_pou_member (out of scope).

    [Trait("Description", "Neues INTERFACE erstellen → taucht in list_pous auf und ist per read_pou als INTERFACE lesbar (nSubType=618-Gate).")]
    [SkippableFact(Timeout = 240_000)]
    public async Task CreateInterface_New_ShowsUpInListAndIsReadable()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string name = $"I_PlcCodeSpecTest_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string expectedPath = $"{DefaultParentPath}^{name}";

        try
        {
            var create = await _client!.CallToolAsync("twincat_create_interface",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = DefaultParentPath,
                    ["name"] = name,
                });
            var createText = GetText(create);
            Assert.False(create.IsError == true, $"create_interface error: {createText}");
            Assert.Contains("Created INTERFACE", createText);
            Assert.Contains(name, createText);

            var list = GetText(await _client.CallToolAsync("twincat_list_pous"));
            Assert.Contains(expectedPath, list);

            var read = await _client.CallToolAsync("twincat_read_pou",
                new Dictionary<string, object?> { ["treePath"] = expectedPath });
            var readText = GetText(read);
            Assert.False(read.IsError == true, $"post-create read error: {readText}");
            Assert.Contains("INTERFACE", readText);
            Assert.Contains(name, readText);
        }
        finally
        {
            try
            {
                await _client!.CallToolAsync("twincat_delete_plc_object",
                    new Dictionary<string, object?> { ["treePath"] = expectedPath });
            }
            catch { /* best-effort teardown */ }
        }
    }

    // ── Scenario 10: bad-container failure surfaces as [Error], not -32603 ─────
    // The other half of #104 (Bug B): a CreateChild/placement failure must reach
    // the caller as a normal "[Error] …" string, never as a transport-level error.

    [Trait("Description", "create_dut unter einer POU (ungültiger Container) → sauberer [Error]-String, kein Transport-Fehler.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task CreateDut_UnderPou_ReturnsCleanErrorNotTransportFault()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        var result = await _client!.CallToolAsync("twincat_create_dut",
            new Dictionary<string, object?>
            {
                ["parentPath"] = FixtureLayout.MainPath, // MAIN — a POU leaf, not a container
                ["name"] = "ST_BadContainerProbe",
                ["dutType"] = "STRUCT",
            });
        var text = GetText(result);

        // The MCP boundary must return a content payload (the "[Error] …" string),
        // not a transport fault. A masked -32603 would surface as IsError with no
        // "[Error] " text — exactly the failure mode #104 describes.
        Assert.StartsWith("[Error]", text);
        Assert.False(string.IsNullOrWhiteSpace(text), "expected a non-empty [Error] message, got an empty/transport fault");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task TryDeleteAsync(string treePath)
    {
        try
        {
            await _client!.CallToolAsync("twincat_delete_plc_object",
                new Dictionary<string, object?> { ["treePath"] = treePath });
        }
        catch { /* best-effort teardown */ }
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }

    // McpReadPou formats output as:
    //   Read POU '...' (ST).
    //   --- Declaration ---
    //   {declaration}
    //   --- Implementation ---
    //   {implementation}
    // Return everything after the implementation marker, untrimmed.
    private static string ExtractImplementation(string mcpText)
    {
        const string marker = "--- Implementation ---";
        int i = mcpText.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return string.Empty;
        int start = i + marker.Length;
        if (start < mcpText.Length && mcpText[start] == '\n') start++;
        else if (start + 1 < mcpText.Length && mcpText[start] == '\r' && mcpText[start + 1] == '\n') start += 2;
        return mcpText.Substring(start);
    }

    private static string Normalise(string s) => s.Replace("\r\n", "\n").TrimEnd();
}
