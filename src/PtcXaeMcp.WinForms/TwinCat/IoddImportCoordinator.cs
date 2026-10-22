namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>Resolves TwinCAT's IODD library folder (<c>…\Config\Io\IOLink</c>).</summary>
internal static class IoddFolder
{
    /// <summary>Candidate IODD folders in priority order; first existing one wins.</summary>
    public static IEnumerable<string> Candidates()
    {
        // Single source of truth for install-dir resolution (issue #157 item 9:
        // registry → %TwinCAT3Dir% → default) instead of a second duplicate
        // %TwinCAT3Dir% lookup here.
        string? installDir = TwinCatPaths.GetInstallDir();
        if (installDir is not null)
            yield return Path.Combine(installDir, "Config", "Io", "IOLink");
        yield return @"C:\Program Files (x86)\Beckhoff\TwinCAT\3.1\Config\Io\IOLink";
        yield return @"C:\TwinCAT\3.1\Config\Io\IOLink";
    }

    public static string? ResolveDefault() =>
        Candidates().FirstOrDefault(Directory.Exists);
}

/// <summary>
/// Registers an IODD file into TwinCAT's IODD folder so the IO-Link master can
/// break the device down into named process data. Pure file operation — it does
/// not require an open solution or a running XAE (it writes the TwinCAT install
/// IODD library, not the project). Mirrors the coordinator error convention:
/// returns <c>[Error] …</c> strings, never throws across the MCP boundary.
/// </summary>
internal sealed class IoddImportCoordinator
{
    /// <param name="sourcePath">Path to the IODD <c>.xml</c> to register.</param>
    /// <param name="targetFolder">Override the IODD folder (tests); null resolves the TwinCAT default.</param>
    public string ImportIodd(string sourcePath, string? targetFolder = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return "[Error] sourcePath is required.";
        if (!File.Exists(sourcePath))
            return $"[Error] IODD file not found: {sourcePath}";

        string xml;
        try { xml = File.ReadAllText(sourcePath); }
        catch (Exception ex) { return $"[Error] cannot read IODD '{sourcePath}': {ex.Message}"; }

        IoddIdentity id;
        try { id = IoddXmlParser.ParseIdentity(xml); }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }

        string? folder = !string.IsNullOrWhiteSpace(targetFolder) ? targetFolder : IoddFolder.ResolveDefault();
        if (string.IsNullOrWhiteSpace(folder))
            return "[Error] could not locate the TwinCAT IODD folder (…\\Config\\Io\\IOLink). " +
                   "Pass targetFolder explicitly.";
        if (!Directory.Exists(folder))
        {
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { return $"[Error] IODD folder '{folder}' missing and not creatable: {ex.Message}"; }
        }

        string dest = Path.Combine(folder, Path.GetFileName(sourcePath));
        bool existed = File.Exists(dest);
        if (existed && FilesEqual(sourcePath, dest))
            return $"IODD already registered: {Path.GetFileName(dest)} ({IdentitySuffix(id)}). No change.";

        try { File.Copy(sourcePath, dest, overwrite: true); }
        catch (Exception ex) { return $"[Error] copy to IODD folder failed: {ex.Message}"; }

        string verb = existed ? "Updated" : "Registered";
        return $"{verb} IODD {Path.GetFileName(dest)} in {folder} ({IdentitySuffix(id)}). " +
               "TwinCAT picks it up on the next IO-Link IODD rescan.";
    }

    /// <summary>
    /// Check whether the IODD for a device is installed. Returns a one-line
    /// "present" message, or a fail-closed <c>[Error] Missing IODD — STOP…</c>
    /// report (mirrors the EtherCAT missing-ESI policy) telling the user to obtain it.
    /// </summary>
    public string CheckPresent(string? productId, int? vendorId, int? deviceId,
        string? ioddFileName, string? targetFolder = null)
    {
        bool hasSelector = !string.IsNullOrWhiteSpace(productId)
            || !string.IsNullOrWhiteSpace(ioddFileName)
            || (vendorId is > 0 && deviceId is > 0);
        if (!hasSelector)
            return "[Error] specify a device to check: productId, ioddFileName, or vendorId+deviceId.";

        string? folder = !string.IsNullOrWhiteSpace(targetFolder) ? targetFolder : IoddFolder.ResolveDefault();
        if (string.IsNullOrWhiteSpace(folder))
            return "[Error] could not locate the TwinCAT IODD folder (…\\Config\\Io\\IOLink). " +
                   "Pass targetFolder explicitly.";

        var catalog = IoddCatalog.Scan(folder);
        IoddCatalogEntry? match =
            (!string.IsNullOrWhiteSpace(ioddFileName) ? catalog.FindByFileName(ioddFileName!) : null)
            ?? (!string.IsNullOrWhiteSpace(productId) ? catalog.FindByProductId(productId!) : null)
            ?? (vendorId is > 0 && deviceId is > 0 ? catalog.FindByIds(vendorId.Value, deviceId.Value) : null);

        if (match is not null)
            return $"IODD present: {match.FileName} ({IdentitySuffix(match.Identity)}) in {folder}.";

        string requested = !string.IsNullOrWhiteSpace(productId) ? $"productId '{productId}'"
            : !string.IsNullOrWhiteSpace(ioddFileName) ? $"file '{ioddFileName}'"
            : $"vendorId {vendorId}/deviceId {deviceId}";
        return $"[Error] Missing IODD — STOP. No installed IODD matches {requested} " +
               $"(scanned {catalog.Entries.Count} IODD(s)).{Environment.NewLine}" +
               IoddPresence.ObtainHint(folder);
    }

    private static string IdentitySuffix(IoddIdentity id)
    {
        string product = id.ProductIds.Count > 0 ? $", product {string.Join("/", id.ProductIds)}" : "";
        string vendor = id.VendorName is not null ? $" {id.VendorName}" : "";
        return $"vendorId {id.VendorId}{vendor}, deviceId {id.DeviceId}{product}";
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch { return false; }
    }
}
