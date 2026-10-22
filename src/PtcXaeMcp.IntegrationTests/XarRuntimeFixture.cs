using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Collection fixture of Stage XAR. It brings the fixture's PLC to Run on an <b>explicit</b> runtime target
/// before the runtime tests execute (issue #195). The target comes from the environment, see <see cref="LiveXae"/>:
/// <see cref="LiveXae.RuntimeNetIdEnvVar"/> (required), <see cref="LiveXae.RuntimePlatformEnvVar"/> and
/// <see cref="LiveXae.RuntimeUmrInstanceEnvVar"/> (optional).
///
/// 1. No runtime NetId: the stage skips with that reason (a hard failure under REQUIRE_LIVE). It never
///    activates anything implicitly. The neutral fixture has no target NetId, so an activation without one
///    would hit the local kernel runtime, which is what blocked the XAE in #195.
/// 2. The NetId answers no ADS state read: only when a UMR instance is named is it started, with
///    <c>twincat_umr_start {instanceName}</c>. Never the no-argument form, and never while the NetId answers:
///    that could start a second UMR next to a running one. Without an instance name the stage skips.
/// 3. Open the per-run fixture copy and <c>twincat_set_target</c> (the NetId, plus the platform when given).
///    Then <c>twincat_build_solution</c> and <c>twincat_activate_config(autoConfirmRestart)</c>, which only
///    succeeds once the target reads Run. The exception: the target already reads Run and already answers the
///    fixture's probe symbol. It is then not activated again. On 2026-09-27 a running user-mode runtime
///    ignored StartRestartTwinCAT(), so the activation failed as "restart not confirmed". That did not
///    reproduce on 2026-10-08 (TC 4026, UmRT_AI): twincat_activate_config from Run restarted the runtime
///    and reached Run again twice. The skip stays because a second restart proves nothing new.
///    <see cref="ActivationSkippedReason"/> says so.
/// 4. Poll <c>twincat_read_ads_variable</c> with <c>amsNetId</c> for up to 90 s. A real PLC symbol read is the
///    only proof every XAR test depends on. If it never succeeds, the stage is not ready.
/// A host built without runtime interop (TwinCatNotBuilt) is its own, named skip.
///
/// Test classes inject this via the ctor parameter, call <see cref="SkipIfNotReady"/> at the top of every
/// test, and pass <see cref="AmsNetId"/> as <c>amsNetId</c> in every ADS call.
/// </summary>
public sealed class XarRuntimeFixture : IAsyncLifetime
{
    private static readonly TimeSpan RuntimeReadTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan UmrAnswerTimeout = TimeSpan.FromSeconds(30);

    private McpClient? _client;

    public bool IsReady { get; private set; }
    public bool IsWrongBuild { get; private set; }
    public string SkipReason { get; private set; } = string.Empty;

    /// <summary>The runtime this stage activates and reads (<see cref="LiveXae.RuntimeNetId"/>); empty when not set.</summary>
    public string AmsNetId { get; } = LiveXae.RuntimeNetId ?? string.Empty;

    /// <summary>What <c>twincat_activate_config</c> returned when this stage activated the fixture; null when it did not.</summary>
    public string? ActivationResult { get; private set; }

    /// <summary>Why this stage did not activate (the target already ran the fixture PLC); null when it activated.</summary>
    public string? ActivationSkippedReason { get; private set; }

    /// <summary>
    /// Arguments of <c>twincat_set_target</c> for this stage's target: the NetId, and the platform only when
    /// <see cref="LiveXae.RuntimePlatformEnvVar"/> is set.
    /// </summary>
    public Dictionary<string, object?> SetTargetArgs()
    {
        var args = new Dictionary<string, object?> { ["amsNetId"] = AmsNetId };
        if (LiveXae.RuntimePlatform is { } platform) args["platform"] = platform;
        return args;
    }

