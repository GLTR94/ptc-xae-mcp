using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Proves that real live-suite traffic actually lands in the per-run JSON
/// activity log. The host (the running exe) writes the log to
/// <c>%LOCALAPPDATA%\ptc-xae-mcp\activity-*.log</c>; this test process reads
/// that same file from the same machine after driving a real
/// <c>twincat_open_project</c> against the live XAE.
/// <para>
/// Skips unless PTC_XAE_MCP_FIXTURE_SLN points to a real .sln AND the MCP
/// server is reachable (hard-fails instead when REQUIRE_LIVE=1).
/// </para>
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class McpActivityLogIntegrationTests : IAsyncLifetime
{
    private const string ServerUrl = "http://localhost:5010/";
    private McpClient? _client;
    private bool _ready;
    private string _fixtureSln = string.Empty;

    private static string LogDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ptc-xae-mcp");

    public async Task InitializeAsync()
    {
        var fixture = LiveXae.FixtureSln();
        if (fixture is null)
        {
            LiveXae.Announce(false, string.Empty);
            return;
        }

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
        catch
        {
            _ready = false;
        }

        LiveXae.Announce(_ready, _fixtureSln);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }

    private void SkipIfNotReady() => LiveXae.GuardReady(_ready);

    [Trait("Description", "Echten twincat_open_project-Aufruf durchführen → Eintrag muss im per-Run-JSON-Log auftauchen.")]
    [SkippableFact(Timeout = 120_000)]
    public async Task LiveOpenProject_IsCapturedInThePerRunJsonLog()
    {
        SkipIfNotReady();

        // Drive a real tool call against the live XAE — this same HTTP path is
        // exactly what the live integration suite uses, so proving it
        // here proves the suite's traffic is logged too.
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var result = await _client!.CallToolAsync("twincat_open_project",
            new Dictionary<string, object?>
            {
                ["slnPath"] = _fixtureSln,
                ["closeCurrentFirst"] = true,
            });
        var text = GetText(result);
        Assert.False(result.IsError == true, $"open_project error: {text}");

        // The host appends asynchronously-ish; give the file a brief moment.
        JsonElement? hit = null;
        for (int attempt = 0; attempt < 20 && hit is null; attempt++)
        {
            hit = FindOpenProjectEntry(before);
            if (hit is null) await Task.Delay(100);
        }

        Assert.True(hit is not null,
            $"No twincat_open_project entry found in any {LogDir}\\activity-*.log " +
            "written during this run — live traffic is NOT being logged.");

        var entry = hit.Value;
        Assert.Equal("twincat_open_project", entry.GetProperty("tool").GetString());
        Assert.True(entry.GetProperty("ok").GetBoolean(),
            "open_project against the fixture should have succeeded");

        // "args" is itself a JSON document (the wrapper serialises the tool's
        // argument object). Parse it and pull slnPath, then compare on the
        // full path case-insensitively — Windows paths and the user-picked
        // c:\ vs C:\ casing must not make a real capture look like a miss.
        var argsJson = entry.GetProperty("args").GetString() ?? "{}";
        using var argsDoc = JsonDocument.Parse(argsJson);
        var loggedSln = argsDoc.RootElement.GetProperty("slnPath").GetString() ?? "";
        Assert.Equal(
            Path.GetFullPath(_fixtureSln),
            Path.GetFullPath(loggedSln),
            ignoreCase: true);
    }

    /// <summary>
    /// Scans every per-run log file touched since <paramref name="since"/>
    /// for the most recent twincat_open_project line.
    /// </summary>
    private static JsonElement? FindOpenProjectEntry(DateTimeOffset since)
    {
        if (!Directory.Exists(LogDir))
            return null;

        JsonElement? best = null;
        foreach (var file in Directory.EnumerateFiles(LogDir, "activity-*.log"))
        {
            if (File.GetLastWriteTimeUtc(file) < since.UtcDateTime)
                continue;

            string[] lines;
            try { lines = File.ReadAllLines(file); }
            catch { continue; } // host may be mid-append; retried by caller

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonElement el;
                try { el = JsonSerializer.Deserialize<JsonElement>(line); }
                catch { continue; }

                if (el.TryGetProperty("tool", out var t) &&
                    t.GetString() == "twincat_open_project")
                {
                    best = el; // keep scanning → last match wins
                }
            }
        }
        return best;
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0)
            return string.Empty;
        return string.Join("\n", content
            .OfType<TextContentBlock>()
            .Select(c => c.Text));
    }
}
