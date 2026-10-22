using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class IoddPresenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "iodd-cat-" + Guid.NewGuid().ToString("N"));

    private static string Iodd(int vendorId, int deviceId, string vendor, params string[] productIds)
    {
        string variants = string.Join("", productIds.Select(p => $"<DeviceVariant productId=\"{p}\"/>"));
        return $"""
            <?xml version="1.0"?>
            <IODevice xmlns="http://www.io-link.com/IODD/2010/10">
              <ProfileBody><DeviceIdentity vendorId="{vendorId}" vendorName="{vendor}" deviceId="{deviceId}">
                <DeviceVariantCollection>{variants}</DeviceVariantCollection>
              </DeviceIdentity></ProfileBody>
            </IODevice>
            """;
    }

    private string Seed(string fileName, string content)
    {
        Directory.CreateDirectory(_folder);
        string p = Path.Combine(_folder, fileName);
        File.WriteAllText(p, content);
        return p;
    }

    // ---- IoddCatalog ----

    [Fact]
    public void Catalog_Scan_IndexesValidIoddsAndSkipsJunk()
    {
        Seed("Festo-VABX.xml", Iodd(333, 65794, "Festo", "VABX-A-P-EL-E12-IOS"));
        Seed("not-an-iodd.xml", "<root><nope/></root>");
        Seed("broken.xml", "<IODevice>");

        var cat = IoddCatalog.Scan(_folder);

        Assert.Single(cat.Entries);
        Assert.NotNull(cat.FindByProductId("VABX-A-P-EL-E12-IOS"));
        Assert.NotNull(cat.FindByIds(333, 65794));
        Assert.NotNull(cat.FindByFileName("Festo-VABX.xml"));
        Assert.Null(cat.FindByProductId("SOMETHING-ELSE"));
    }

    [Fact]
    public void Catalog_Scan_MissingFolder_IsEmpty()
        => Assert.Empty(IoddCatalog.Scan(Path.Combine(_folder, "does-not-exist")).Entries);

    // ---- IoddPresence.MissingReport (apply pre-flight policy) ----

    private static IoLinkConfig CfgWithDevice(string? ioddFile, string productId = "VABX-A-P-EL-E12-IOS") =>
        new("TIID^...^Term 28 (EL6224)", "EL6224", 1,
        [
            new IoLinkPort(1, true, "VABX-A-P-EL-E12-IOS", "Festo",
                productId, "VTUX", ioddFile, Array.Empty<IoLinkPdoEntry>()),
        ]);

    [Fact]
    public void MissingReport_IoddPresent_ReturnsNull()
    {
        Seed("Festo-VABX.xml", Iodd(333, 65794, "Festo", "VABX-A-P-EL-E12-IOS"));
        var cat = IoddCatalog.Scan(_folder);

        Assert.Null(IoddPresence.MissingReport(CfgWithDevice("Festo-VABX.xml"), cat));
    }

    [Fact]
    public void MissingReport_IoddMissing_ReturnsStopReport()
    {
        var cat = IoddCatalog.Scan(_folder);   // empty folder

        string? report = IoddPresence.MissingReport(CfgWithDevice("Festo-VABX.xml"), cat);

        Assert.NotNull(report);
        Assert.StartsWith("[Error] Missing IODD", report);
        Assert.Contains("Port 1: VABX-A-P-EL-E12-IOS", report);
        Assert.Contains("ioddfinder.io-link.com", report);
        Assert.Contains("twincat_import_iodd", report);
    }

    [Fact]
    public void MissingReport_MatchesByProductId_WhenFilenameDiffers()
    {
        // Installed under a different filename, but productId matches "8000870 VABX-...".
        Seed("whatever.xml", Iodd(333, 65794, "Festo", "VABX-A-P-EL-E12-IOS"));
        var cat = IoddCatalog.Scan(_folder);

        Assert.Null(IoddPresence.MissingReport(
            CfgWithDevice("some-other-name.xml", productId: "8000870 VABX-A-P-EL-E12-IOS"), cat));
    }

    [Fact]
    public void MissingReport_UnconfiguredPorts_AreIgnored()
    {
        var cfg = new IoLinkConfig("m", "EL6224", 2,
        [
            new IoLinkPort(1, false, null, null, null, null, null, Array.Empty<IoLinkPdoEntry>()),
            new IoLinkPort(2, false, null, null, null, null, null, Array.Empty<IoLinkPdoEntry>()),
        ]);

        Assert.Null(IoddPresence.MissingReport(cfg, IoddCatalog.Scan(_folder)));
    }

    // ---- CheckPresent (MCP tool) ----

    [Fact]
    public void CheckPresent_Present_ReportsFile()
    {
        Seed("Festo-VABX.xml", Iodd(333, 65794, "Festo", "VABX-A-P-EL-E12-IOS"));

        var result = new IoddImportCoordinator()
            .CheckPresent("VABX-A-P-EL-E12-IOS", null, null, null, _folder);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("IODD present", result);
        Assert.Contains("Festo-VABX.xml", result);
    }

    [Fact]
    public void CheckPresent_Missing_ReturnsStopReportWithObtainHint()
    {
        Directory.CreateDirectory(_folder);

        var result = new IoddImportCoordinator()
            .CheckPresent("VABX-A-P-EL-E12-IOS", null, null, null, _folder);

        Assert.StartsWith("[Error] Missing IODD", result);
        Assert.Contains("productId 'VABX-A-P-EL-E12-IOS'", result);
        Assert.Contains(_folder, result);
        Assert.Contains("Obtain the IODD", result);
    }

    [Fact]
    public void CheckPresent_ByVendorDeviceId_Works()
    {
        Seed("dev.xml", Iodd(333, 65794, "Festo", "VABX-A-P-EL-E12-IOS"));

        var result = new IoddImportCoordinator().CheckPresent(null, 333, 65794, null, _folder);

        Assert.Contains("IODD present", result);
    }

    [Fact]
    public void CheckPresent_NoSelector_ReturnsError()
    {
        var result = new IoddImportCoordinator().CheckPresent(null, null, null, null, _folder);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("specify", result, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
    }
}
