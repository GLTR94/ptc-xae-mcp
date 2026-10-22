using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Shared readiness gate for the integration suite. Every test in this
/// project runs against a <b>real, running TwinCAT XAE</b> via the live
/// MCP server on http://localhost:5010 — there are no fakes or shadows here.
///
/// Behaviour:
/// <list type="bullet">
/// <item>Normal run: if the fixture .sln or the MCP server is missing, the
/// test is <b>skipped</b> (so a plain `dotnet test` of the whole solution
/// stays green offline).</item>
/// <item><c>PTC_XAE_MCP_REQUIRE_LIVE=1</c> (set for a live run,
/// see CONTRIBUTING.md): the suite <b>brings the live environment up itself</b> via
/// <see cref="EnsureLiveAsync"/> — it starts the MCP host (which hosts XAE)
/// if port 5010 is dead and opens the neutral fixture (a per-run %TEMP% copy of
/// <c>tests/Fixtures/PtcXaeMcpFixture</c>, see <see cref="FixtureLayout"/>) if a
/// different or no project is loaded — then runs. A genuine failure to bootstrap is a
/// <b>hard failure</b>, never a silent skip.</item>
/// </list>
/// <para><b>Runtime target (issue #195).</b> Everything that activates a configuration or reads a
/// runtime (Stage XAR, see <see cref="XarRuntimeFixture"/>) uses an <b>explicit</b> runtime, taken from:</para>
/// <list type="bullet">
/// <item><c>PTC_XAE_MCP_RUNTIME_NETID</c> (<see cref="RuntimeNetIdEnvVar"/>): the AMS NetId of the runtime,
/// e.g. the AMS NetId of a user-mode runtime. <b>Required</b> for the runtime stage. The per-run
/// fixture copy is pointed at it with <c>twincat_set_target</c> and activated there (unless the runtime
/// already runs the fixture PLC, see <see cref="XarRuntimeFixture"/>), and every ADS call passes it as
/// <c>amsNetId</c>. Without it the runtime stage skips with that reason (a hard failure under
/// <c>PTC_XAE_MCP_REQUIRE_LIVE=1</c>). It never activates the local kernel implicitly: the neutral fixture
/// has no target NetId, so an activation without one would hit the local runtime.</item>
/// <item><c>PTC_XAE_MCP_RUNTIME_PLATFORM</c> (<see cref="RuntimePlatformEnvVar"/>, optional): the target
/// platform passed to <c>twincat_set_target</c> together with the NetId, e.g. <c>TwinCAT OS (x64)</c>.
/// It is passed only when it is set.</item>
/// <item><c>PTC_XAE_MCP_RUNTIME_UMR_INSTANCE</c> (<see cref="RuntimeUmrInstanceEnvVar"/>, optional): a
/// registered user-mode runtime instance, e.g. <c>UmRT_Default</c>. Only when it is set <b>and</b> the NetId
/// answers no ADS state read does the stage call <c>twincat_umr_start {instanceName}</c> first. The suite
/// never starts a UMR without an instance name, and never while the NetId answers: that could start a
/// second UMR next to a running one.</item>
/// </list>
/// </summary>
public static class LiveXae
{
    public const string ServerUrl = "http://localhost:5010/";
    public const string FixtureEnvVar = "PTC_XAE_MCP_FIXTURE_SLN";
    public const string RequireLiveEnvVar = "PTC_XAE_MCP_REQUIRE_LIVE";
    public const string HostExeEnvVar = "PTC_XAE_MCP_HOST_EXE";

    /// <summary>AMS NetId of the runtime the runtime stage activates and reads (issue #195). Required for Stage XAR.</summary>
    public const string RuntimeNetIdEnvVar = "PTC_XAE_MCP_RUNTIME_NETID";

    /// <summary>Optional target platform for <c>twincat_set_target</c>; passed only when set.</summary>
    public const string RuntimePlatformEnvVar = "PTC_XAE_MCP_RUNTIME_PLATFORM";

