using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Opens the fixture .sln then calls twincat_get_fieldbus_topology against the
/// live MCP server and XAE. Skips unless PTC_XAE_MCP_FIXTURE_SLN is set
/// and the MCP server is reachable on http://localhost:5010.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class FieldbusIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
    private const string FixtureEnvVar = "PTC_XAE_MCP_FIXTURE_SLN";
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

    [Trait("Description", "Feldbustopologie lesen → Antwort beginnt mit \"Found\" (mit JSON) oder \"No fieldbus\".")]
    [SkippableFact(Timeout = 180_000)]
    public async Task GetFieldbusTopology_AfterOpen_ReturnsTopologyOrEmpty()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var topo = await _client.CallToolAsync("twincat_get_fieldbus_topology");
        var text = GetText(topo);

        Assert.False(topo.IsError == true, $"Topology call returned error: {text}");
        bool ok = text.StartsWith("Found", StringComparison.Ordinal)
            || text.StartsWith("No fieldbus", StringComparison.OrdinalIgnoreCase);
        Assert.True(ok, $"Unexpected topology response: {text}");

        if (text.StartsWith("Found", StringComparison.Ordinal))
        {
            Assert.Contains("\"kind\":", text);
            Assert.Contains("\"devices\":", text);
        }
    }

    [Trait("Description", "Geräte-XML mit Root-Pfad (TIID) anfordern → muss mit \"[Error] root\" abgewiesen werden.")]
    [SkippableFact(Timeout = 180_000)]
    public async Task GetDeviceXml_RejectsRootPath()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var result = await _client.CallToolAsync("twincat_get_device_xml",
            new Dictionary<string, object?> { ["treePath"] = "TIID" });
        var text = GetText(result);
        Assert.StartsWith("[Error]", text);
        Assert.Contains("root", text, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Description", "Erstes Gerät aus der Topologie auslesen → XML-Antwort oder (falls COM-Fehler) [Error].")]
    [SkippableFact(Timeout = 180_000)]
    public async Task GetDeviceXml_FirstDeviceFromTopology_ReturnsXml()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var topo = await _client.CallToolAsync("twincat_get_fieldbus_topology");
        var topoText = GetText(topo);
        if (!topoText.StartsWith("Found", StringComparison.Ordinal))
        {
            // Fixture has no fieldbus devices — nothing more to assert.
            return;
        }

        int jsonStart = topoText.IndexOf('{');
        Assert.True(jsonStart >= 0, "Topology output did not contain JSON body.");
        using var doc = JsonDocument.Parse(topoText[jsonStart..]);
        var firstDevicePath = doc.RootElement
            .GetProperty("devices")[0]
            .GetProperty("path")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstDevicePath));

        var xmlResult = await _client.CallToolAsync("twincat_get_device_xml",
            new Dictionary<string, object?> { ["treePath"] = firstDevicePath });
        var xmlText = GetText(xmlResult);

        bool xmlLooking = xmlText.Contains('<') && xmlText.Contains('>');
        bool errored = xmlText.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(xmlLooking || errored,
            $"Expected XML-looking response or [Error], got: {xmlText}");
    }

    [Trait("Description", "EtherCAT-Slave XML lesen und unverändert zurückschreiben → Round-Trip ohne Fehler.")]
    [SkippableFact(Timeout = 180_000)]
    public async Task ConfigureEtherCatSlave_RoundTrip_WriteAndRestore()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var topo = await _client.CallToolAsync("twincat_get_fieldbus_topology");
        var topoText = GetText(topo);
        if (!topoText.StartsWith("Found", StringComparison.Ordinal))
            return; // No devices — nothing to configure.

        int jsonStart = topoText.IndexOf('{');
        Assert.True(jsonStart >= 0);
        using var doc = JsonDocument.Parse(topoText[jsonStart..]);
        var firstDevicePath = doc.RootElement
            .GetProperty("devices")[0]
            .GetProperty("path")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstDevicePath));

        // Read current XML.
        var xmlResult = await _client.CallToolAsync("twincat_get_device_xml",
            new Dictionary<string, object?> { ["treePath"] = firstDevicePath });
        var originalXml = GetText(xmlResult);
        if (originalXml.StartsWith("[Error]", StringComparison.Ordinal))
            return; // Can't get XML — skip write.

        // Write it back unchanged (round-trip).
        var configResult = await _client.CallToolAsync("twincat_configure_ethercat_slave",
            new Dictionary<string, object?> { ["treePath"] = firstDevicePath, ["xml"] = originalXml });
        var configText = GetText(configResult);

        Assert.False(configText.StartsWith("[Error]", StringComparison.Ordinal),
            $"ConfigureEtherCatSlave failed: {configText}");
        Assert.Contains("configured", configText, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Description", "Topologie darf keinen Gerätepfad doppelt enthalten (Duplikat-Schutz).")]
    [SkippableFact(Timeout = 180_000)]
    public async Task GetFieldbusTopology_NoDuplicatePaths()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var topo = await _client.CallToolAsync("twincat_get_fieldbus_topology");
        var text = GetText(topo);
        if (!text.StartsWith("Found", StringComparison.Ordinal))
            return; // No devices — nothing to assert.

        int jsonStart = text.IndexOf('{');
        Assert.True(jsonStart >= 0);
        using var doc = JsonDocument.Parse(text[jsonStart..]);

        var allPaths = new List<string>();
        CollectPaths(doc.RootElement.GetProperty("devices"), allPaths);

        var duplicates = allPaths
            .GroupBy(p => p)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Trait("Description", "EK1122-Knoten in der Topologie müssen das Feld \"physicalPort\" besitzen.")]
    [SkippableFact(Timeout = 180_000)]
    public async Task GetFieldbusTopology_EK1122Nodes_HavePhysicalPort()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));

        var topo = await _client.CallToolAsync("twincat_get_fieldbus_topology");
        var text = GetText(topo);
        if (!text.StartsWith("Found", StringComparison.Ordinal))
            return;

        int jsonStart = text.IndexOf('{');
        Assert.True(jsonStart >= 0);
        using var doc = JsonDocument.Parse(text[jsonStart..]);

        var ek1122Children = new List<JsonElement>();
        CollectEK1122Children(doc.RootElement.GetProperty("devices"), ek1122Children);

        // If there are any EK1122 children with a port, they must have a physicalPort
        // (seed file covers EK1122 A→X1, B→X2).
        foreach (var child in ek1122Children)
        {
            if (child.TryGetProperty("port", out var portProp) &&
                portProp.ValueKind != JsonValueKind.Null)
            {
                Assert.True(
                    child.TryGetProperty("physicalPort", out var phys) &&
                    phys.ValueKind != JsonValueKind.Null,
                    $"EK1122 child missing physicalPort: {child}");
            }
        }
    }

    [Trait("Description", "EtherCAT-Box hinzufügen → in Topologie sichtbar → wieder entfernen (Round-Trip, Fixture bleibt unverändert).")]
    [SkippableFact(Timeout = 240_000)]
    public async Task AddThenRemoveEtherCatBox_RoundTrip_LeavesFixtureUnchanged()
    {
        SkipIfNotReady();

        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?> { ["slnPath"] = _fixtureSln, ["closeCurrentFirst"] = true });
        Assert.False(open.IsError == true, GetText(open));

        var topo = await _client.CallToolAsync("twincat_get_fieldbus_topology");
        var topoText = GetText(topo);
        if (!topoText.StartsWith("Found", StringComparison.Ordinal))
            return; // No fieldbus devices in fixture — nothing to exercise.

        using var doc = JsonDocument.Parse(topoText[topoText.IndexOf('{')..]);
        string? masterPath = null;
        foreach (var dev in doc.RootElement.GetProperty("devices").EnumerateArray())
        {
            var nm = dev.GetProperty("name").GetString() ?? "";
            if (nm.Contains("EtherCAT", StringComparison.OrdinalIgnoreCase))
            {
                masterPath = dev.GetProperty("path").GetString();
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(masterPath))
            return; // No EtherCAT master — skip (do not mutate non-EtherCAT devices).

        // Distinctive name so we can locate and tear down exactly what we add.
        const string boxName = "ZZ_EteEdit_Scratch (EK1100)";
        string boxPath = $"{masterPath}^{boxName}";
        bool addSucceeded = false;
        bool boxVisible = false;

        // Issue #190: the add itself sits inside the try — an add that created the box and still
        // reported an error must not leave it saved in the per-run fixture.
        try
        {
            var add = await _client.CallToolAsync("twincat_add_ethercat_box",
                new Dictionary<string, object?>
                {
                    ["parentPath"] = masterPath,
                    ["vInfo"] = "EK1100",
                    ["name"] = boxName,
                });
            var addText = GetText(add);
            // ESI for EK1100 should always be present; if the platform genuinely
            // can't create it, surface the error rather than silently passing.
            Assert.False(addText.StartsWith("[Error]", StringComparison.Ordinal),
                $"add_ethercat_box failed: {addText}");
            addSucceeded = true;

            // Poll topology: in Run mode (e.g. after an activate_config on the
            // project's target) TwinCAT re-scans hardware after File.SaveAll and silently
            // removes the programmatically added ghost box. Poll to absorb the async
            // refresh and track whether the box ever became visible.
            string topo2 = string.Empty;
            for (int attempt = 0; attempt < 5 && !boxVisible; attempt++)
            {
                if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(3));
                topo2 = GetText(await _client.CallToolAsync("twincat_get_fieldbus_topology"));
                boxVisible = topo2.Contains(boxName, StringComparison.Ordinal);
            }

            // In Run mode the EtherCAT tree reflects live hardware: added ghost
            // boxes are removed by the hardware scan. The add tool correctly
            // reports success (CreateChild succeeded in-memory), but the topology
            // will never show the ghost box. Skip with a visible reason rather
            // than hard-failing — no false-green in live tests.
            if (!boxVisible)
            {
                Skip.If(true,
                    $"⏭ EtherCAT box '{boxName}' not visible in topology after add — "
                    + "likely Run mode (post activate_config): topology reflects live hardware. "
                    + "Skipping topology assertion; cleanup proceeds.");
            }

            Assert.Contains(boxName, topo2);

            // set_previous_port is best-effort here (an EK1100 directly under a
            // single-port master may have no selectable upstream port) — assert
            // only that it returns a structured, non-crashing result.
            var port = GetText(await _client.CallToolAsync("twincat_set_previous_port",
                new Dictionary<string, object?> { ["treePath"] = boxPath, ["port"] = "A" }));
            Assert.False(string.IsNullOrWhiteSpace(port));
        }
        finally
        {
            var rm = GetText(await _client.CallToolAsync("twincat_remove_ethercat_box",
                new Dictionary<string, object?> { ["treePath"] = boxPath }));
            // When the box was never visible, the hardware scan may have already
            // removed it from the tree; tolerate a "not found" error in that case.
            // After a failed add the remove is best-effort cleanup: asserting here would
            // replace the add's failure with the cleanup's.
            bool notFound = rm.Contains("Tree path not found", StringComparison.OrdinalIgnoreCase)
                         || rm.Contains("Parent tree path not found", StringComparison.OrdinalIgnoreCase);
            if (addSucceeded && (boxVisible || !notFound))
            {
                Assert.False(rm.StartsWith("[Error]", StringComparison.Ordinal),
                    $"remove_ethercat_box failed — fixture may be dirty: {rm}");
            }
        }

        var topo3 = GetText(await _client.CallToolAsync("twincat_get_fieldbus_topology"));
        Assert.DoesNotContain(boxName, topo3);
    }

    private static void CollectPaths(JsonElement nodes, List<string> paths)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                paths.Add(p.GetString()!);
            if (node.TryGetProperty("children", out var children))
                CollectPaths(children, paths);
        }
    }

    // Collect children of nodes whose name contains "(EK1122"
    private static void CollectEK1122Children(JsonElement nodes, List<JsonElement> result)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            bool isEk1122 = node.TryGetProperty("name", out var nameProp) &&
                            nameProp.GetString()?.Contains("(EK1122", StringComparison.OrdinalIgnoreCase) == true;

            if (node.TryGetProperty("children", out var children))
            {
                if (isEk1122)
                    foreach (var child in children.EnumerateArray())
                        result.Add(child);

                CollectEK1122Children(children, result);
            }
        }
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
