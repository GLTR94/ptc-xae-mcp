using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #148 (B-3 + fresh-tsproj finding) — ManualSelect takes GUIDs only and creates a missing Licenses/Target section.</summary>
public class ManualSelectXmlTests
{
    private const string PouPath = "TIPC^Plc^Plc Project^POUs^FB_A";

    private static FakeTwinCatSession OpenFake() => new() { IsConnected = true, SolutionOpen = true };

    // =====================================================================
    // ManualSelect (B-3 + fresh-tsproj finding)
    // =====================================================================

    private const string Guid1 = "{854588BA-345B-43F2-9354-0CF7AFF76EA3}";
    private const string Guid2 = "aaaaaaaa-0000-4000-8000-000000000001";

    // Shape of a fresh project (the neutral fixture): <System> without <Licenses>.
    private const string FreshTsproj =
        "<?xml version=\"1.0\"?>\r\n<TcSmProject TcSmVersion=\"1.0\">\r\n\t<Project ProjectGUID=\"{29DB4968-A3F0-4895-952E-A43B9E6AEE2F}\">\r\n" +
        "\t\t<System>\r\n\t\t\t<Tasks>\r\n\t\t\t\t<Task Id=\"3\">\r\n\t\t\t\t\t<Name>PlcTask</Name>\r\n\t\t\t\t</Task>\r\n\t\t\t</Tasks>\r\n\t\t</System>\r\n" +
        "\t\t<Plc>\r\n\t\t\t<Project Name=\"FixturePlc\"/>\r\n\t\t</Plc>\r\n\t</Project>\r\n</TcSmProject>\r\n";

    private const string ExistingTargetTsproj =
        "<?xml version=\"1.0\"?>\n<TcSmProject>\n\t<Project>\n\t\t<System>\n\t\t\t<Settings>\n\t\t\t\t<IoIdleTask Priority=\"6\"/>\n\t\t\t</Settings>\n" +
        "\t\t\t<Licenses>\n\t\t\t\t<Target>\n\t\t\t\t\t<ManualSelect>" + Guid1 + "</ManualSelect>\n" +
        "\t\t\t\t\t<TargetSelect TargetId=\"2\">" + Guid1 + "</TargetSelect>\n\t\t\t\t</Target>\n\t\t\t</Licenses>\n" +
        "\t\t\t<Tasks/>\n\t\t</System>\n\t</Project>\n</TcSmProject>\n";

    private static XElement LicensesTarget(string xml) =>
        XDocument.Parse(xml).Root!.Element("Project")!.Element("System")!.Element("Licenses")!.Element("Target")!;

    [Fact]
    public void ManualSelect_FreshTsproj_CreatesSection_InSchemaPosition()
    {
        string xml = ManualSelectXml.Add(FreshTsproj, Guid2);

        var system = XDocument.Parse(xml).Root!.Element("Project")!.Element("System")!;
        Assert.Equal(["Licenses", "Tasks"], system.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("{AAAAAAAA-0000-4000-8000-000000000001}", LicensesTarget(xml).Element("ManualSelect")!.Value);
        Assert.True(ManualSelectXml.Contains(ManualSelectXml.Parse(xml), Guid2, null));
        Assert.Contains("\t\t\t<Licenses>\r\n\t\t\t\t<Target>\r\n\t\t\t\t\t<ManualSelect>", xml); // formatting kept
        Assert.DoesNotContain("\n<Licenses", xml.Replace("\r\n", "\n").Replace("\n\t", "\n"));
    }

    [Fact]
    public void ManualSelect_SettingsPresent_LicensesGoesAfterSettings()
    {
        const string xml0 = "<TcSmProject><Project><System>\n<Settings>\n<X/>\n</Settings>\n<Tasks/>\n</System></Project></TcSmProject>";
        var system = XDocument.Parse(ManualSelectXml.Add(xml0, Guid2)).Root!.Element("Project")!.Element("System")!;
        Assert.Equal(["Settings", "Licenses", "Tasks"], system.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void ManualSelect_ExistingTarget_InsertsBeforeTargetSelect()
    {
        string xml = ManualSelectXml.Add(ExistingTargetTsproj, Guid2);
        var names = LicensesTarget(xml).Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["ManualSelect", "ManualSelect", "TargetSelect"], names);
        Assert.Equal(2, ManualSelectXml.Parse(xml).Count);
    }

    [Fact]
    public void ManualSelect_LicensesWithoutTarget_CreatesTarget()
    {
        const string xml0 = "<TcSmProject><Project><System>\n<Licenses IgnoreProjectLicenses=\"false\">\n</Licenses>\n</System></Project></TcSmProject>";
        string xml = ManualSelectXml.Add(xml0, Guid2);
        Assert.Single(LicensesTarget(xml).Elements("ManualSelect"));
    }

    [Fact]
    public void ManualSelect_SelfClosingLicenses_IsExpanded()
    {
        const string xml0 = "<TcSmProject><Project><System>\n\t<Licenses/>\n</System></Project></TcSmProject>";
        string xml = ManualSelectXml.Add(xml0, Guid2);
        Assert.Single(LicensesTarget(xml).Elements("ManualSelect"));
    }

    [Fact]
    public void ManualSelect_NoSystem_Throws_NeverReturnsUnchanged()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ManualSelectXml.Add("<TcSmProject><Project/></TcSmProject>", Guid2));
        Assert.Contains("<System>", ex.Message);
    }

    [Theory]
    [InlineData("TC1200")]
    [InlineData("{TC1200}")]
    [InlineData("not-a-guid")]
    public void ManualSelect_NonGuid_Throws(string id)
    {
        Assert.Null(ManualSelectXml.TryFormatGuid(id));
        Assert.Throws<InvalidOperationException>(() => ManualSelectXml.Add(ExistingTargetTsproj, id));
    }

    [Fact]
    public void ManualSelect_GuidFormats_Canonical()
    {
        Assert.Equal("{AAAAAAAA-0000-4000-8000-000000000001}", ManualSelectXml.TryFormatGuid(Guid2));
        Assert.Equal(Guid1, ManualSelectXml.TryFormatGuid(Guid1.ToLowerInvariant()));
    }

    [Fact]
    public void AddRequiredLicense_OrderNoOnly_IsRejected_BeforeSession()
    {
        var fake = OpenFake();
        string r = new BuildCoordinator(fake).AddRequiredLicense(null, "TC1200");
        Assert.StartsWith("[Error] orderNo 'TC1200' alone cannot be added", r);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddRequiredLicense_NonGuidLicenseId_IsRejected_BeforeSession()
    {
        var fake = OpenFake();
        string r = new BuildCoordinator(fake).AddRequiredLicense("TC1200", null);
        Assert.StartsWith("[Error] licenseId 'TC1200' is not a GUID", r);
        Assert.Empty(fake.CallLog);
    }
}
