using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// These tests drive VariantConfigParser directly with the actual XML format that
// TwinCAT's ProjectVariantConfig property emits — child <Name> elements, NOT attributes.
// The original parser bug (using e.Attribute("Name")) would make every test here fail.
public class VariantConfigParserTests
{
    // Actual XML captured from a live TwinCAT project (100004_SampleInterface)
    private const string RealBeckhoffXml =
        """<?xml version="1.0"?><ProjectVariants><Variant><Name>Original</Name></Variant><Variant><Name>Simulation</Name></Variant></ProjectVariants>""";

    [Fact]
    public void Parse_RealBeckhoffXml_ReturnsBothVariants()
    {
        var (variants, groups) = VariantConfigParser.Parse(RealBeckhoffXml);

        Assert.Equal(2, variants.Count);
        Assert.Contains("Original", variants);
        Assert.Contains("Simulation", variants);
        Assert.Empty(groups);
    }

    [Fact]
    public void Parse_AttributeFormat_StillWorks()
    {
        // Forward-compat: fall back to Name="..." attribute if no child element
        const string xml = """<ProjectVariants><Variant Name="PROD"/><Variant Name="SIM"/></ProjectVariants>""";
        var (variants, _) = VariantConfigParser.Parse(xml);

        Assert.Equal(2, variants.Count);
        Assert.Contains("PROD", variants);
        Assert.Contains("SIM", variants);
    }

    [Fact]
    public void Parse_EmptyXml_ReturnsEmptyLists()
    {
        var (variants, groups) = VariantConfigParser.Parse("<ProjectVariants/>");

        Assert.Empty(variants);
        Assert.Empty(groups);
    }

    [Fact]
    public void Parse_WithVariantGroup_ChildElementFormat_ParsesGroupAndMembers()
    {
        const string xml = """
            <ProjectVariants>
              <Variant><Name>Original</Name></Variant>
              <Variant><Name>Simulation</Name></Variant>
              <VariantGroup>
                <Name>AllSim</Name>
                <Variant><Name>Simulation</Name></Variant>
              </VariantGroup>
            </ProjectVariants>
            """;

        var (variants, groups) = VariantConfigParser.Parse(xml);

        Assert.Equal(2, variants.Count);
        Assert.Single(groups);
        Assert.Equal("AllSim", groups[0].Name);
        Assert.Contains("Simulation", groups[0].Members);
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsEmptyLists()
    {
        var (variants, groups) = VariantConfigParser.Parse("not xml at all <<<");

        Assert.Empty(variants);
        Assert.Empty(groups);
    }
}