    /// <summary>Optional registered UMR instance, started only when the runtime NetId answers no ADS state read.</summary>
    public const string RuntimeUmrInstanceEnvVar = "PTC_XAE_MCP_RUNTIME_UMR_INSTANCE";

    /// <summary>The runtime's AMS NetId (<see cref="RuntimeNetIdEnvVar"/>), or null when it is not set.</summary>
    public static string? RuntimeNetId => EnvOrNull(RuntimeNetIdEnvVar);

    /// <summary>The runtime's target platform (<see cref="RuntimePlatformEnvVar"/>), or null when it is not set.</summary>
    public static string? RuntimePlatform => EnvOrNull(RuntimePlatformEnvVar);

    /// <summary>The UMR instance the suite may start (<see cref="RuntimeUmrInstanceEnvVar"/>), or null.</summary>
    public static string? RuntimeUmrInstance => EnvOrNull(RuntimeUmrInstanceEnvVar);

    /// <summary>Why the runtime stage cannot run without <see cref="RuntimeNetIdEnvVar"/>.</summary>
    public static string MissingRuntimeTargetReason =>
        $"{RuntimeNetIdEnvVar} is not set. The runtime stage needs an explicit runtime target (the AMS NetId of the " +
        $"runtime to activate and read, e.g. a user-mode runtime) and never activates the local kernel implicitly. " +
        $"Set {RuntimeNetIdEnvVar} (optionally {RuntimePlatformEnvVar} and {RuntimeUmrInstanceEnvVar}) to run it.";

