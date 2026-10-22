using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- plc-run-control / boot-project ----
    public PlcRunControlResult FakePlcStartResult { get; set; } =
        new(true, "Stop", "Run", "local", 851, "PLC started: Stop → Run.");
    public PlcRunControlResult FakePlcStopResult { get; set; } =
        new(true, "Run", "Stop", "local", 851, "PLC stopped: Run → Stop.");
    public BootProjectResult FakeBootProjectResult { get; set; } =
        new(true, true, "ActivateBootProject", new[] { "ActivateBootProject: available" }, "Dry run only.");
    public Exception? ActivateBootProjectThrows { get; set; }

    public PlcRunControlResult PlcStart(string? amsNetId, int adsPort)
    {
        CallLog.Add($"PlcStart({amsNetId ?? "<null>"},{adsPort})");
        return FakePlcStartResult;
    }

    public PlcRunControlResult PlcStop(string? amsNetId, int adsPort)
    {
        CallLog.Add($"PlcStop({amsNetId ?? "<null>"},{adsPort})");
        return FakePlcStopResult;
    }

    public BootProjectResult ActivateBootProject(bool dryRun)
    {
        CallLog.Add($"ActivateBootProject({dryRun})");
        if (ActivateBootProjectThrows is not null) throw ActivateBootProjectThrows;
        return FakeBootProjectResult;
    }
}
