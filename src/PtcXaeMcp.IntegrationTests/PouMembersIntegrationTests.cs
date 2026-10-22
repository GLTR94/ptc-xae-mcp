using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// End-to-end coverage for feature/pou-handling against the live MCP server +
/// XAE. Serialized assembly-wide (AssemblyInfo: DisableTestParallelization)
/// against the single live XAE per PR #17.
///
/// <para><b>Safety rule (learned the hard way 2026-05-15).</b> These scenarios
/// must NEVER read, write, or "restore" the fixture's real <c>MAIN</c> (or any
/// pre-existing shared POU), and must never save the user's project. An
/// earlier helper that round-tripped MAIN's declaration through formatted MCP
/// text corrupted a real production project. Instead each scenario creates its
/// OWN throwaway <c>PRG_PouSpec_{timestamp}</c> PROGRAM that instantiates the
/// test FB and exercises its members, then deletes both the program and the FB
/// in <c>finally</c>. The fixture's MAIN is never touched.</para>
///
/// <para><b>Compilation-reachability.</b> TwinCAT drops an FB/member that
/// nothing references. The throwaway PROGRAM references the FB, calls its
/// METHOD / ACTION and reads its PROPERTY, so a clean <c>twincat_build_plc</c>
/// proves the members were actually compiled.</para>
///
/// <para><b>METHOD (issue #19).</b> <c>twincat_add_pou_member kind=METHOD</c>
/// works on a freshly <c>twincat_create_pou</c>'d FB. The earlier "Name
/// mismatch"/C0243 was caused by reserved-word method names (CALC, ADD — IL
/// operators); the name must be a valid, non-reserved IEC 61131-3 identifier.
/// Scenario 1 below adds a real METHOD ("Compute"), gives it a body, calls it
/// from the scratch POU and asserts a green build.</para>
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class PouMembersIntegrationTests : IAsyncLifetime, IClassFixture<PouSpecCleanupFixture>
{
    private const string ServerUrl = "http://localhost:5010/";
    private static string DefaultParentPath => FixtureLayout.PousFolder;

    private McpClient? _client;
    private bool _ready;
    private string _fixtureSln = string.Empty;

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is null) { LiveXae.Announce(false, string.Empty); return; }
        _fixtureSln = fixture;

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

    // ── Scenario 0: members inside a folder under a FB are listed ─────────────
    // fix/pou-members-in-folders: the member walk descended only into PROPERTY, so a FB that
    // groups its members in a folder (TC >= 4024.14) listed "0 PROPERTY". The fixture FB
    // FB_FolderMembers keeps PROPERTY Enabled (Get/Set) and METHOD Reset in folder "Members".
    // Read-only: nothing is created, written or deleted.

    [Trait("Description", "FB mit Members in einem Unterordner → list_pou_members zeigt PROPERTY inkl. Get/Set und METHOD aus dem Ordner (nur lesend).")]
    [SkippableFact(Timeout = 240_000)]
    public async Task ListMembers_MembersInsideFolder_AreListed()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string pou = FixtureLayout.FolderMembersPouPath;
        string folder = $"{pou}^{FixtureLayout.FolderMembersFolder}";
        var list = GetText(await _client!.CallToolAsync("twincat_list_pou_members",
            new Dictionary<string, object?> { ["pouPath"] = pou }));

        Assert.DoesNotContain("[Error]", list);
        Assert.Contains($"{folder}^Enabled", list);
        Assert.Contains($"{folder}^Enabled^Get", list);
        Assert.Contains($"{folder}^Enabled^Set", list);
        Assert.Contains($"{folder}^Reset", list);
        Assert.Contains("1 PROPERTY", list);
        Assert.Contains("1 METHOD", list);
    }

    // ── Scenario 1: FB + ACTION + PROPERTY, wired into a throwaway PRG ─────────

    [Trait("Description", "FB mit ACTION und PROPERTY anlegen, in Scratch-POU verdrahten → Build grün.")]
    [SkippableFact(Timeout = 600_000)]
    public async Task AddMembers_WiredIntoOwnProgram_BuildsClean()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string stamp = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string fb = $"FB_PouSpec_{stamp}";
        string fbPath = $"{DefaultParentPath}^{fb}";

        await ScratchPou.EnsureAsync(_client!);
        try
        {
            await CreateOk("twincat_create_pou", new()
            {
                ["parentPath"] = DefaultParentPath, ["name"] = fb,
                ["pouType"] = "FUNCTION_BLOCK",
            }, "Created FUNCTION_BLOCK");

            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = fbPath, ["kind"] = "ACTION", ["name"] = "Reset",
            }, "Added ACTION");

            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = fbPath, ["kind"] = "PROPERTY",
                ["name"] = "Level", ["returnType"] = "INT",
            }, "Added PROPERTY");

            // METHOD add (issue #19): works on a freshly create_pou'd FB with a
            // valid, non-reserved IEC 61131-3 name. "Compute" is fine; CALC/ADD
            // (IL operators) would be rejected by TC3 with "Name mismatch".
            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = fbPath, ["kind"] = "METHOD",
                ["name"] = "Compute", ["returnType"] = "INT",
            }, "Added METHOD");

            // Give the METHOD a real signature + body so the build exercises it
            // (also proves write_pou_member round-trips a METHOD).
            var wm = await _client!.CallToolAsync("twincat_write_pou_member",
                new Dictionary<string, object?>
                {
                    ["memberPath"] = $"{fbPath}^Compute",
                    ["declaration"] = "METHOD Compute : INT\r\nVAR_INPUT\r\n\ta : INT;\r\n\tb : INT;\r\nEND_VAR",
                    ["implementation"] = "Compute := a + b;",
                });
            Assert.False(wm.IsError == true, $"write METHOD body failed: {GetText(wm)}");

            // list surfaces the METHOD, the ACTION, the PROPERTY + Get/Set.
            var list = GetText(await _client!.CallToolAsync("twincat_list_pou_members",
                new Dictionary<string, object?> { ["pouPath"] = fbPath }));
            Assert.Contains("METHOD", list);
            Assert.Contains("ACTION", list);
            Assert.Contains("PROPERTY", list);
            Assert.Contains("GET", list);
            Assert.Contains("SET", list);

            // The METHOD declaration must carry the requested INT return type —
            // the scalar-vInfo path would otherwise leave "... : 1".
            var rm = GetText(await _client!.CallToolAsync("twincat_read_pou_member",
                new Dictionary<string, object?> { ["memberPath"] = $"{fbPath}^Compute" }));
            Assert.Contains("METHOD Compute : INT", rm);
            await ReadOk($"{fbPath}^Reset");
            await ReadOk($"{fbPath}^Level");

            // Wire the FB into the build-reachable scratch POU so its members
            // are actually compiled (an unreferenced FB is dropped by the
            // build). MAIN itself is never touched beyond the scratch hook.
            await ScratchPou.WriteAsync(_client!,
                $"PROGRAM {ScratchPou.Name}\r\nVAR\r\n\tfb{fb} : {fb};\r\n\tiLevel : INT;\r\n\tiSum : INT;\r\nEND_VAR",
                $"iSum := fb{fb}.Compute(a := 2, b := 3);\r\nfb{fb}.Reset();\r\niLevel := fb{fb}.Level;");

            var build = GetText(await _client!.CallToolAsync("twincat_build_plc",
                new Dictionary<string, object?>()));
            Assert.Contains("Build succeeded", build, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ScratchPou.ResetAsync(_client!);
            await DeleteQuiet(fbPath);
        }
    }

    // ── Scenario 1b: interface METHOD live gate (nSubType 610) + build green ───
    // THE primary live gate for interface members. add_pou_member(kind=METHOD) on
    // an INTERFACE must use subtype 610 (vInfo[0]=return type), NOT the POU method
    // 609 — the latter fails with CreateChild "Kann nicht einfuegen". If 610 is
    // wrong, the add surfaces a clean [Error] naming the rejected code. If right,
    // the method declaration round-trips and an FB that IMPLEMENTS the interface
    // (providing a matching method) compiles green — proving the interface method
    // is a real, implementable member.

    [Trait("Description", "INTERFACE mit METHOD (Subtyp 610) anlegen, Deklaration round-trippen, FB IMPLEMENTS → Build grün.")]
    [SkippableFact(Timeout = 600_000)]
    public async Task AddInterfaceMethod_ImplementedByFb_BuildsClean()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string stamp = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string itf = $"I_PouSpec_{stamp}";
        string itfPath = $"{DefaultParentPath}^{itf}";
        string fb = $"FB_PouSpecImpl_{stamp}";
        string fbPath = $"{DefaultParentPath}^{fb}";

        // The interface method and the FB's implementing method must share an
        // identical signature, so both are written from the same declaration body
        // (only the METHOD keyword line is shared shape; interfaces are decl-only).
        string methodVars = "VAR_INPUT\r\n\tPlace : INT;\r\n\tToWorking : BOOL;\r\n\tToHome : BOOL;\r\nEND_VAR";

        await ScratchPou.EnsureAsync(_client!);
        try
        {
            await CreateOk("twincat_create_interface", new()
            {
                ["parentPath"] = DefaultParentPath, ["name"] = itf,
            }, "Created INTERFACE");

            // GATE: METHOD on an interface — subtype 610. A wrong subtype fails here.
            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = itfPath, ["kind"] = "METHOD",
                ["name"] = "SetCoil", ["returnType"] = "BOOL",
            }, "Added METHOD");

            // Declaration round-trips on the interface method (declaration-only).
            var wmItf = await _client!.CallToolAsync("twincat_write_pou_member",
                new Dictionary<string, object?>
                {
                    ["memberPath"] = $"{itfPath}^SetCoil",
                    ["declaration"] = $"METHOD SetCoil : BOOL\r\n{methodVars}",
                });
            Assert.False(wmItf.IsError == true, $"write interface METHOD decl failed: {GetText(wmItf)}");
            var rmItf = GetText(await _client!.CallToolAsync("twincat_read_pou_member",
                new Dictionary<string, object?> { ["memberPath"] = $"{itfPath}^SetCoil" }));
            Assert.Contains("METHOD SetCoil : BOOL", rmItf);
            Assert.Contains("Place", rmItf);

            // list surfaces the interface + its method; read_pou shows it as an INTERFACE.
            var members = GetText(await _client!.CallToolAsync("twincat_list_pou_members",
                new Dictionary<string, object?> { ["pouPath"] = itfPath }));
            Assert.Contains("METHOD", members);
            Assert.Contains("SetCoil", members);
            Assert.Contains(itfPath,
                GetText(await _client!.CallToolAsync("twincat_list_pous")));

            // FB IMPLEMENTS the interface and provides a matching SetCoil method.
            await CreateOk("twincat_create_pou", new()
            {
                ["parentPath"] = DefaultParentPath, ["name"] = fb,
                ["pouType"] = "FUNCTION_BLOCK",
            }, "Created FUNCTION_BLOCK");
            var wfb = await _client!.CallToolAsync("twincat_write_pou_body",
                new Dictionary<string, object?>
                {
                    ["treePath"] = fbPath,
                    ["declaration"] = $"FUNCTION_BLOCK {fb} IMPLEMENTS {itf}",
                    ["implementation"] = "",
                });
            Assert.False(wfb.IsError == true, $"write FB decl failed: {GetText(wfb)}");
            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = fbPath, ["kind"] = "METHOD",
                ["name"] = "SetCoil", ["returnType"] = "BOOL",
            }, "Added METHOD");
            var wm = await _client!.CallToolAsync("twincat_write_pou_member",
                new Dictionary<string, object?>
                {
                    ["memberPath"] = $"{fbPath}^SetCoil",
                    ["declaration"] = $"METHOD SetCoil : BOOL\r\n{methodVars}",
                    ["implementation"] = "SetCoil := ToWorking AND NOT ToHome;",
                });
            Assert.False(wm.IsError == true, $"write FB METHOD body failed: {GetText(wm)}");

            // Wire the FB (typed as the interface) into the build-reachable scratch POU.
            await ScratchPou.WriteAsync(_client!,
                $"PROGRAM {ScratchPou.Name}\r\nVAR\r\n\tfb{stamp} : {fb};\r\n\tip{stamp} : {itf};\r\n\tb : BOOL;\r\nEND_VAR",
                $"ip{stamp} := fb{stamp};\r\nb := ip{stamp}.SetCoil(Place := 1, ToWorking := TRUE, ToHome := FALSE);");

            var build = GetText(await _client!.CallToolAsync("twincat_build_plc",
                new Dictionary<string, object?>()));
            Assert.Contains("Build succeeded", build, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ScratchPou.ResetAsync(_client!);
            await DeleteQuiet(fbPath);
            await DeleteQuiet(itfPath);
        }
    }

    // ── Scenario 1c: interface PROPERTY live gate (612 / 654 / 655) ────────────
    // Lower-priority companion to 1b: confirms CreateChild accepts the interface
    // PROPERTY subtype 612 and the accessor subtypes 654/655 (no build — the
    // method path is the must-have; this just proves the property subtypes land).

    [Trait("Description", "INTERFACE mit PROPERTY (Subtyp 612 + GET 654/SET 655) anlegen → erscheint in list_pou_members.")]
    [SkippableFact(Timeout = 240_000)]
    public async Task AddInterfaceProperty_SurfacesGetAndSet()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string stamp = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string itf = $"I_PouSpecProp_{stamp}";
        string itfPath = $"{DefaultParentPath}^{itf}";

        try
        {
            await CreateOk("twincat_create_interface", new()
            {
                ["parentPath"] = DefaultParentPath, ["name"] = itf,
            }, "Created INTERFACE");

            // GATE: PROPERTY on an interface — subtype 612 with accessors 654/655.
            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = itfPath, ["kind"] = "PROPERTY",
                ["name"] = "Enabled", ["returnType"] = "BOOL",
            }, "Added PROPERTY");

            var members = GetText(await _client!.CallToolAsync("twincat_list_pou_members",
                new Dictionary<string, object?> { ["pouPath"] = itfPath }));
            Assert.Contains("PROPERTY", members);
            Assert.Contains("Enabled", members);
            Assert.Contains("GET", members);
            Assert.Contains("SET", members);

            // ACTION must be rejected on an interface with a clear, non-transport error.
            var badAction = await _client!.CallToolAsync("twincat_add_pou_member",
                new Dictionary<string, object?>
                {
                    ["pouPath"] = itfPath, ["kind"] = "ACTION", ["name"] = "DoWork",
                });
            var badText = GetText(badAction);
            Assert.StartsWith("[Error]", badText);
            Assert.Contains("interface", badText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteQuiet(itfPath);
        }
    }

    // ── Scenario 2: rename action + FB, delete action; bad-then-good build ─────

    [Trait("Description", "ACTION umbenennen + löschen + FB umbenennen → Build schlägt fehl, nach Korrektur wieder grün.")]
    [SkippableFact(Timeout = 600_000)]
    public async Task RenameAndDelete_BadBuildThenFixed()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        // Millisecond precision (matches scenarios 1 & 3) — a coarser second-stamp
        // widened the cross-run collision window that breeds TwinCAT _1 suffixes (#94).
        string stamp = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string fb = $"FB_PouSpec_{stamp}";
        string prg = $"PRG_PouSpec_{stamp}";
        string fbPath = $"{DefaultParentPath}^{fb}";

        await ScratchPou.EnsureAsync(_client!);
        try
        {
            await CreateOk("twincat_create_pou", new()
            {
                ["parentPath"] = DefaultParentPath, ["name"] = fb,
                ["pouType"] = "FUNCTION_BLOCK",
            }, "Created FUNCTION_BLOCK");
            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = fbPath, ["kind"] = "ACTION", ["name"] = "DoWork",
            }, "Added ACTION");
            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = fbPath, ["kind"] = "ACTION", ["name"] = "Clear",
            }, "Added ACTION");

            // Wire into the build-reachable scratch POU (not a dead PROGRAM).
            await ScratchPou.WriteAsync(_client!,
                $"PROGRAM {ScratchPou.Name}\r\nVAR\r\n\tfb{fb} : {fb};\r\nEND_VAR",
                $"fb{fb}.DoWork();\r\nfb{fb}.Clear();");

            var b0 = GetText(await _client!.CallToolAsync("twincat_build_plc",
                new Dictionary<string, object?>()));
            Assert.Contains("Build succeeded", b0, StringComparison.OrdinalIgnoreCase);

            // Rename the first action and the FB; delete the second action.
            var rm = await _client.CallToolAsync("twincat_rename_plc_object",
                new Dictionary<string, object?>
                {
                    ["treePath"] = $"{fbPath}^DoWork", ["newName"] = "DoWork2",
                });
            // String tools report failures as "[Error] ..." text, not IsError (issue #190: a refused
            // delete went unnoticed, and the later build check failed for the wrong reason).
            Assert.False(rm.IsError == true, GetText(rm));
            Assert.DoesNotContain("[Error]", GetText(rm), StringComparison.Ordinal);

            var del = await _client.CallToolAsync("twincat_delete_plc_object",
                new Dictionary<string, object?> { ["treePath"] = $"{fbPath}^Clear" });
            Assert.False(del.IsError == true, GetText(del));
            Assert.DoesNotContain("[Error]", GetText(del), StringComparison.Ordinal);

            var rfb = await _client.CallToolAsync("twincat_rename_plc_object",
                new Dictionary<string, object?>
                {
                    ["treePath"] = fbPath, ["newName"] = fb + "_R",
                });
            Assert.False(rfb.IsError == true, GetText(rfb));
            Assert.DoesNotContain("[Error]", GetText(rfb), StringComparison.Ordinal);
            fbPath = $"{DefaultParentPath}^{fb}_R";

            // BAD build: must fail because the scratch POU still calls the deleted Clear action (and,
            // since the renames answer TwinCAT's refactoring prompt with the default "Nein", still uses
            // the old names). Issue #190: with a refused delete every build stayed green.
            // The rename can lag the SaveBeforeBuild+compile: the first build
            // after a just-issued rename may still compile the stale pre-rename
            // tree and report success (issue #66 state-bleed/timing family,
            // same as the post-reopen window). Poll the build until it reflects
            // the rename (i.e. fails) before asserting.
            var bad = await BuildUntil(
                b => b.Contains("Build failed", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Build failed", bad, StringComparison.OrdinalIgnoreCase);

            // Fix the scratch POU to the new names and rebuild green.
            await ScratchPou.WriteAsync(_client!,
                $"PROGRAM {ScratchPou.Name}\r\nVAR\r\n\tfb{fb} : {fb}_R;\r\nEND_VAR",
                $"fb{fb}.DoWork2();");
            var good = GetText(await _client.CallToolAsync("twincat_build_plc",
                new Dictionary<string, object?>()));
            Assert.Contains("Build succeeded", good, StringComparison.OrdinalIgnoreCase);

            // Deterministic read: the tree can lag the just-completed
            // rename+rebuild, so the first listing may still show the old
            // member view (issue #73, #66 suite-order family). Poll until it
            // reflects the rename before asserting.
            var members = await ListMembersUntil(fbPath,
                m => m.Contains("DoWork2", StringComparison.Ordinal)
                  && !m.Contains("Clear", StringComparison.Ordinal));
            Assert.Contains("DoWork2", members);
            Assert.DoesNotContain("Clear", members);
        }
        finally
        {
            await ScratchPou.ResetAsync(_client!);
            // Cover both the renamed form (fbPath == {fb}_R, the success path) and
            // the original (in case the rename never took, which would otherwise
            // orphan {fb} — issue #94). The suite-end sweep is the final backstop.
            await DeleteQuiet(fbPath);
            await DeleteQuiet($"{DefaultParentPath}^{fb}");
        }
    }

    // ── Scenario 3: plcopen export → delete → import → rebuild green ──────────

    [Trait("Description", "FB exportieren (PLCopen XML), löschen, reimportieren → Build wieder grün.")]
    [SkippableFact(Timeout = 600_000)]
    public async Task PlcOpenRoundTrip_ExportDeleteImport_BuildsClean()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string stamp = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        string fb = $"FB_PouSpec_{stamp}";
        string fbPath = $"{DefaultParentPath}^{fb}";
        string xml = Path.Combine(Path.GetTempPath(), fb + ".xml");

        await ScratchPou.EnsureAsync(_client!);
        try
        {
            await CreateOk("twincat_create_pou", new()
            {
                ["parentPath"] = DefaultParentPath, ["name"] = fb,
                ["pouType"] = "FUNCTION_BLOCK",
            }, "Created FUNCTION_BLOCK");
            await CreateOk("twincat_add_pou_member", new()
            {
                ["pouPath"] = fbPath, ["kind"] = "ACTION", ["name"] = "Tick",
            }, "Added ACTION");

            // Wire into the build-reachable scratch POU.
            await ScratchPou.WriteAsync(_client!,
                $"PROGRAM {ScratchPou.Name}\r\nVAR\r\n\tfb{fb} : {fb};\r\nEND_VAR",
                $"fb{fb}.Tick();");

            var b0 = GetText(await _client!.CallToolAsync("twincat_build_plc",
                new Dictionary<string, object?>()));
            Assert.Contains("Build succeeded", b0, StringComparison.OrdinalIgnoreCase);

            // Issue #56: in the full suite (not in isolation) PlcOpenExport
            // intermittently fails to resolve the freshly-created FB, surfacing
            // as "export file missing". twincat_build_plc does SaveBeforeBuild,
            // but a preceding test can leave the tree in a state where the FB is
            // not yet persisted where PlcOpenExport's resolver looks. Force an
            // explicit Save All immediately before the export so the new FB is
            // on disk regardless of suite order.
            Assert.False(
                GetText(await _client!.CallToolAsync("twincat_save_project",
                    new Dictionary<string, object?>())).StartsWith("[Error]", StringComparison.Ordinal),
                "twincat_save_project failed before PlcOpenExport (issue #56)");

            // Export the FB (selector = "POUs.<fb>", verified form).
            var exp = await _client.CallToolAsync("twincat_plcopen_export",
                new Dictionary<string, object?>
                {
                    ["filePath"] = xml, ["selector"] = $"POUs.{fb}",
                });
            Assert.False(exp.IsError == true, GetText(exp));
            // Surfaces the actual PlcOpenExport error (text-based "[Error]") before checking the file.
            // See issue #56 for suite-order flake investigation.
            Assert.DoesNotContain("[Error]", GetText(exp), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(xml), $"export file missing: {xml}");

            // Stop referencing the FB, delete it, then re-import from XML.
            // Issue #98: save after delete so TwinCAT commits the removal before
            // the import runs — without this, the node lingers in the in-memory tree
            // and the import creates FB_PouSpec_<stamp>_1 instead of replacing it.
            await ScratchPou.ResetAsync(_client!);
            await DeleteQuiet(fbPath);
            Assert.False(
                GetText(await _client!.CallToolAsync("twincat_save_project",
                    new Dictionary<string, object?>())).StartsWith("[Error]", StringComparison.Ordinal),
                "twincat_save_project failed after delete, before PlcOpenImport (issue #98)");

            var impSkip = await _client.CallToolAsync("twincat_plcopen_import",
                new Dictionary<string, object?>
                {
                    ["filePath"] = xml, ["conflictMode"] = "Skip",
                });
            Assert.False(impSkip.IsError == true, GetText(impSkip));

            var impReplace = await _client.CallToolAsync("twincat_plcopen_import",
                new Dictionary<string, object?>
                {
                    ["filePath"] = xml, ["conflictMode"] = "Replace",
                });
            Assert.False(impReplace.IsError == true, GetText(impReplace));

            // Re-wire via the scratch POU and assert the round-tripped FB builds.
            await ScratchPou.WriteAsync(_client!,
                $"PROGRAM {ScratchPou.Name}\r\nVAR\r\n\tfb{fb} : {fb};\r\nEND_VAR",
                $"fb{fb}.Tick();");
            var good = GetText(await _client.CallToolAsync("twincat_build_plc",
                new Dictionary<string, object?>()));
            Assert.Contains("Build succeeded", good, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ScratchPou.ResetAsync(_client!);
            await DeleteQuiet(fbPath);
            await DeleteQuiet(fbPath + "_1");   // backstop: issue #98 _1 duplicate
            try { if (File.Exists(xml)) File.Delete(xml); } catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task OpenFixtureAsync()
    {
        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        // The just-(re)opened solution can momentarily report a non-ready PLC
        // tree — seen after an activate_config ran earlier in the same pass (it
        // used to run in Stage XAE; since issue #195 it runs in Stage XAR). A
        // runtime restart temporarily leaves the PLC tree in a
        // locked/transitional state where CreateChild fails. Wait here until
        // twincat_list_pous returns real POUs, matching the same resilience
        // idiom used by PlcCodeIntegrationTests (issue #66 family).
        await PlcProjectReadiness.AwaitAsync(_client!);
    }

    private async Task CreateOk(string tool, Dictionary<string, object?> args, string expect)
    {
        var r = await _client!.CallToolAsync(tool, args);
        var t = GetText(r);
        Assert.False(r.IsError == true, $"{tool} error: {t}");
        Assert.Contains(expect, t, StringComparison.OrdinalIgnoreCase);
    }

    // A member must be readable without error and come back in the standard
    // envelope. The declaration/implementation *content* differs by kind (an
    // ACTION has no declaration; a freshly-added member's body is empty), so
    // we assert the read succeeds and is well-formed, not specific text.
    private async Task ReadOk(string memberPath)
    {
        var r = await _client!.CallToolAsync("twincat_read_pou_member",
            new Dictionary<string, object?> { ["memberPath"] = memberPath });
        var t = GetText(r);
        Assert.False(r.IsError == true, $"read_pou_member error: {t}");
        Assert.DoesNotContain("[Error]", t);
        Assert.Contains("Read member", t);
        Assert.Contains("--- Implementation ---", t);
    }

    // Writes a body to one of OUR throwaway POUs only (never MAIN).
    private async Task WriteBodyOk(string treePath, string decl, string impl)
    {
        var w = await _client!.CallToolAsync("twincat_write_pou_body",
            new Dictionary<string, object?>
            {
                ["treePath"] = treePath,
                ["declaration"] = decl,
                ["implementation"] = impl,
            });
        Assert.False(w.IsError == true, $"write_pou_body error ({treePath}): {GetText(w)}");
    }

    // Best-effort teardown delete that actually sticks (issue #94). It (1) persists
    // the delete with a Save All — an unsaved tree edit is lost when the next
    // scenario reopens the fixture with closeCurrentFirst, leaving the node behind;
    // and (2) surfaces a delete that did not take (logged, not thrown, so a flaky
    // teardown can't fail an otherwise-passing test) — the suite-end
    // PouSpecCleanupFixture sweeps any survivor. A "not found" means the node is
    // already gone, which is the desired outcome, so it is treated as success.
    private async Task DeleteQuiet(string treePath)
    {
        try
        {
            var t = GetText(await _client!.CallToolAsync("twincat_delete_plc_object",
                new Dictionary<string, object?> { ["treePath"] = treePath }));

            await _client!.CallToolAsync("twincat_save_project",
                new Dictionary<string, object?>());

            if (t.StartsWith("[Error]", StringComparison.Ordinal) &&
                !t.Contains("not found", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"  ⚠ teardown: delete '{treePath}' did not take: {t}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ teardown: delete '{treePath}' threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Read <c>twincat_list_pou_members</c> with a bounded retry. The tree can
    /// lag a just-completed rename+rebuild (issue #73, same suite-order family
    /// as #66): the renamed member is not yet reflected in the first listing.
    /// Polls until <paramref name="accept"/> holds or the budget is exhausted,
    /// then returns the last listing so a never-converged read still produces a
    /// meaningful assertion failure message.
    /// </summary>
    private async Task<string> ListMembersUntil(
        string pouPath, Func<string, bool> accept, int maxAttempts = 5)
    {
        string members = string.Empty;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            members = GetText(await _client!.CallToolAsync("twincat_list_pou_members",
                new Dictionary<string, object?> { ["pouPath"] = pouPath }));
            if (accept(members))
                return members;
            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(2));
        }
        return members;
    }

    /// <summary>
    /// Rebuild the PLC with a bounded retry until <paramref name="accept"/>
    /// holds. A build issued immediately after a tree mutation (rename/delete)
    /// can compile the stale pre-mutation project and report the wrong verdict
    /// (issue #66 state-bleed/timing family); polling rides out the
    /// save/propagate window. Returns the last build text so a never-converged
    /// build still yields a meaningful assertion message.
    /// </summary>
    private async Task<string> BuildUntil(
        Func<string, bool> accept, int maxAttempts = 6)
    {
        string build = string.Empty;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            build = GetText(await _client!.CallToolAsync("twincat_build_plc",
                new Dictionary<string, object?>()));
            if (accept(build))
                return build;
            if (attempt < maxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(3));
        }
        return build;
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
