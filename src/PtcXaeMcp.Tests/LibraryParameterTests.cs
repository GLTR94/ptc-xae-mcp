using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class LibraryParameterTests
{
    private static LibraryReference Ref(string placeholder, string name) =>
        new(
            Name: name,
            Version: "*",
            Publisher: "Pneutronic AG",
            Placeholder: placeholder,
            EffectiveResolution: $"{name}, * (Pneutronic AG)",
            Source: LibrarySource.Xae,
            FilePinnedVersion: null,
            VersionMatchesFile: null);

    private static FakeTwinCatSession Session()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.LibraryReferences.Add(Ref("Sample-Library", "PTC_Library"));
        return fake;
    }

    [Fact]
    public void GetParameters_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var (summary, pars) = new LibraryCoordinator(fake).GetParameters("Sample-Library");
        Assert.StartsWith("[Error]", summary);
        Assert.Empty(pars);
    }

    [Fact]
    public void GetParameters_UnknownLibrary_ReturnsError()
    {
        var (summary, pars) = new LibraryCoordinator(Session()).GetParameters("Nope");
        Assert.StartsWith("[Error]", summary);
        Assert.Empty(pars);
    }

    [Fact]
    public void GetParameters_NoOverrides_ReportsDefaults()
    {
        var (summary, pars) = new LibraryCoordinator(Session()).GetParameters("Sample-Library");
        Assert.Contains("library defaults apply", summary);
        Assert.Empty(pars);
    }

    [Fact]
    public void GetParameters_MatchesByLibraryNameToo()
    {
        var fake = Session();
        fake.LibraryParameters.Add(new LibraryParameter("Sample-Library", "GVL_PARAMETER", "NUMBER_OF_GROUPS", "3"));
        // Lookup by resolved library name instead of the placeholder name.
        var (summary, pars) = new LibraryCoordinator(fake).GetParameters("PTC_Library");
        Assert.Contains("1 parameter override", summary);
        Assert.Equal("3", pars[0].Value);
    }

    [Fact]
    public void SetParameter_AddsNewOverride()
    {
        var fake = Session();
        var (summary, pars) = new LibraryCoordinator(fake)
            .SetParameter("Sample-Library", "GVL_PARAMETER", "NUMBER_OF_ITEMS", "101");
        Assert.Contains("NUMBER_OF_ITEMS = 101", summary);
        Assert.Single(pars);
        Assert.Contains("SetLibraryParameter(Sample-Library,GVL_PARAMETER,NUMBER_OF_ITEMS,101)",
            string.Join(",", fake.CallLog));
    }

    [Fact]
    public void SetParameter_UpdatesExistingOverride()
    {
        var fake = Session();
        fake.LibraryParameters.Add(new LibraryParameter("Sample-Library", "GVL_PARAMETER", "NUMBER_OF_ITEMS", "40"));
        var (_, pars) = new LibraryCoordinator(fake)
            .SetParameter("Sample-Library", "GVL_PARAMETER", "NUMBER_OF_ITEMS", "101");
        Assert.Single(pars);
        Assert.Equal("101", pars[0].Value);
    }

    [Fact]
    public void SetParameter_EmptyKey_ReturnsError()
    {
        var (summary, _) = new LibraryCoordinator(Session())
            .SetParameter("Sample-Library", "GVL_PARAMETER", " ", "1");
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void SetParameter_SessionThrows_SurfacesError()
    {
        var fake = Session();
        fake.SetLibraryParameterThrows = new InvalidOperationException("ConsumeXml rejected <Parameters>");
        var (summary, _) = new LibraryCoordinator(fake)
            .SetParameter("Sample-Library", "GVL_PARAMETER", "NUMBER_OF_ITEMS", "101");
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("ConsumeXml rejected", summary);
    }

    // TreeItem schema as produced by XAE (TREEITEMTYPE_PLCLIBPLACEHOLDER, live-verified):
    // <PlcLibPlaceholder><PlaceholderReference id="..."><ParameterList>
    //   <Parameter><Name>LIST.KEY</Name><Value>V</Value></Parameter>
    [Fact]
    public void UpsertParameterXml_AddsParameterListEntry()
    {
        string xml = "<TreeItem><PlcLibPlaceholder><PlaceholderReference id=\"1\">" +
                     "<PlaceholderName>PTC</PlaceholderName></PlaceholderReference></PlcLibPlaceholder></TreeItem>";
        string updated = LibraryParsing.UpsertParameterInReferenceXml(xml, "GVL_PARAMETER", "NUMBER_OF_ITEMS", "101");
        Assert.Contains("<Name>GVL_PARAMETER.NUMBER_OF_ITEMS</Name>", updated);
        Assert.Contains("<Value>101</Value>", updated);
    }

    [Fact]
    public void UpsertParameterXml_UpdatesExistingValue()
    {
        string xml = "<TreeItem><PlcLibPlaceholder><PlaceholderReference id=\"1\"><PlaceholderName>PTC</PlaceholderName>" +
                     "<ParameterList><Parameter><Name>GVL_PARAMETER.NUMBER_OF_ITEMS</Name><Value>40</Value></Parameter></ParameterList>" +
                     "</PlaceholderReference></PlcLibPlaceholder></TreeItem>";
        string updated = LibraryParsing.UpsertParameterInReferenceXml(xml, "GVL_PARAMETER", "NUMBER_OF_ITEMS", "101");
        Assert.Contains("<Value>101</Value>", updated);
        Assert.DoesNotContain("<Value>40</Value>", updated);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(updated, "NUMBER_OF_ITEMS"));
    }

    [Fact]
    public void UpsertParameterXml_NoPlaceholder_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LibraryParsing.UpsertParameterInReferenceXml("<TreeItem/>", "L", "K", "V"));
    }

    [Fact]
    public void GetParameters_FallsBackToLibraryName_WhenPlaceholderDiffers()
    {
        // XAE placeholder "PTC" vs .plcproj Include "Sample-Library" (real-world 100000 case).
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.LibraryReferences.Add(new LibraryReference(
            Name: "Sample-Library", Version: "*", Publisher: "Pneutronic AG",
            Placeholder: "PTC", EffectiveResolution: "Sample-Library, * (Pneutronic AG)",
            Source: LibrarySource.Xae, FilePinnedVersion: null, VersionMatchesFile: null));
        fake.LibraryParameters.Add(new LibraryParameter("Sample-Library", "GVL_PARAMETER", "NUMBER_OF_ITEMS", "101"));

        var (summary, pars) = new LibraryCoordinator(fake).GetParameters("Sample-Library");

        Assert.Contains("1 parameter override", summary);
        Assert.Equal("101", pars[0].Value);
    }
}
