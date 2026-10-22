using System.Globalization;
namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>One installed IODD: its filename and parsed identity.</summary>
internal sealed record IoddCatalogEntry(string FileName, IoddIdentity Identity);

/// <summary>
/// Index of the IODDs installed in TwinCAT's IO-Link folder
/// (<c>…\Config\Io\IOLink</c>). The IO-Link analogue of the EtherCAT ESI catalog:
/// answers "is the device description for device X installed?" so the workflow can
/// STOP and ask the user to obtain a missing IODD instead of configuring blind.
/// </summary>
internal sealed class IoddCatalog
{
    private readonly List<IoddCatalogEntry> _entries;

    public string Folder { get; }
    public IReadOnlyList<IoddCatalogEntry> Entries => _entries;

    private IoddCatalog(string folder, List<IoddCatalogEntry> entries)
    {
        Folder = folder;
        _entries = entries;
    }

    /// <summary>Scan a folder, parsing every <c>*.xml</c> that is a valid IODD (others skipped silently).</summary>
    public static IoddCatalog Scan(string folder)
    {
        var entries = new List<IoddCatalogEntry>();
        if (Directory.Exists(folder))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.xml"))
            {
                try
                {
                    var id = IoddXmlParser.ParseIdentity(File.ReadAllText(file));
                    entries.Add(new IoddCatalogEntry(Path.GetFileName(file), id));
                }
                catch { /* not an IODD / unreadable — ignore */ }
            }
        }
        return new IoddCatalog(folder, entries);
    }

    public IoddCatalogEntry? FindByFileName(string fileName) =>
        _entries.FirstOrDefault(e => e.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

    public IoddCatalogEntry? FindByProductId(string productId) =>
        _entries.FirstOrDefault(e =>
            e.Identity.ProductIds.Any(p => p.Equals(productId, StringComparison.OrdinalIgnoreCase)));

    public IoddCatalogEntry? FindByIds(int vendorId, int deviceId) =>
        _entries.FirstOrDefault(e => e.Identity.VendorId == vendorId && e.Identity.DeviceId == deviceId);
}

/// <summary>
/// Missing-IODD policy, mirroring the EtherCAT missing-ESI policy: STOP and report
/// the device + expected location + how to obtain it; do not configure/link blind.
/// </summary>
internal static class IoddPresence
{
    /// <summary>
    /// For a live IO-Link config, returns a fail-closed report if any <b>configured</b>
    /// port references an IODD that is not installed; null when all are present.
    /// </summary>
    public static string? MissingReport(IoLinkConfig cfg, IoddCatalog catalog)
    {
        var missing = new List<IoLinkPort>();
        foreach (var port in cfg.Ports)
        {
            if (!port.Configured) continue;
            if (string.IsNullOrWhiteSpace(port.IoddFileName)) continue;
            bool present = catalog.FindByFileName(port.IoddFileName!) is not null
                || (!string.IsNullOrWhiteSpace(port.ProductId)
                    && catalog.FindByProductId(StripProductId(port.ProductId!)) is not null);
            if (!present) missing.Add(port);
        }
        if (missing.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"[Error] Missing IODD — STOP. {missing.Count} IO-Link device(s) on " +
                      $"'{cfg.MasterPath}' have no installed device description:");
        foreach (var p in missing)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  • Port {p.Port}: {p.DeviceName ?? "?"}" +
                          $"{(p.VendorName is not null ? $" ({p.VendorName})" : "")} — " +
                          $"expected IODD '{p.IoddFileName}'");
        sb.Append(ObtainHint(catalog.Folder));
        return sb.ToString();
    }

    /// <summary>The "obtain the IODD" instruction block, shared by the report and the check tool.</summary>
    public static string ObtainHint(string folder) =>
        $"  Expected in: {folder}{Environment.NewLine}" +
        $"  Action:      Obtain the IODD from the device manufacturer or IODDfinder " +
        $"(https://ioddfinder.io-link.com), then register it with twincat_import_iodd.{Environment.NewLine}" +
        $"               Do NOT configure or link the device until its IODD is present.";

    // "8000870 VABX-A-P-EL-E12-IOS" -> "VABX-A-P-EL-E12-IOS" (drop a leading order-number token).
    private static string StripProductId(string productId)
    {
        string s = productId.Trim();
        int sp = s.IndexOf(' ');
        return sp > 0 && s[..sp].All(char.IsDigit) ? s[(sp + 1)..] : s;
    }
}
