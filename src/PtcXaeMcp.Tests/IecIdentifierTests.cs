using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// Issue #157 item 7: IecIdentifier had no unit coverage of its own (the reserved-word
// check it exists for — issue #19 — was only exercised end-to-end via a live test).
public class IecIdentifierTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingName_ReturnsError(string? name)
    {
        string? result = IecIdentifier.Validate(name, "METHOD name");
        Assert.NotNull(result);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("required", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1Foo")]
    [InlineData("Foo-Bar")]
    [InlineData("Foo Bar")]
    [InlineData("Foo.Bar")]
    public void Validate_BadSyntax_ReturnsError(string name)
    {
        string? result = IecIdentifier.Validate(name, "METHOD name");
        Assert.NotNull(result);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("valid IEC 61131-3 identifier", result);
    }

    [Theory]
    [InlineData("CALC")]
    [InlineData("calc")]
    [InlineData("ADD")]
    [InlineData("IF")]
    [InlineData("BOOL")]
    [InlineData("VAR_INPUT")]
    public void Validate_ReservedWord_ReturnsError(string name)
    {
        // The issue-#19 culprits: TC3's own error ("Name mismatch") is misleading,
        // so this pre-check exists to name the real problem before it reaches XAE.
        string? result = IecIdentifier.Validate(name, "METHOD name");
        Assert.NotNull(result);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("reserved IEC 61131-3 word", result);
    }

    [Theory]
    [InlineData("DoThing")]
    [InlineData("_private")]
    [InlineData("Foo123")]
    [InlineData("CALCULATE")] // shares a prefix with the reserved CALC but is distinct
    public void Validate_ValidName_ReturnsNull(string name)
    {
        Assert.Null(IecIdentifier.Validate(name, "METHOD name"));
    }

    [Fact]
    public void Validate_TrimsSurroundingWhitespace()
    {
        Assert.Null(IecIdentifier.Validate("  DoThing  ", "METHOD name"));
        Assert.NotNull(IecIdentifier.Validate("  CALC  ", "METHOD name"));
    }

    [Fact]
    public void Validate_ErrorMessage_NamesTheOffendingValueAndWhat()
    {
        string? result = IecIdentifier.Validate("CALC", "METHOD name");
        Assert.Contains("METHOD name", result);
        Assert.Contains("CALC", result);
    }
}
