using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- ads-variable-rw ----
    public AdsVariableResult FakeReadAdsVariableResult { get; set; } =
        new AdsVariableResult(true, "GVL.bTest", "BOOL", false, "127.0.0.1.1.1", 851, "OK");
    public AdsVariableResult FakeWriteAdsVariableResult { get; set; } =
        new AdsVariableResult(true, "GVL.bTest", "BOOL", true, "127.0.0.1.1.1", 851, "OK");
    public Exception? ReadAdsVariableThrows { get; set; }
    public Exception? WriteAdsVariableThrows { get; set; }

    public AdsVariableResult ReadAdsVariable(string symbolPath, string? amsNetId, int adsPort)
    {
        CallLog.Add($"ReadAdsVariable(sym={symbolPath}, net={amsNetId ?? "<null>"}, port={adsPort})");
        if (ReadAdsVariableThrows is not null) throw ReadAdsVariableThrows;
        return FakeReadAdsVariableResult;
    }

    /// <summary>Runs inside <see cref="WriteAdsVariable"/> (after the call is logged) with
    /// (symbolPath, value) — lets a test hold a write in flight to drive interleavings.</summary>
    public Action<string, string>? OnWriteAdsVariable { get; set; }

    public AdsVariableResult WriteAdsVariable(string symbolPath, string value, string? amsNetId, int adsPort)
    {
        lock (CallLog)
            CallLog.Add($"WriteAdsVariable(sym={symbolPath}, val={value}, net={amsNetId ?? "<null>"}, port={adsPort})");
        OnWriteAdsVariable?.Invoke(symbolPath, value);
        if (WriteAdsVariableThrows is not null) throw WriteAdsVariableThrows;
        return FakeWriteAdsVariableResult;
    }

    // ---- ibn-live: batch read ----
    public AdsBatchReadResult FakeReadAdsVariablesBatchResult { get; set; } =
        new AdsBatchReadResult(true,
            new[] { new AdsBatchReadItem("GVL.bTest", "BOOL", false, true, "OK") },
            "127.0.0.1.1.1", 851, "OK");
    public Exception? ReadAdsVariablesBatchThrows { get; set; }

    public AdsBatchReadResult ReadAdsVariablesBatch(IReadOnlyList<string> symbolPaths, string? amsNetId, int adsPort)
    {
        CallLog.Add($"ReadAdsVariablesBatch(n={symbolPaths.Count}, syms=[{string.Join(",", symbolPaths)}], net={amsNetId ?? "<null>"}, port={adsPort})");
        if (ReadAdsVariablesBatchThrows is not null) throw ReadAdsVariablesBatchThrows;
        return FakeReadAdsVariablesBatchResult;
    }

    public SignalTransitionResult FakeMeasureSignalTransitionResult { get; set; } =
        new SignalTransitionResult(true, 123.4, false, "OK");
    public Exception? MeasureSignalTransitionThrows { get; set; }

    public SignalTransitionResult MeasureSignalTransition(string startSymbol, string startEquals,
        string endSymbol, string endEquals, int timeoutMs, string? amsNetId, int adsPort)
    {
        CallLog.Add($"MeasureSignalTransition(start={startSymbol}=={startEquals}, end={endSymbol}=={endEquals}, timeout={timeoutMs}, net={amsNetId ?? "<null>"}, port={adsPort})");
        if (MeasureSignalTransitionThrows is not null) throw MeasureSignalTransitionThrows;
        return FakeMeasureSignalTransitionResult;
    }

    // ---- diagnostics: fb-instance-inspect ----
    public InspectFbInstanceResult FakeInspectFbInstanceResult { get; set; } =
        new InspectFbInstanceResult(true, "MAIN.fbMachine", "FB_Machine",
            new[] { new FbMemberEntry("Step", "MAIN.fbMachine.Step", false, "INT", 2, 0) },
            Array.Empty<FbRpcMethodEntry>(),
            "127.0.0.1.1.1", 851, "OK");
    public Exception? InspectFbInstanceThrows { get; set; }

    public InspectFbInstanceResult InspectFbInstance(string instancePath, string? amsNetId, int adsPort)
    {
        CallLog.Add($"InspectFbInstance(path={instancePath}, net={amsNetId ?? "<null>"}, port={adsPort})");
        if (InspectFbInstanceThrows is not null) throw InspectFbInstanceThrows;
        return FakeInspectFbInstanceResult;
    }
}
