using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Stage HMI fixture (hmi-server). Copies an HMI project
/// root into a fresh folder below %TEMP% — never the original, never a customer HMI —
/// opens it in the XAE through the MCP host and waits until the HMI project reports
/// IsReady.
/// <para>
/// The source is chosen by <see cref="HmiFixtureSource"/> (#163): <see cref="SourceEnvVar"/>
/// when set, otherwise — under <c>PTC_XAE_MCP_REQUIRE_LIVE=1</c> — the committed neutral
/// fixture <c>tests/Fixtures/PtcXaeMcpHmiFixture</c> (created by
/// <c>tests/Fixtures/create_hmi_fixture.py</c>). The stage stays opt-in, because opening the
/// fixture CLOSES the solution open in the XAE without saving: a plain <c>dotnet test</c>
/// skips it untouched, and it runs last (<see cref="StageCollectionOrderer"/>).
/// </para>
/// <para>
/// The copy leaves out bin/obj/Packages (≈600 MB of restored NuGet packages); the
/// XAE restores them from the local Beckhoff feed on open — verified on 2026-09-25.
/// </para>
/// </summary>
public sealed class HmiFixture : IAsyncLifetime
{
    public const string SourceEnvVar = HmiFixtureSource.SourceEnvVar;
    /// <summary>Set to 1 when the host runs with PTC_XAE_MCP_HMI_AUTOCONFIRM_TEMP=1 — the
    /// write tests then expect success instead of an expired popup.</summary>
    public const string ExpectAutoConfirmEnvVar = "PTC_XAE_MCP_HMI_EXPECT_AUTOCONFIRM";

    private const string HmiProjectTypeGuid = "{FE7A1B72-C5B7-4D7C-BB7D-76384D4DE8E1}";
    private static readonly string[] SkippedDirectories = ["bin", "obj", "Packages", ".engineering_servers", ".vs"];

    public McpClient? Client { get; private set; }
    public bool Ready { get; private set; }
    public string NotReadyReason { get; private set; } = "";

    /// <summary>
    /// True when the stage is not ready because no fixture SOURCE is available (not opted in, or
    /// the named / committed folder is missing). That is a configuration fact, not a live-run
    /// failure, so <see cref="Guard"/> skips on it even under <c>PTC_XAE_MCP_REQUIRE_LIVE=1</c> —
    /// the suite must never fall back to an HMI project on someone's disk (#146).
    /// </summary>
    public bool SourceMissing { get; private set; }
    public string ProjectName { get; private set; } = "";
    public string ProjectDirectory { get; private set; } = "";
    public string SlnPath { get; private set; } = "";

    public static bool ExpectAutoConfirm =>
        Environment.GetEnvironmentVariable(ExpectAutoConfirmEnvVar) == "1";

    public async Task InitializeAsync()
    {
        HmiFixtureSource.Resolution source = HmiFixtureSource.Resolve(
            Environment.GetEnvironmentVariable(SourceEnvVar), LiveXae.RequireLive, AppContext.BaseDirectory);
        if (source.Directory is null)
        {
            SourceMissing = true;
            NotReadyReason = source.Reason;
            return;
        }

        string sourceRoot = source.Directory;
        if (!Directory.Exists(sourceRoot))
        {
            SourceMissing = true;
            NotReadyReason = source.Origin == HmiFixtureSource.Origin.Environment
                ? $"HMI fixture source not found: '{sourceRoot}' ({SourceEnvVar})."
                : $"HMI fixture source not found: '{sourceRoot}'.";
            return;
        }

        Client = await TryConnectAsync();
        if (Client is null)
        {
            NotReadyReason = $"MCP host not reachable on {LiveXae.ServerUrl}.";
            return;
        }

        string runToken;
        try
        {
            (SlnPath, ProjectName, runToken) = CreateTempCopy(sourceRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            NotReadyReason = $"Could not create the TEMP fixture: {ex.Message}";
            return;
        }

        string open = await CallAsync("twincat_open_project",
            new() { ["slnPath"] = SlnPath, ["closeCurrentFirst"] = true }, TimeSpan.FromMinutes(4));

        // A cold XAE start can report "[Error] … DTE not reachable" although the solution
        // opens (memory: xae-mcp-new-tool-needs-client-reconnect) — verify, don't trust.
        bool opened = await WaitForAsync(async () =>
        {
            string active = await CallAsync("twincat_get_active_project");
            return active.Contains(runToken, StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromMinutes(3));
        if (!opened)
        {
            NotReadyReason = $"Fixture solution did not open ({SlnPath}): {open}";
            return;
        }

        // First open restores NuGet packages; the TE2000 license window may also block here.
        string last = "";
        bool ready = await WaitForAsync(async () =>
        {
            last = await CallAsync("twincat_hmi_list_projects");
            JsonElement? json = JsonAfterSummary(last);
            if (json is null) return false;
            foreach (JsonElement p in json.Value.GetProperty("projects").EnumerateArray())
            {
                if (p.GetProperty("name").GetString() != ProjectName) continue;
                ProjectDirectory = p.GetProperty("projectDirectory").GetString() ?? "";
                return p.GetProperty("isReady").GetBoolean();
            }
            return false;
        }, TimeSpan.FromMinutes(4));
        if (!ready)
        {
            NotReadyReason = $"HMI project '{ProjectName}' did not become ready: {last}";
            return;
        }

        Ready = true;
    }

    public async Task DisposeAsync()
    {
        if (Client is not null) await Client.DisposeAsync();
    }

    /// <summary>
    /// Skip (normal) or fail (live required) with the precise reason. A missing fixture
    /// SOURCE always skips: without it there is nothing to test against, and the honest
    /// answer is "not configured", not "the live run failed".
    /// </summary>
    public void Guard()
    {
        if (Ready) return;
        if (LiveXae.RequireLive && !SourceMissing)
            throw new Xunit.Sdk.XunitException("LIVE RUN REQUIRED — refusing to skip. " + NotReadyReason);
        throw new SkipException(NotReadyReason);
    }

    public async Task<string> CallAsync(string tool, Dictionary<string, object?>? args = null, TimeSpan? timeout = null)
    {
        try
        {
            var result = await Client!.CallToolAsync(tool, args,
                cancellationToken: new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2)).Token);
            return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        }
        catch (Exception ex) when (ex is OperationCanceledException or McpException or HttpRequestException)
        {
            return $"[Error] {tool}: {ex.Message}";
        }
    }

    /// <summary>The tools answer "summary line\n{json}"; returns the JSON part or null.</summary>
    public static JsonElement? JsonAfterSummary(string text)
    {
        if (text.StartsWith("[Error]", StringComparison.Ordinal)) return null;
        int start = text.IndexOf('{');
        if (start < 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[start..]);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(2000);
        }
        return await condition();
    }

