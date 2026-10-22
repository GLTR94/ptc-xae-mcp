using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.RegularExpressions;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// End-to-end coverage for feature/nc-axes against the live MCP server + XAE.
/// Serialized assembly-wide against the single live XAE.
///
/// <para><b>Safety.</b> The NC create/configure tools deliberately do NOT
/// save the project (there is no NC-delete tool to undo a persisted task).
/// This scenario creates a uniquely-named throwaway NC-Task + axis, exercises
/// the full ProduceXml→ConsumeXml round-trip in the in-memory XAE session,
/// asserts it via list/dump, then closes the project WITHOUT saving in
/// <c>finally</c> so the fixture copy on disk is never
/// modified.</para>
/// </summary>
[Collection("Stage Motion")]
[Trait("Stage", "Motion")]
public class NcAxesIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
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

    [Trait("Description", "Achse unter bestehender NC-Task anlegen, skalieren, listen/dumpen → Round-trip grün; create_nc_task = 4026-Guidance; Projekt NICHT gespeichert.")]
    [SkippableFact(Timeout = 600_000)]
    public async Task CreateAxis_Configure_RoundTrips_WithoutSaving()
    {
        SkipIfNotReady();
        await OpenFixtureAsync();

        string axisName = $"ZZ_Axis_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";

        try
        {
            // 0. create_nc_task on a project that already has one: TC3 4026.22
            //    raises "Item already exists!" — assert the rewritten guidance
            //    (the documented green-field-only contract).
            var ct = GetText(await _client!.CallToolAsync("twincat_create_nc_task",
                new Dictionary<string, object?> { ["name"] = "ZZ_NcTask" }));
            Assert.StartsWith("[Error]", ct);
            Assert.Contains("green-field", ct);

            // 1. Resolve the existing NC-Task by browsing (never hardcode the
            //    label). The first listed NC-Task is our target.
            var list0 = GetText(await _client.CallToolAsync("twincat_list_nc_tasks_axes"));
            Assert.Contains("NC-Task ", list0);
            string taskPath = FirstTaskPath(list0);

            // 2. Create an axis under that task (task resolved by browsing).
            var ca = GetText(await _client.CallToolAsync("twincat_create_axis",
                new Dictionary<string, object?> { ["ncTaskPath"] = taskPath, ["axisName"] = axisName }));
            Assert.DoesNotContain("[Error]", ca);
            Assert.Contains("Created axis at", ca);
            string axisPath = ExtractPath(ca, "Created axis at ");

            // 3. Configure known parameters via ProduceXml→ConsumeXml.
            var cfg = GetText(await _client.CallToolAsync("twincat_configure_axis",
                new Dictionary<string, object?>
                {
                    ["axisPath"] = axisPath,
                    ["scaleFactorNumerator"] = "0.0002",
                    ["velocityMax"] = "1234",
                }));
            Assert.DoesNotContain("[Error]", cfg);
            Assert.Contains("scaleFactorNumerator=0.0002", cfg);

            // 4. list surfaces the new axis by its tree path.
            var list = GetText(await _client.CallToolAsync("twincat_list_nc_tasks_axes"));
            Assert.Contains(axisPath, list);

            // 5. dump proves the ConsumeXml actually took (value is in the axis
            //    ProduceXml under the captured NcAxisDef path).
            var dump = GetText(await _client.CallToolAsync("twincat_dump_nc_tree"));
            Assert.Contains("<ScaleFactorNumerator>0.0002</ScaleFactorNumerator>", dump);
            Assert.Contains("<Maximum>1234</Maximum>", dump);
        }
        finally
        {
            // Discard everything — never persist into the shared fixture.
            try
            {
                await _client!.CallToolAsync("twincat_close_project",
                    new Dictionary<string, object?> { ["saveFirst"] = false });
            }
            catch { /* best-effort */ }
        }
    }

    private async Task OpenFixtureAsync()
    {
        var open = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        Assert.False(open.IsError == true, GetText(open));
    }

    // First "  NC-Task  TINC^…" line from list_nc_tasks_axes output.
    private static string FirstTaskPath(string listText)
    {
        // Lines look like "  NC-Task  TINC^NC-Task 1 SAF" and may carry a
        // trailing \r (MCP joins blocks with \n), so don't anchor on $.
        var m = Regex.Match(listText, @"\bNC-Task\s+(TINC\^[^\r\n]+)");
        Assert.True(m.Success, $"no NC-Task line in: {listText}");
        return m.Groups[1].Value.Trim();
    }

    // Pulls the trailing tree path out of a "… at TINC^…." success line.
    private static string ExtractPath(string text, string marker)
    {
        int i = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(i >= 0, $"marker '{marker}' not in: {text}");
        string rest = text[(i + marker.Length)..].Trim();
        var m = Regex.Match(rest, @"(TINC\^[^\r\n]+)");
        Assert.True(m.Success, $"no TINC path after '{marker}' in: {text}");
        // NC node names carry no '.', so a trailing sentence period is safe to strip.
        return m.Groups[1].Value.Trim().TrimEnd('.');
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
