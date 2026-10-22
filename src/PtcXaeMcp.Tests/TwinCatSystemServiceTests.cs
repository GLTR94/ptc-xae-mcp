using PtcXaeMcp.WinForms.Mcp;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TwinCatSystemServiceProbe
{
    public const string Name = "TwinCatSystemServiceProbe";
}

[Collection(TwinCatSystemServiceProbe.Name)]
public sealed class TwinCatSystemServiceTests : IDisposable
{
    private readonly Func<SystemServiceStatus> _original = TwinCatSystemService.Probe;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tcsys_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        TwinCatSystemService.Probe = _original;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private McpActivity NewActivity() =>
        new(jsonLog: new McpActivityJsonLog(_dir, keepRuns: 1, keepDays: 1));

    private static Func<SystemServiceStatus> Fixed(SystemServiceState s, string detail) =>
        () => new SystemServiceStatus(s, detail);

    [Fact]
    public async Task AdsTool_WhileStopped_IsBlockedWithoutRunningTheBody()
    {
        TwinCatSystemService.Probe = Fixed(SystemServiceState.NotRunning, "Stopped");
        bool ran = false;

        var result = await NewActivity().TraceAsync("twincat_activate_config", null,
            () => { ran = true; return Task.FromResult("Activated"); });

        Assert.False(ran);
        Assert.StartsWith("[Error] Blocked: the TwinCAT System Service (TcSysSrv) is Stopped", result);
        Assert.Contains("reboot", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("twincat_get_status")]
    [InlineData("twincat_ibn_abort")]
    [InlineData("twincat_umr_stop")]
    public async Task NonAdsAndStopTools_WhileStopped_StillRun(string tool)
    {
        TwinCatSystemService.Probe = Fixed(SystemServiceState.NotRunning, "Stopped");

        var result = await NewActivity().TraceAsync(tool, null, () => Task.FromResult("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task UnknownState_NeverBlocks()
    {
        TwinCatSystemService.Probe = Fixed(SystemServiceState.Unknown, "not installed");

        var result = await NewActivity().TraceAsync("twincat_login", null, () => Task.FromResult("ok"));

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task ServiceStoppingDuringAnAdsTool_AppendsAWarning()
    {
        var state = SystemServiceState.Running;
        TwinCatSystemService.Probe = () => new SystemServiceStatus(state, state == SystemServiceState.Running ? "Running" : "Stopped");

        var result = await NewActivity().TraceAsync("twincat_activate_config", null, () =>
        {
            state = SystemServiceState.NotRunning;
            return Task.FromResult("Activated");
        });

        Assert.StartsWith("Activated", result);
        Assert.Contains("[Warning] The TwinCAT System Service (TcSysSrv) went from Running to Stopped", result);
    }

    [Fact]
    public void StatusText_NamesEachState()
    {
        Assert.Equal("TwinCAT System: Running",
            TwinCatSystemService.StatusText(new(SystemServiceState.Running, "Running")));
        Assert.StartsWith("TwinCAT System: Stopped",
            TwinCatSystemService.StatusText(new(SystemServiceState.NotRunning, "Stopped")));
        Assert.Equal("TwinCAT System: Unknown (not installed)",
            TwinCatSystemService.StatusText(new(SystemServiceState.Unknown, "not installed")));
    }

    [Fact]
    public void ThrowingProbe_ReadsAsUnknown()
    {
        TwinCatSystemService.Probe = () => throw new InvalidOperationException("boom");

        Assert.Equal(SystemServiceState.Unknown, TwinCatSystemService.Query().State);
    }
}
