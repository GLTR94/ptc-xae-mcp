using System.Text;
using System.Text.Json;
using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Guard + error-shaping layer over <see cref="ITwinCatSession"/> for the
/// IO-Link feature group. Mirrors <see cref="FieldbusCoordinator"/>: never throws
/// across the MCP boundary; errors come back as <c>[Error] …</c> strings.
/// </summary>
internal sealed class IoLinkCoordinator(ITwinCatSession session)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Read the IO-Link config of a master node. Returns (summary, json); json is "{}" on error.</summary>
    public (string Summary, string Json) GetConfig(string treePath)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            return ("[Error] treePath is required.", "{}");
        if (treePath.Equals("TIID", StringComparison.OrdinalIgnoreCase))
            return ("[Error] treePath must be a master node under TIID (not the root).", "{}");

        string? error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, "{}");

        IoLinkConfig cfg;
        try { cfg = session.GetIoLinkConfig(treePath); }
        catch (InvalidOperationException ex) { return ($"[Error] {ex.Message}", "{}"); }

        if (cfg.PortCount == 0)
            return ($"No IO-Link ports found on '{treePath}' (type {cfg.MasterType}). " +
                    "Is this an IO-Link master (e.g. EL6224)?", JsonSerializer.Serialize(cfg, JsonOpts));

        string json = JsonSerializer.Serialize(cfg, JsonOpts);
        return (Summarize(cfg), json);
    }

    private static string Summarize(IoLinkConfig cfg)
    {
        var sb = new StringBuilder();
        int configured = cfg.Ports.Count(p => p.Configured);
        sb.Append(CultureInfo.InvariantCulture, $"{cfg.MasterType} at '{cfg.MasterPath}': {cfg.PortCount} IO-Link port(s), {configured} configured.");
        foreach (var p in cfg.Ports.Where(p => p.Configured))
            sb.Append(CultureInfo.InvariantCulture, $" Port {p.Port} = {p.DeviceName} ({p.ProcessData.Count} process-data point(s)).");
        return sb.ToString();
    }
}
