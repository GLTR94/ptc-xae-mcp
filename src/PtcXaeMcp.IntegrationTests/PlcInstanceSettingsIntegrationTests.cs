using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// plc-instance-settings end to end on the fixture copy:
/// build → init symbols read → one written, refusals, restored → retain area lists the RETAIN variable →
/// .tpy generation on → build writes the .tpy → flags restored. Every setting it changes is put back.
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public sealed class PlcInstanceSettingsIntegrationTests : IAsyncLifetime
{
    private McpClient? _client;
    private bool _ready;
    private string _fixtureSln = string.Empty;

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is not null)
        {
            _fixtureSln = fixture;
            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri("http://localhost:5010/"),
                    ConnectionTimeout = TimeSpan.FromSeconds(5),
                });
                _client = await McpClient.CreateAsync(transport,
                    cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
                // Start from the fixture (an earlier class may have closed it or opened a scratch solution).
                await _client.CallToolAsync("twincat_open_project",
                    new Dictionary<string, object?> { ["slnPath"] = _fixtureSln, ["closeCurrentFirst"] = true });
                _ready = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _ready = false;
            }
        }
        LiveXae.Announce(_ready, _fixtureSln);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    [Trait("Description", "Fixture bauen, TcInitSymbol-Werte lesen, einen setzen (Read-back) und zurücksetzen; ungültige Werte/Symbole abgelehnt; Retain-Bereich enthält die RETAIN-Variable; TPY-Erzeugung ein → Build schreibt .tpy → Flags zurück.")]
    [SkippableFact(Timeout = 900_000)]
    public async Task InstanceSettings_InitSymbolsRetainAndTypeFiles()
    {
        LiveXae.GuardReady(_ready);
        var plc = new Dictionary<string, object?> { ["plcProjectName"] = FixtureLayout.PlcName };

        // 1. Build: the instance carries init values and data areas only after a build.
        string text = await Call("twincat_build_plc", new(plc));
        Assert.Contains("Build succeeded", text, StringComparison.OrdinalIgnoreCase);

        // 2. Init symbols.
        text = await Call("twincat_read_init_symbols", new(plc));
        Assert.Contains($"{FixtureLayout.InitSymbolDint} : DINT = {FixtureLayout.InitSymbolDintValue}", text);
        Assert.Contains($"{FixtureLayout.InitSymbolLreal} : LREAL", text);

        try
        {
            text = await Call("twincat_write_init_symbol", new(plc) { ["symbol"] = FixtureLayout.InitSymbolDint, ["value"] = "42" });
            Assert.Contains("read-back: 42", text);
            text = await Call("twincat_read_init_symbols", new(plc));
            Assert.Contains($"{FixtureLayout.InitSymbolDint} : DINT = 42", text);

            // Live: the XAE ignores an unparseable value and wraps an out-of-range one — both must be refused.
            text = await Call("twincat_write_init_symbol", new(plc) { ["symbol"] = FixtureLayout.InitSymbolDint, ["value"] = "abc" }, expectError: true);
            Assert.Contains("did not store", text);
            text = await Call("twincat_write_init_symbol", new(plc) { ["symbol"] = FixtureLayout.InitSymbolDint, ["value"] = "99999999999" }, expectError: true);
            Assert.Contains("restored the old value '42'", text);
            text = await Call("twincat_write_init_symbol", new(plc) { ["symbol"] = FixtureLayout.UintSymbol, ["value"] = "7" }, expectError: true);
            Assert.Contains("is not an init symbol", text);
            text = await Call("twincat_read_init_symbols", new(plc));
            Assert.Contains($"{FixtureLayout.InitSymbolDint} : DINT = 42", text);
        }
        finally
        {
            await Call("twincat_write_init_symbol",
                new(plc) { ["symbol"] = FixtureLayout.InitSymbolDint, ["value"] = FixtureLayout.InitSymbolDintValue }, expectError: null);
        }

        // 3. Retain.
        text = await Call("twincat_read_retain_config", new(plc));
        Assert.Contains($"{FixtureLayout.RetainSymbol} : UDINT", text);

        // 4. .tmc/.tpy generation.
        text = await Call("twincat_get_type_file_generation", new(plc));
        bool tmcBefore = text.Contains("TmcFileCopy=true", StringComparison.Ordinal);
        bool tpyBefore = text.Contains("TpyFileCopy=true", StringComparison.Ordinal);
        string plcDir = Path.Combine(Path.GetDirectoryName(_fixtureSln)!, FixtureLayout.SolutionName, FixtureLayout.PlcName);
        string tpy = Path.Combine(plcDir, FixtureLayout.PlcName + ".tpy");
        try
        {
            text = await Call("twincat_set_type_file_generation", new(plc) { ["tmc"] = false, ["tpy"] = true }, expectError: true);
            Assert.Contains("not possible", text);

            text = await Call("twincat_set_type_file_generation", new(plc) { ["tpy"] = true });
            Assert.Contains("read-back: TmcFileCopy=true, TpyFileCopy=true", text);
            DateTime before = DateTime.UtcNow.AddSeconds(-2);
            text = await Call("twincat_rebuild_plc", new(plc));
            Assert.DoesNotContain("[Error]", text);
            Assert.True(File.Exists(tpy), $"{tpy} not written by the build with TpyFileCopy=true");
            Assert.True(File.GetLastWriteTimeUtc(tpy) >= before, $"{tpy} is older than the build");

            text = await Call("twincat_set_type_file_generation", new(plc) { ["tmc"] = false });
            Assert.Contains("read-back: TmcFileCopy=false, TpyFileCopy=false", text);
        }
        finally
        {
            await Call("twincat_set_type_file_generation", new(plc) { ["tmc"] = tmcBefore, ["tpy"] = tpyBefore }, expectError: null);
        }
    }

    /// <param name="expectError">true: must be an error; false: must not; null: either (cleanup).</param>
    private async Task<string> Call(string tool, Dictionary<string, object?> args, bool? expectError = false)
    {
        var result = await _client!.CallToolAsync(tool, args);
        string text = string.Join("\n", (result.Content ?? []).OfType<TextContentBlock>().Select(c => c.Text));
        Console.WriteLine($"[instance-settings] {tool}: {text}");
        if (expectError == true)
            Assert.StartsWith("[Error]", text);
        else if (expectError == false)
            Assert.False(text.StartsWith("[Error]", StringComparison.Ordinal), $"{tool} failed: {text}");
        return text;
    }
}
