using System.Globalization;
namespace PtcXaeMcp.WinForms.TwinCat;

// Orchestrates the run-control and boot-project tools. Deliberately does NOT
// call SessionGuards.RequireOpenSolution for start/stop: those talk ADS to the
// runtime, not to the XAE project, and must work when no solution is loaded
// (e.g. after a download from another session). ActivateBootProject does need
// the project, because it acts on the PLC project node.
internal sealed class PlcControlCoordinator(ITwinCatSession session)
{
    public string PlcStart(string? amsNetId, int adsPort)
    {
        var r = session.PlcStart(amsNetId, adsPort);
        return Format(r);
    }

    public string PlcStop(string? amsNetId, int adsPort)
    {
        var r = session.PlcStop(amsNetId, adsPort);
        return Format(r);
    }

    private static string Format(PlcRunControlResult r) =>
        r.Success
            ? $"{r.Message} (target {r.AmsNetId}:{r.AdsPort})"
            : r.Message.StartsWith("[Error]", StringComparison.Ordinal)
                ? r.Message
                : $"[Error] {r.Message}";

    public string ActivateBootProject(bool dryRun)
    {
        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        BootProjectResult r;
        try { r = session.ActivateBootProject(dryRun); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(r.Message);
        if (r.Probed.Count > 0)
        {
            sb.AppendLine("Probed Automation-Interface members:");
            foreach (var p in r.Probed) sb.AppendLine(CultureInfo.InvariantCulture, $"  {p}");
        }
        return sb.ToString().TrimEnd();
    }
}
