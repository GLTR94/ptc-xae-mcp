using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- plc-login ----
    public LoginPlcResult FakeLoginPlcResult { get; set; } =
        new LoginPlcResult(true, "OnlineChange", "none", true, "running", "PLC logged in (fake).");
    public Exception? LoginPlcThrows { get; set; }

    public LoginPlcResult LoginPlc(LoginMode mode, bool allowDownloadFallback = false)
    {
        CallLog.Add($"LoginPlc({mode}, downloadFallback={allowDownloadFallback})");
        if (LoginPlcThrows is not null) throw LoginPlcThrows;
        return FakeLoginPlcResult;
    }
}
