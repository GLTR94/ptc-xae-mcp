using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- plc-online-status --------------------------------------------------
    public PlcOnlineStateResult FakePlcOnlineStateResult { get; set; } =
        new PlcOnlineStateResult(false, "XAE is Offline (fake).");
    public Exception? GetPlcOnlineStateThrows { get; set; }

    public PlcOnlineStateResult GetPlcOnlineState()
    {
        CallLog.Add("GetPlcOnlineState()");
        if (GetPlcOnlineStateThrows is not null) throw GetPlcOnlineStateThrows;
        return FakePlcOnlineStateResult;
    }
}