    private static async Task<McpClient?> TryConnectAsync()
    {
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(LiveXae.ServerUrl),
                ConnectionTimeout = TimeSpan.FromSeconds(5),
            });
            return await McpClient.CreateAsync(transport,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        }
        catch
        {
            return null;
        }
    }

    // ---- TEMP copy -------------------------------------------------------------

    private static (string Sln, string ProjectName, string RunToken) CreateTempCopy(string sourceRoot)
    {
        string hmiproj = EnumerateFiles(sourceRoot, "*.hmiproj").FirstOrDefault()
            ?? throw new InvalidOperationException($"No .hmiproj below {sourceRoot}.");
        string projectName = Path.GetFileNameWithoutExtension(hmiproj);

        string baseDir = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp-hmi-fixture");
        DeleteOldRuns(baseDir);
        string runToken = "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string runDir = Path.Combine(baseDir, runToken);
        CopyTree(sourceRoot, runDir);

        // Reuse a solution that already references the project; otherwise generate one.
        string projectFile = Path.GetFileName(hmiproj);
        foreach (string sln in EnumerateFiles(runDir, "*.sln"))
            if (File.ReadAllText(sln).Contains(projectFile, StringComparison.OrdinalIgnoreCase))
                return (sln, projectName, runToken);

        string relativeProject = Path.GetRelativePath(sourceRoot, hmiproj);
        Match guid = Regex.Match(File.ReadAllText(hmiproj), "<ProjectGuid>\\{?([0-9a-fA-F-]{36})\\}?</ProjectGuid>");
        string projectGuid = "{" + (guid.Success ? guid.Groups[1].Value : Guid.NewGuid().ToString()).ToUpperInvariant() + "}";
        string generated = Path.Combine(runDir, "HmiFixture.sln");
        File.WriteAllText(generated, SolutionText(projectName, relativeProject, projectGuid));
        return (generated, projectName, runToken);
    }

    private static string SolutionText(string name, string relativePath, string projectGuid) =>
        "\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n" +
        "# TcXaeShell Solution File, Format Version 11.00\r\n" +
        "VisualStudioVersion = 17.10.35827.194\r\n" +
        "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
        $"Project(\"{HmiProjectTypeGuid}\") = \"{name}\", \"{relativePath}\", \"{projectGuid}\"\r\n" +
        "EndProject\r\n" +
        "Global\r\n" +
        "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
        "\t\tDebug|TwinCAT HMI = Debug|TwinCAT HMI\r\n" +
        "\t\tRelease|TwinCAT HMI = Release|TwinCAT HMI\r\n" +
        "\tEndGlobalSection\r\n" +
        "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
        $"\t\t{projectGuid}.Debug|TwinCAT HMI.ActiveCfg = Debug|TwinCAT HMI\r\n" +
        $"\t\t{projectGuid}.Debug|TwinCAT HMI.Build.0 = Debug|TwinCAT HMI\r\n" +
        $"\t\t{projectGuid}.Release|TwinCAT HMI.ActiveCfg = Release|TwinCAT HMI\r\n" +
        $"\t\t{projectGuid}.Release|TwinCAT HMI.Build.0 = Release|TwinCAT HMI\r\n" +
        "\tEndGlobalSection\r\n" +
        "EndGlobal\r\n";

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(root, pattern))
            yield return file;
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase)) continue;
            foreach (string file in EnumerateFiles(dir, pattern))
                yield return file;
        }
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(dir);
            if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            CopyTree(dir, Path.Combine(target, name));
        }
    }

    // Best effort: a run folder the XAE still has open is simply left for next time.
    private static void DeleteOldRuns(string baseDir)
    {
        if (!Directory.Exists(baseDir)) return;
        foreach (string dir in Directory.EnumerateDirectories(baseDir, "run-*"))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddHours(-12))
                    Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // locked by a running XAE — keep it
            }
        }
    }
}
