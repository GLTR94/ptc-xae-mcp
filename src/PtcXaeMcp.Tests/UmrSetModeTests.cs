using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #15: twincat_umr_set_mode reported "Run" while the system stayed in Config (it only wrote 'r' to the
/// UMR's stdin). It now switches over ADS on the instance's effective NetId and succeeds only on a confirmed
/// ReadState. <see cref="UmrProcessManager.SwitchVerified"/> runs here against a scripted control — no UMR.
/// </summary>
public class UmrSetModeTests
{
    private const string NetId = "192.168.250.20.1.1";

    private sealed class FakeControl(params TargetStateReading[] states) : ISystemControl
    {
        private readonly Queue<TargetStateReading> _states = new(states);
        private TargetStateReading _last = states.Length > 0 ? states[^1] : TargetStateReading.Unreachable("none");
        public List<string> Writes { get; } = [];
        public bool Disposed { get; private set; }

        public TargetStateReading ReadState() => _states.Count > 0 ? (_last = _states.Dequeue()) : _last;

        public string? WriteControl(string adsState)
        {
            Writes.Add(adsState);
            return null;
        }

        public void Dispose() => Disposed = true;
    }

    private static string Switch(string? netId, string mode, FakeControl control, List<string>? opened = null)
    {
        var t = TimeSpan.Zero;
        return UmrProcessManager.SwitchVerified(netId, mode,
            id => { opened?.Add(id); return control; },
            TimeSpan.FromSeconds(30), elapsed: () => t, sleep: d => t += d);
    }

    [Fact]
    public void Run_ConfirmedByReadState_Succeeds_OnTheInstanceNetId()
    {
        var control = new FakeControl(TargetStateReading.Of("Config"), TargetStateReading.Unreachable("restarting"),
            TargetStateReading.Of("Run"));
        var opened = new List<string>();

        var result = Switch(NetId, "run", control, opened);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("switched from Config to Run", result, StringComparison.Ordinal);
        Assert.Equal([NetId], opened);
        Assert.Equal(["Reset"], control.Writes);
        Assert.True(control.Disposed);
    }

    [Fact]
    public void Run_NeverConfirmed_IsAnError()
    {
        // The live case of issue #15: the command is accepted, the system stays in Config.
        var control = new FakeControl(TargetStateReading.Of("Config"));

        var result = Switch(NetId, "run", control);

        Assert.StartsWith("[Error]", result, StringComparison.Ordinal);
        Assert.Contains("did not read Run", result, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownNetId_IsRefused_NothingOpened()
    {
        var control = new FakeControl(TargetStateReading.Of("Config"));
        var opened = new List<string>();

        var result = Switch(null, "run", control, opened);

        Assert.StartsWith("[Error]", result, StringComparison.Ordinal);
        Assert.Contains("NetId is unknown", result, StringComparison.Ordinal);
        Assert.Empty(opened);
        Assert.Empty(control.Writes);
    }

    [Fact]
    public void AlreadyInMode_SendsNothing_AndIsOk()
    {
        var control = new FakeControl(TargetStateReading.Of("Config"));

        var result = Switch(NetId, "config", control);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Empty(control.Writes);
    }

    [Fact]
    public void InvalidMode_IsRefused()
    {
        var control = new FakeControl(TargetStateReading.Of("Config"));

        Assert.StartsWith("[Error]", Switch(NetId, "turbo", control), StringComparison.Ordinal);
        Assert.Empty(control.Writes);
    }
}