    private static string? EnvOrNull(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private const int ServerPort = 5010;

    /// <summary>
    /// Host exe locations relative to the test assembly (…/src/PtcXaeMcp.IntegrationTests/
    /// bin/&lt;cfg&gt;/&lt;tfm&gt;/ → …/src/PtcXaeMcp.WinForms/bin/). Newest build wins;
    /// <see cref="HostExeEnvVar"/> overrides. No absolute developer paths.
    /// </summary>
    private static IEnumerable<string> DefaultHostExes()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string bin = Path.Combine(dir.FullName, "PtcXaeMcp.WinForms", "bin");
            if (!Directory.Exists(bin)) continue;
            return Directory.EnumerateFiles(bin, "ptc-xae-mcp.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        return [];
    }

    private static int _bannerPrinted;
    private static readonly SemaphoreSlim BootstrapGate = new(1, 1);
    private static bool _bootstrapDone;

    /// <summary>True when the caller demanded a real live run (no skipping).</summary>
    public static bool RequireLive =>
        Environment.GetEnvironmentVariable(RequireLiveEnvVar) == "1";

    /// <summary>
    /// Prints a one-time, unmissable banner so it is 100% clear from the test
    /// output whether the suite is exercising real TwinCAT or being skipped.
    /// </summary>
    public static void Announce(bool ready, string fixtureSln)
    {
        if (Interlocked.Exchange(ref _bannerPrinted, 1) != 0)
            return;

        Console.WriteLine("================================================================");
        if (ready)
        {
            Console.WriteLine("  ⚡ LIVE XAE  —  tests run against REAL running TwinCAT");
            Console.WriteLine($"     Solution : {fixtureSln}");
            Console.WriteLine($"     MCP      : {ServerUrl}");
        }
        else if (RequireLive)
        {
            Console.WriteLine("  ✖ LIVE XAE REQUIRED but not ready — suite will FAIL.");
        }
        else
        {
            Console.WriteLine("  ⏭ LIVE XAE not ready — integration tests will SKIP.");
            Console.WriteLine("     (no fixture .sln / MCP server). NOT a real test run.");
        }
        Console.WriteLine("================================================================");
    }

    /// <summary>
    /// Resolves the fixture .sln. Prefers <see cref="FixtureEnvVar"/> when it
    /// points at an existing file (used as-is — pass a disposable copy). In live
    /// mode (<see cref="RequireLive"/>) it otherwise falls back to a per-run %TEMP%
    /// copy of the repo's neutral fixture (<see cref="FixtureLayout.PrepareTempCopy"/>),
    /// so a live-suite run needs no machine-specific setup and never mutates
    /// the committed sources. In normal mode an unset env var stays null, preserving
    /// the offline skip behaviour (a plain <c>dotnet test</c> never opens a project).
    /// </summary>
    public static string? FixtureSln()
    {
        var fixture = Environment.GetEnvironmentVariable(FixtureEnvVar);
        if (!string.IsNullOrWhiteSpace(fixture) && File.Exists(fixture))
            return fixture;

        return RequireLive ? FixtureLayout.PrepareTempCopy() : null;
    }

    /// <summary>
    /// Brings the whole live environment up if it isn't already — run once for
    /// the entire assembly, idempotent, and safe to call from every stage
    /// fixture. In live mode (<see cref="RequireLive"/>):
    /// <list type="number">
    /// <item>Starts the MCP host (which launches XAE) when port 5010 is dead,
    /// then waits up to 120 s for it to listen.</item>
    /// <item>Opens the fixture solution when a <i>different</i> project — or no
    /// project — is currently loaded. An already-open fixture is reused, never
    /// needlessly closed and reopened.</item>
    /// </list>
    /// In non-live mode it only discovers the PLC root of an already-open fixture
    /// (<see cref="FixtureLayout.DiscoverPlcRootAsync"/>), so an offline <c>dotnet test</c> keeps
    /// its skip behaviour and never spawns a GUI host.
    /// </summary>
    public static async Task EnsureLiveAsync(CancellationToken ct = default)
    {
        await BootstrapGate.WaitAsync(ct);
        try
        {
            if (_bootstrapDone)
                return;

            if (!RequireLive)
            {
                // Opt-in run against an already-open fixture (env var set): only learn
                // the localized PLC root; never start a host or open a project here.
                _bootstrapDone = true;
                await using var probe = await TryConnectFixtureClientAsync(ct);
                if (probe is not null)
                    await FixtureLayout.DiscoverPlcRootAsync(probe);
                return;
            }

            // A previous run's TEMP copy may still be open in the XAE. FixtureSln() deletes old run folders, and
            // closing a solution whose files are gone raises "Schließen des Projekts fehlgeschlagen! … .tmcRefac"
            // (seen live 2026-10-10) and blocks every call — so close it first, while its files still exist.
            if (FixtureLayout.FindRepoFixtureSln() is not null)
            {
                await EnsureServerAsync(ct);
                await CloseStaleFixtureRunAsync(ct);
            }

            var sln = FixtureSln();
            if (sln is null)
            {
                // Nothing to load. GuardReady/SkipIfNotReady will fail the run
                // with the canonical "fixture not found" message.
                _bootstrapDone = true;
                return;
            }

            await EnsureServerAsync(ct);
            await EnsureProjectOpenAsync(sln, ct);
            await using (var client = await ConnectAsync())
                if (client is not null)
                    await FixtureLayout.DiscoverPlcRootAsync(client);
            _bootstrapDone = true;
        }
        finally
        {
            BootstrapGate.Release();
        }
    }

    /// <summary>
    /// Throws when the live environment is not ready. A skip in normal mode,
    /// a hard <see cref="Xunit.Sdk.XunitException"/> when live is required.
    /// </summary>
    public static void GuardReady(bool ready)
    {
        if (ready)
            return;

        var reason =
            $"Live XAE not ready: run with {RequireLiveEnvVar}=1 (opens a TEMP copy of " +
            $"{FixtureLayout.RepoRelativeDirectory}) or set {FixtureEnvVar} to a disposable .sln, " +
            $"and start the MCP server on {ServerUrl}.";

        if (RequireLive)
            throw new Xunit.Sdk.XunitException(
                "LIVE RUN REQUIRED — refusing to skip. " + reason);

        throw new SkipException(reason);
    }

    // ── live bootstrap helpers ──────────────────────────────────────────────

    private static readonly string[] StaleRunMarkers =
        [@"\ptc-xae-mcp-fixture-runs\", @"\ptc-xae-mcp-hmi-fixture\"];

    /// <summary>Closes (without saving) the solution the XAE has open when it is a test-run TEMP copy.</summary>
    private static async Task CloseStaleFixtureRunAsync(CancellationToken ct)
    {
        await using var client = await ConnectAsync();
        if (client is null) return;
        string active = GetText(await client.CallToolAsync("twincat_get_active_project", cancellationToken: ct));
        string? path = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(active);
            if (doc.RootElement.TryGetProperty("solutionPath", out var p)) path = p.GetString();
        }
        catch (System.Text.Json.JsonException) { return; }
        if (path is null || !StaleRunMarkers.Any(m => path.Contains(m, StringComparison.OrdinalIgnoreCase))) return;

        Console.WriteLine($"  ▶ Live bootstrap: closing the previous test run's copy before it is cleaned up — {path}");
        await client.CallToolAsync("twincat_close_project",
            new Dictionary<string, object?> { ["saveFirst"] = false }, cancellationToken: ct);
    }

    private static async Task EnsureServerAsync(CancellationToken ct)
    {
        if (await IsServerListeningAsync(ct))
            return;

        var exe = DefaultHostExes().Prepend(Environment.GetEnvironmentVariable(HostExeEnvVar) ?? "")
                                 .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
        if (exe is null)
            throw new Xunit.Sdk.XunitException(
                "LIVE RUN REQUIRED — MCP host is not running and its exe was not found. " +
                $"Build the WinForms host or set {HostExeEnvVar} to ptc-xae-mcp.exe.");

        // --diagnostics: the live suite calls diagnostics-only tools (twincat_show_step,
        // ptc_test_marker, …) that a released host does not register by default (issue #157
        // item 1). UseShellExecute=true does not read ArgumentList, so build the command line
        // string explicitly.
        Console.WriteLine($"  ▶ Live bootstrap: MCP host not running — starting {exe} --diagnostics");
        Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "--diagnostics", UseShellExecute = true });

        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (await IsServerListeningAsync(ct))
            {
                Console.WriteLine($"  ✓ Live bootstrap: MCP host listening on port {ServerPort}.");
                return;
            }
            await Task.Delay(1000, ct);
        }

        throw new Xunit.Sdk.XunitException(
            $"LIVE RUN REQUIRED — MCP host did not start listening on port {ServerPort} within 120 s.");
    }

    private static async Task EnsureProjectOpenAsync(string sln, CancellationToken ct)
    {
        await using var client = await ConnectAsync();
        if (client is null)
            throw new Xunit.Sdk.XunitException(
                "LIVE RUN REQUIRED — MCP server is up but the client could not connect.");

        // XAE can be briefly busy (RPC_E_SERVERCALL_RETRYLATER) after activate_config or
        // a TwinCAT restart — retry the whole check-then-open sequence with back-off.
        const int maxAttempts = 12;
        const int retryDelayMs = 10_000; // 10 s between retries → up to 120 s total

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Probe: is the fixture already open?  null = RETRYLATER, true/false = definitive
            bool? isOpen = await TryIsFixtureOpenAsync(client, sln);
            if (isOpen == true)
            {
                Console.WriteLine($"  ✓ Live bootstrap: '{Path.GetFileName(sln)}' already open — reusing.");
                return;
            }
            if (isOpen == null)
            {
                Console.WriteLine($"  ⏳ Live bootstrap: XAE busy (RETRYLATER), waiting {retryDelayMs / 1000} s… (attempt {attempt}/{maxAttempts})");
                await Task.Delay(retryDelayMs, ct);
                continue;
            }

            // isOpen == false → try to open
            Console.WriteLine($"  ▶ Live bootstrap: opening fixture in XAE — {sln}");
            var open = await client.CallToolAsync("twincat_open_project",
                new Dictionary<string, object?>
                {
                    ["slnPath"] = sln,
                    ["closeCurrentFirst"] = true,
                },
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token);

            var text = GetText(open);
            if (IsRetryLaterText(text))
            {
                Console.WriteLine($"  ⏳ Live bootstrap: open_project returned RETRYLATER, waiting {retryDelayMs / 1000} s… (attempt {attempt}/{maxAttempts})");
                await Task.Delay(retryDelayMs, ct);
                continue;
            }

            if (open.IsError == true || text.StartsWith("[Error]", StringComparison.Ordinal))
                throw new Xunit.Sdk.XunitException($"LIVE RUN REQUIRED — opening the fixture failed: {text}");

            Console.WriteLine($"  ✓ Live bootstrap: {text}");
            return;
        }

        throw new Xunit.Sdk.XunitException(
            $"LIVE RUN REQUIRED — opening the fixture timed out after {maxAttempts} attempts: XAE remained busy (RETRYLATER).");
    }

    /// <summary>
    /// Returns <c>true</c> if the fixture is open, <c>false</c> if it is not (or another
    /// project is open), and <c>null</c> when XAE is transiently busy (RPC_E_SERVERCALL_RETRYLATER).
    /// </summary>
    private static async Task<bool?> TryIsFixtureOpenAsync(McpClient client, string sln)
    {
        try
        {
            var result = await client.CallToolAsync("twincat_get_active_project",
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
            var text = GetText(result);

            if (IsRetryLaterText(text))
                return null;

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("solutionOpen", out var open) || !open.GetBoolean())
                return false;
            if (!root.TryGetProperty("solutionPath", out var path) || path.ValueKind != JsonValueKind.String)
                return false;
            var openPath = path.GetString();
            return openPath is not null && SameSolution(openPath, sln);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when both paths name the same solution. Compares the file name and the
    /// two parent folder names (the per-run TEMP token), not the full path: the XAE
    /// reports long paths while %TEMP% may be an 8.3 short path. A previous run's
    /// TEMP copy is therefore never mistaken for this run's.
    /// </summary>
    internal static bool SameSolution(string a, string b)
    {
        static IEnumerable<string> Tail(string p)
        {
            var file = new FileInfo(Path.GetFullPath(p));
            yield return file.Name;
            if (file.Directory is { } dir)
            {
                yield return dir.Name;
                if (dir.Parent is { } parent) yield return parent.Name;
            }
        }
        return Tail(a).SequenceEqual(Tail(b), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRetryLaterText(string text)
        => text.Contains("0x8001010A", StringComparison.Ordinal)
        || text.Contains("RPC_E_SERVERCALL_RETRYLATER", StringComparison.OrdinalIgnoreCase)
        || text.Contains("ausgelastet", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsServerListeningAsync(CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync("127.0.0.1", ServerPort);
            var done = await Task.WhenAny(connect, Task.Delay(1000, ct));
            return done == connect && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Connects an MCP client only when the live server is reachable <b>and</b> the
    /// resolved fixture is the project currently open in XAE. Returns null in every
    /// other case — offline <c>dotnet test</c> (no fixture resolved), server down, or
    /// an unrelated project loaded — so cleanup callers (issue #94 sweep) never mutate
    /// a project that isn't the fixture. The caller owns disposal of a non-null client.
    /// </summary>
    public static async Task<McpClient?> TryConnectFixtureClientAsync(CancellationToken ct = default)
    {
        var sln = FixtureSln();
        if (sln is null) return null;
        if (!await IsServerListeningAsync(ct)) return null;

        var client = await ConnectAsync();
        if (client is null) return null;

        if (await TryIsFixtureOpenAsync(client, sln) == true)
            return client;

        await client.DisposeAsync();
        return null;
    }

    private static async Task<McpClient?> ConnectAsync()
    {
        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(ServerUrl),
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


    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0)
            return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}

// Skips use Xunit.SkipException (from Xunit.SkippableFact). A [SkippableFact]
// test that throws it is reported as *skipped* rather than failed. The throw
// sites here and in the fixtures bind to it via the project-wide `using Xunit`.
