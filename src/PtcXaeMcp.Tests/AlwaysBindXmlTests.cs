using PtcXaeMcp.WinForms.TwinCat.Schema;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Coverage for the pure AlwaysBind/AlwaysLink XML read/mutation logic extracted from
/// NativeTwinCatRunner.SetGvlAlwaysBindTrue / TryReadAlwaysBindState (Hardening 9, step 1).
/// </summary>
public class AlwaysBindXmlTests
{
    [Fact]
    public void TryReadState_EmptyXml_ReturnsNull()
    {
        AlwaysBindXml.TryReadState("").Should().BeNull();
    }

    [Fact]
    public void TryReadState_ElementTextTrue_ReturnsTrue()
    {
        string xml = "<TreeItem><AlwaysBind>true</AlwaysBind></TreeItem>";
        AlwaysBindXml.TryReadState(xml).Should().BeTrue();
    }

    [Fact]
    public void TryReadState_ElementTextFalse_ReturnsFalse()
    {
        string xml = "<TreeItem><AlwaysBind>false</AlwaysBind></TreeItem>";
        AlwaysBindXml.TryReadState(xml).Should().BeFalse();
    }

    [Fact]
    public void TryReadState_NameValueAttributePairTrue_ReturnsTrue()
    {
        string xml = "<TreeItem><Prop Name=\"AlwaysLink\" Value=\"true\"/></TreeItem>";
        AlwaysBindXml.TryReadState(xml).Should().BeTrue();
    }

    [Fact]
    public void TryReadState_NameValueChildElementPair_ReturnsValue()
    {
        string xml = "<TreeItem><Prop><Name>Immer binden</Name><Value>1</Value></Prop></TreeItem>";
        AlwaysBindXml.TryReadState(xml).Should().BeTrue();
    }

    [Fact]
    public void TryReadState_FieldNotPresent_ReturnsNull()
    {
        string xml = "<TreeItem><SomethingElse>x</SomethingElse></TreeItem>";
        AlwaysBindXml.TryReadState(xml).Should().BeNull();
    }

    [Fact]
    public void Apply_ElementAlreadyTrue_NoChange()
    {
        string xml = "<TreeItem><AlwaysBind>true</AlwaysBind></TreeItem>";
        var result = AlwaysBindXml.Apply(xml);
        result.Changed.Should().BeFalse();
    }

    [Fact]
    public void Apply_ElementFalse_SetsToTrueAndReportsChanged()
    {
        string xml = "<TreeItem><AlwaysBind>false</AlwaysBind></TreeItem>";
        var result = AlwaysBindXml.Apply(xml);

        result.Changed.Should().BeTrue();
        AlwaysBindXml.TryReadState(result.Xml).Should().BeTrue();
    }

    [Fact]
    public void Apply_NameAttributeWithoutValueAttribute_AddsValueAttributeTrue()
    {
        string xml = "<TreeItem><Prop Name=\"AlwaysBind\"/></TreeItem>";
        var result = AlwaysBindXml.Apply(xml);

        result.Changed.Should().BeTrue();
        result.Xml.Should().Contain("Value=\"true\"");
    }

    [Fact]
    public void Apply_NameValueChildPairFalse_SetsValueTextTrue()
    {
        string xml = "<TreeItem><Prop><Name>AlwaysLink</Name><Value>false</Value></Prop></TreeItem>";
        var result = AlwaysBindXml.Apply(xml);

        result.Changed.Should().BeTrue();
        AlwaysBindXml.TryReadState(result.Xml).Should().BeTrue();
    }

    [Fact]
    public void Apply_NoAlwaysBindFieldAnywhere_NoChange()
    {
        string xml = "<TreeItem><Foo>bar</Foo></TreeItem>";
        var result = AlwaysBindXml.Apply(xml);
        result.Changed.Should().BeFalse();
    }

    [Theory]
    [InlineData("AlwaysBind", true)]
    [InlineData("AlwaysLink", true)]
    [InlineData("LinkAlways", true)]
    [InlineData("Immer binden", true)]
    [InlineData("Immer Binden", true)]
    [InlineData("SomethingElse", false)]
    [InlineData(null, false)]
    public void IsAlwaysBindSemanticName_MatchesKnownVariants(string? name, bool expected)
    {
        AlwaysBindXml.IsAlwaysBindSemanticName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void IsBooleanTrueText_MatchesKnownVariants(string? value, bool expected)
    {
        AlwaysBindXml.IsBooleanTrueText(value).Should().Be(expected);
    }
}
