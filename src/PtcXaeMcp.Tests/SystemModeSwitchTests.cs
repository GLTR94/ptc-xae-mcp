using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #203: switching a target's TwinCAT system to Config/Run over ADS, decided by ReadState only.</summary>
public class SystemModeSwitchTests
{
    private sealed class FakeControl(params TargetStateReading[] states) : ISystemControl
    {
        private readonly Queue<TargetStateReading> _states = new(states);
        private TargetStateReading _last = states.Length > 0 ? states[^1] : TargetStateReading.Unreachable("none");
        public List<string> Writes { get; } = [];
        public string? WriteError { get; set; }

        public TargetStateReading ReadState() => _states.Count > 0 ? (_last = _states.Dequeue()) : _last;

        public string? WriteControl(string adsState)
        {
            Writes.Add(adsState);
            return WriteError;
        }

        public void Dispose() { }
    }

    private static TimeSpan _t;
    private static string Run(FakeControl c, SystemMode mode, int timeoutS = 10)
    {
        _t = TimeSpan.Zero;
        return SystemModeSwitch.Switch(c, "192.168.250.20.1.1", mode, TimeSpan.FromSeconds(timeoutS),
            elapsed: () => _t, sleep: d => _t += d);
    }

    [Fact]
    public void RunToConfig_SendsReconfig_AndWaitsForConfig()
    {
        var c = new FakeControl(TargetStateReading.Of("Run"), TargetStateReading.Of("Run"),
            TargetStateReading.Unreachable("restarting"), TargetStateReading.Of("Config"));

        var r = Run(c, SystemMode.Config);

        Assert.Equal(["Reconfig"], c.Writes);
        Assert.Contains("switched from Run to Config", r, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigToRun_SendsReset()
    {
        var c = new FakeControl(TargetStateReading.Of("Config"), TargetStateReading.Of("Run"));

        var r = Run(c, SystemMode.Run);

        Assert.Equal(["Reset"], c.Writes);
        Assert.False(r.StartsWith("[Error]", StringComparison.Ordinal), r);
    }

    [Fact]
    public void AlreadyInMode_SendsNothing()
    {
        var c = new FakeControl(TargetStateReading.Of("Run"));

        var r = Run(c, SystemMode.Run);

        Assert.Empty(c.Writes);
        Assert.Contains("already in Run", r, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreachableTarget_SendsNothing_AndIsAnError()
    {
        var c = new FakeControl(TargetStateReading.Unreachable("ClientSyncTimeOut"));

        var r = Run(c, SystemMode.Config);

        Assert.Empty(c.Writes);
        Assert.StartsWith("[Error]", r);
        Assert.Contains("Nothing was sent", r, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusedWrite_IsAnError()
    {
        var c = new FakeControl(TargetStateReading.Of("Run")) { WriteError = "ADS error DeviceAccessDenied" };

        var r = Run(c, SystemMode.Config);

        Assert.StartsWith("[Error]", r);
        Assert.Contains("refused", r, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedButIgnored_TimesOut_WithTheLastState()
    {
        var c = new FakeControl(TargetStateReading.Of("Run"));

        var r = Run(c, SystemMode.Config, timeoutS: 5);

        Assert.Equal(["Reconfig"], c.Writes);
        Assert.StartsWith("[Error]", r);
        Assert.Contains("last: state Run", r, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("config", true)]
    [InlineData(" RUN ", true)]
    [InlineData("stop", false)]
    [InlineData(null, false)]
    public void ParsesMode(string? s, bool ok) => Assert.Equal(ok, SystemModeSwitch.TryParseMode(s, out _));
}