    public async Task InitializeAsync()
    {
        var fixtureSln = LiveXae.FixtureSln();
        if (fixtureSln is null)
        {
            SkipReason = $"Stage XAR skipped — {LiveXae.FixtureEnvVar} not set or .sln not found.";
            PrintBanner(false, null);
            return;
        }

        if (AmsNetId.Length == 0)
        {
            SkipReason = $"⏭ Stage XAR skipped — {LiveXae.MissingRuntimeTargetReason}";
            PrintBanner(false, SkipReason);
            return;
        }

        // Bring the live environment up (start the host, open the fixture) when
        // running in live mode — idempotent and a no-op if Stage XAE already did
        // it. Lets the runtime stage bootstrap even when run in isolation.
        await LiveXae.EnsureLiveAsync();

        try
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(LiveXae.ServerUrl),
                ConnectionTimeout = TimeSpan.FromSeconds(5)
            });
            _client = await McpClient.CreateAsync(transport,
                cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }
        catch
        {
            SkipReason = $"Stage XAR skipped — MCP server not reachable on {LiveXae.ServerUrl}.";
            PrintBanner(false, null);
            return;
        }

        // 1. A host built without runtime interop cannot pass any XAR test: its own, named skip.
        if (await RuntimeReadiness.IsWrongBuildAsync(_client, AmsNetId))
        {
            IsWrongBuild = true;
            SkipReason = "⏭ Stage XAR skipped — host built without runtime interop (TwinCat=false build).";
            PrintBanner(false, SkipReason);
            return;
        }

        // 2. The target has to answer before anything is activated on it.
        string? unreachable = await EnsureTargetAnswersAsync();
        if (unreachable is not null)
        {
            SkipReason = $"⏭ Stage XAR not exercised — {unreachable}";
            PrintBanner(false, SkipReason);
            return;
        }

        // 3. This run's fixture onto the target. The activation result is checked (it now only succeeds once
        //    the target reads Run), but it is not the proof the XAR tests need — that is step 4.
        string? activationFailure = await ActivateOnTargetAsync(fixtureSln);
        if (activationFailure is not null)
        {
            SkipReason = $"⏭ Stage XAR not exercised — bringing the fixture to Run on {AmsNetId} failed: {activationFailure}";
            PrintBanner(false, SkipReason);
            return;
        }

        // 4. The only proof the runtime is usable: a real PLC symbol read on the target succeeds —
        //    exactly what every XAR test depends on. If it never does, every XAR test skips (or hard-fails
        //    under REQUIRE_LIVE) instead of giving a false green.
        var (up, lastRead) = await RuntimeReadiness.WaitForRuntimeAsync(_client, AmsNetId, RuntimeReadTimeout);
        if (up)
        {
            IsReady = true;
            PrintBanner(true, null);
        }
        else
        {
            SkipReason = $"⏭ Stage XAR not exercised — PLC runtime on {AmsNetId} not reachable: a live symbol read of "
                + $"{FixtureLayout.UintSymbol} failed for {RuntimeReadTimeout.TotalSeconds:0} s after the activation "
                + $"(last: {Shorten(lastRead)}). Check the PLC is running on the target's ADS port 851.";
            PrintBanner(false, SkipReason);
        }
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }

    /// <summary>
    /// Throws a <see cref="SkipException"/> (or hard fail) when the XAR
    /// runtime stage is not ready. Call at the top of every XAR test.
    /// </summary>
    public void SkipIfNotReady()
    {
        if (IsReady) return;

        if (LiveXae.RequireLive)
            throw new Xunit.Sdk.XunitException("LIVE RUN REQUIRED — " + SkipReason);

        throw new SkipException(SkipReason);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Null when the target answers an ADS state read, possibly after starting the named UMR instance;
    /// otherwise why it does not.
    /// </summary>
    private async Task<string?> EnsureTargetAnswersAsync()
    {
        string? state = AdsStateProbe.TryReadState(AmsNetId);
        if (state is not null)
        {
            Console.WriteLine($"  ✓ Stage XAR: {AmsNetId} answers an ADS state read ({state}) — no UMR is started.");
            return null;
        }

        string? instance = LiveXae.RuntimeUmrInstance;
        if (instance is null)
            return $"{AmsNetId} answers no ADS state read (runtime not running, or not reachable: neither a classic ADS route nor ADS-over-MQTT through a broker that "
                 + "both sides use, see Target\\Routes\\AdsOverMqtt.xml; an MQTT target needs no static route, and a broadcast search "
                 + "never finds it). Start that runtime, "
                 + $"or set {LiveXae.RuntimeUmrInstanceEnvVar} to the registered UMR instance the suite may start.";

        Console.WriteLine($"  ▶ Stage XAR: {AmsNetId} answers no ADS state read — starting UMR instance '{instance}'.");
        var (started, text) = await CallAsync("twincat_umr_start",
            new Dictionary<string, object?> { ["instanceName"] = instance }, TimeSpan.FromSeconds(60));
        if (!started)
            return $"twincat_umr_start {{ instanceName: '{instance}' }} failed: {Shorten(text)}";

        string? after = await AdsStateProbe.WaitForStateAsync(AmsNetId, UmrAnswerTimeout);
        if (after is not null)
        {
            Console.WriteLine($"  ✓ Stage XAR: {text} {AmsNetId} now answers ({after}).");
            return null;
        }
        return $"UMR instance '{instance}' was started ({Shorten(text)}), but {AmsNetId} still answers no ADS state "
             + $"read after {UmrAnswerTimeout.TotalSeconds:0} s. Is {AmsNetId} that instance's AMS NetId?";
    }

    /// <summary>
    /// Null when this run's fixture was built and activated on the target, or the target already ran it; otherwise
    /// what failed.
    /// </summary>
    private async Task<string?> ActivateOnTargetAsync(string fixtureSln)
    {
        try
        {
            var (opened, openText) = await CallAsync("twincat_open_project",
                new Dictionary<string, object?> { ["slnPath"] = fixtureSln, ["closeCurrentFirst"] = true },
                TimeSpan.FromSeconds(120));
            if (!opened) return $"twincat_open_project: {Shorten(openText)}";

            var (targeted, targetText) = await CallAsync("twincat_set_target", SetTargetArgs(), TimeSpan.FromSeconds(60));
            if (!targeted) return $"twincat_set_target: {Shorten(targetText)}";
            Console.WriteLine($"  ✓ Stage XAR: {targetText}");

            // Already running the fixture's PLC: no second restart (see the class comment).
            string? state = AdsStateProbe.TryReadState(AmsNetId);
            if (string.Equals(state, "Run", StringComparison.OrdinalIgnoreCase)
                && await RuntimeReadiness.IsRuntimeInRunAsync(_client!, AmsNetId))
            {
                ActivationSkippedReason =
                    $"{AmsNetId} already read Run and answered {FixtureLayout.UintSymbol}, so this run did not activate the " +
                    "fixture again: the PLC is already running, and a second restart proves nothing new (on 2026-09-27 a " +
                    "running user-mode runtime also ignored StartRestartTwinCAT(); on 2026-10-08 it did not). Switch the " +
                    "runtime to Config mode to exercise the activation.";
                Console.WriteLine($"  ✓ Stage XAR: {ActivationSkippedReason}");
                return null;
            }

            var (_, buildText) = await CallAsync("twincat_build_solution",
                new Dictionary<string, object?>(), TimeSpan.FromSeconds(300));
            if (!buildText.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase))
                return $"twincat_build_solution: {Shorten(buildText)}";

            var (activated, activateText) = await CallAsync("twincat_activate_config",
                new Dictionary<string, object?> { ["autoConfirmRestart"] = true }, TimeSpan.FromSeconds(300));
            if (!activated || !activateText.Contains("Activated", StringComparison.OrdinalIgnoreCase))
                return $"twincat_activate_config: {Shorten(activateText)}";
            Console.WriteLine($"  ✓ Stage XAR: {Shorten(activateText)}");
            ActivationResult = activateText;
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>A tool call: ok = neither an MCP error nor an [Error] text.</summary>
    private async Task<(bool Ok, string Text)> CallAsync(string tool, Dictionary<string, object?> args, TimeSpan timeout)
    {
        var result = await _client!.CallToolAsync(tool, args,
            cancellationToken: new CancellationTokenSource(timeout).Token);
        string text = GetText(result);
        return (result.IsError != true && !text.StartsWith("[Error]", StringComparison.Ordinal), text);
    }

    private static string Shorten(string text)
    {
        string oneLine = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        return oneLine.Length <= 600 ? oneLine : oneLine[..600] + "…";
    }

    private static void PrintBanner(bool ready, string? explicitMessage)
    {
        Console.WriteLine("================================================================");
        if (ready)
        {
            Console.WriteLine($"  ⚡ Stage XAR — runtime {LiveXae.RuntimeNetId} in Run mode, XAR tests will execute");
        }
        else if (explicitMessage is not null)
        {
            Console.WriteLine($"  {explicitMessage}");
        }
        else
        {
            Console.WriteLine("  ⏭ Stage XAR not exercised — not started.");
        }
        Console.WriteLine("================================================================");
    }

    private static string GetText(CallToolResult result)
    {
        var content = result.Content;
        if (content is null || content.Count == 0) return string.Empty;
        return string.Join("\n", content.OfType<TextContentBlock>().Select(c => c.Text));
    }
}
