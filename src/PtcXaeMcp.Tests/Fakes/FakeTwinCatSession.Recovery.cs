using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- multi-xae-robustness (issue #57) ----
    public string? MultiXaeBlockReasonResult { get; set; }
    public string FakeForceCloseResult { get; set; } =
        "No TcXaeShell process is running — nothing to close.";

    // Not logged to CallLog: like HasOpenSolution this is a precondition probe
    // RequireOpenSolution runs before every mutating call, not a domain
    // operation — logging it would break every coordinator's call-order assert.
    public string? MultiXaeBlockReason() => MultiXaeBlockReasonResult;

    public string ForceCloseXae(int? pid = null)
    {
        CallLog.Add(pid is null ? "ForceCloseXae()" : $"ForceCloseXae({pid})");
        return FakeForceCloseResult;
    }
}
