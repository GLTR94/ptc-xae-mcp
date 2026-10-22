using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class IoddXmlParserTests
{
    // Minimal but faithful IODD (namespace + DeviceIdentity + DeviceVariant), matching
    // the shape of the Festo/Beckhoff IODDs shipped in TwinCAT's Config\Io\IOLink.
    private const string Iodd = """
        <?xml version="1.0" encoding="UTF-8"?>
        <IODevice xmlns="http://www.io-link.com/IODD/2010/10">
          <ProfileBody>
            <DeviceIdentity vendorId="333" vendorName="Festo SE &amp; Co. KG" deviceId="65794">
              <DeviceName textId="TI_Name"/>
              <DeviceVariantCollection>
                <DeviceVariant productId="VABX-A-P-EL-E12-IOS" deviceIcon="i.png"><Name textId="TI_P1"/></DeviceVariant>
                <DeviceVariant productId="VABX-A-P-EL-E12-IOL" deviceIcon="j.png"><Name textId="TI_P2"/></DeviceVariant>
              </DeviceVariantCollection>
            </DeviceIdentity>
          </ProfileBody>
        </IODevice>
        """;

    [Fact]
    public void ParseIdentity_ReadsVendorDeviceAndProducts()
    {
        var id = IoddXmlParser.ParseIdentity(Iodd);

        Assert.Equal(333, id.VendorId);
        Assert.Equal(65794, id.DeviceId);
        Assert.Equal("Festo SE & Co. KG", id.VendorName);
        Assert.Equal(["VABX-A-P-EL-E12-IOS", "VABX-A-P-EL-E12-IOL"], id.ProductIds);
    }

    [Fact]
    public void ParseIdentity_Empty_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => IoddXmlParser.ParseIdentity("  "));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseIdentity_Malformed_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => IoddXmlParser.ParseIdentity("<IODevice>"));
        Assert.Contains("not well-formed", ex.Message);
    }

    [Fact]
    public void ParseIdentity_NotAnIodd_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => IoddXmlParser.ParseIdentity("<root><nope/></root>"));
        Assert.Contains("DeviceIdentity", ex.Message);
    }
}

public class IoddImportCoordinatorTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "iodd-test-" + Guid.NewGuid().ToString("N"));

    private const string Iodd = """
        <?xml version="1.0"?>
        <IODevice xmlns="http://www.io-link.com/IODD/2010/10">
          <ProfileBody><DeviceIdentity vendorId="333" vendorName="Festo" deviceId="65794">
            <DeviceVariantCollection><DeviceVariant productId="VABX-A-P-EL-E12-IOS"/></DeviceVariantCollection>
          </DeviceIdentity></ProfileBody>
        </IODevice>
        """;

    private string WriteSource(string name = "Festo-VABX.xml", string content = Iodd)
    {
        Directory.CreateDirectory(_tmp);
        string p = Path.Combine(_tmp, name);
        File.WriteAllText(p, content);
        return p;
    }

    private string TargetFolder()
    {
        string f = Path.Combine(_tmp, "IOLink");
        Directory.CreateDirectory(f);
        return f;
    }

    [Fact]
    public void ImportIodd_MissingSource_ReturnsError()
    {
        var result = new IoddImportCoordinator().ImportIodd(Path.Combine(_tmp, "nope.xml"), TargetFolder());

        Assert.StartsWith("[Error]", result);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ImportIodd_EmptySource_ReturnsError(string src)
    {
        var result = new IoddImportCoordinator().ImportIodd(src, TargetFolder());
        Assert.StartsWith("[Error]", result);
        Assert.Contains("required", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportIodd_HappyPath_CopiesAndReportsIdentity()
    {
        string src = WriteSource();
        string folder = TargetFolder();

        var result = new IoddImportCoordinator().ImportIodd(src, folder);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("Registered", result);
        Assert.Contains("vendorId 333", result);
        Assert.Contains("deviceId 65794", result);
        Assert.Contains("VABX-A-P-EL-E12-IOS", result);
        Assert.True(File.Exists(Path.Combine(folder, "Festo-VABX.xml")));
    }

    [Fact]
    public void ImportIodd_AlreadyRegisteredIdentical_IsIdempotent()
    {
        string src = WriteSource();
        string folder = TargetFolder();
        new IoddImportCoordinator().ImportIodd(src, folder);

        var result = new IoddImportCoordinator().ImportIodd(src, folder);

        Assert.Contains("already registered", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No change", result);
    }

    [Fact]
    public void ImportIodd_ChangedContent_Updates()
    {
        string src = WriteSource();
        string folder = TargetFolder();
        new IoddImportCoordinator().ImportIodd(src, folder);

        File.WriteAllText(src, Iodd.Replace("deviceId=\"65794\"", "deviceId=\"99\""));
        var result = new IoddImportCoordinator().ImportIodd(src, folder);

        Assert.Contains("Updated", result);
        Assert.Contains("deviceId 99", result);
    }

    [Fact]
    public void ImportIodd_NotAnIodd_ReturnsError()
    {
        string src = WriteSource("bogus.xml", "<root><nope/></root>");

        var result = new IoddImportCoordinator().ImportIodd(src, TargetFolder());

        Assert.StartsWith("[Error]", result);
        Assert.Contains("DeviceIdentity", result);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true); } catch { }
    }
}
