using System.Diagnostics;
#if TWINCAT
using TwinCAT.Ads;
#endif

namespace PtcXaeMcp.WinForms.TwinCat;

internal enum SystemMode { Config, Run }

/// <summary>ADS access to a target's TwinCAT system service (port 10000). Never throws.</summary>
internal interface ISystemControl : IDisposable
{
    TargetStateReading ReadState();

    /// <summary>Sends WriteControl with the given ADS state name ("Reconfig" or "Reset"); null on success, else the error.</summary>
    string? WriteControl(string adsState);
}

/// <summary>
/// Issue #203: on a runtime that already runs, StartRestartTwinCAT() is ignored, and twincat_umr_set_mode only
/// reaches a UMR the host started itself. The XAE's own "Restart TwinCAT (Config Mode)" / "Restart TwinCAT System"
/// are ADS WriteControl calls on port 10000: Reconfig → Config, Reset → Run. Success is decided by ReadState only.
/// </summary>
internal static class SystemModeSwitch
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public static string ControlFor(SystemMode mode) => mode == SystemMode.Config ? "Reconfig" : "Reset";

    public static bool TryParseMode(string? s, out SystemMode mode)
    {
        mode = SystemMode.Run;
        switch (s?.Trim().ToLowerInvariant())
        {
            case "config": mode = SystemMode.Config; return true;
            case "run": mode = SystemMode.Run; return true;
            default: return false;
        }
    }

    public static string Switch(ISystemControl control, string netId, SystemMode mode, TimeSpan timeout,
        Func<TimeSpan>? elapsed = null, Action<TimeSpan>? sleep = null)
    {
        var sw = Stopwatch.StartNew();
        elapsed ??= () => sw.Elapsed;
        sleep ??= Thread.Sleep;
        string want = mode.ToString();

        var before = control.ReadState();
        if (before.Error is not null)
            return $"[Error] The TwinCAT system of {netId} does not answer over ADS (port 10000): {before.Error}. Nothing was sent.";
        if (string.Equals(before.State, want, StringComparison.OrdinalIgnoreCase))
            return $"The TwinCAT system of {netId} is already in {want} mode. Nothing was sent.";

        string control_ = ControlFor(mode);
        string? writeError = control.WriteControl(control_);
        if (writeError is not null)
            return $"[Error] WriteControl({control_}) to {netId}:10000 was refused: {writeError}. State stays {before.State}.";

        TargetStateReading last = before;
        while (elapsed() < timeout)
        {
            sleep(PollInterval);
            last = control.ReadState();
            if (last.Error is null && string.Equals(last.State, want, StringComparison.OrdinalIgnoreCase))
                return $"The TwinCAT system of {netId} switched from {before.State} to {want} " +
                       $"(WriteControl {control_}, confirmed by ReadState after {elapsed().TotalSeconds:F1} s).";
        }
        string lastText = last.Error is null ? $"state {last.State}" : $"unreachable ({last.Error})";
        return $"[Error] WriteControl({control_}) was accepted by {netId}, but the system did not read {want} " +
               $"within {timeout.TotalSeconds:F0} s (last: {lastText}).";
    }
}

/// <summary>The production <see cref="ISystemControl"/> over Beckhoff.TwinCAT.Ads.</summary>
internal sealed class AdsSystemControl : ISystemControl
{
    private readonly TargetStateReader _reader;
    private readonly string _netId;

    public AdsSystemControl(string netId)
    {
        _netId = netId;
        _reader = TargetStateReader.Open(netId);
    }

    public TargetStateReading ReadState() => _reader.Read();

#if TWINCAT
    public string? WriteControl(string adsState)
    {
        try
        {
            using var client = new AdsClient { Timeout = 5000 };
            client.Connect(_netId, ActivationTarget.SystemServicePort);
            var state = Enum.Parse<AdsState>(adsState);
            AdsErrorCode error = client.TryWriteControl(new StateInfo(state, 0));
            return error == AdsErrorCode.NoError ? null : $"ADS error {error}";
        }
        catch (Exception ex) { return ex.Message; }
    }
#else
    public string? WriteControl(string adsState) => TwinCatNotBuilt.Message;
#endif

    public void Dispose() => _reader.Dispose();
}
