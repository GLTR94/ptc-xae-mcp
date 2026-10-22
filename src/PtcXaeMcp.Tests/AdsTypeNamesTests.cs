using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// D-1 (issue #152): NormalizeTypeName/TryGetStringArrayCount used to live inside the
// #if TWINCAT region of TwinCatComSession.AdsVariable.cs and could only be exercised via a
// live ADS symbol read. Moved to AdsTypeNames so the string logic — including the
// namespace-strip regression on array element types — is unit-testable without TwinCAT.
public class AdsTypeNamesTests
{
    [Theory]
    [InlineData("STRING(80)", "STRING")]
    [InlineData("string(80)", "STRING")]
    [InlineData("BOOL", "BOOL")]
    [InlineData("  INT  ", "INT")]
    public void NormalizeTypeName_StripsSizeQualifierAndUpperCases(string input, string expected)
    {
        Assert.Equal(expected, AdsTypeNames.NormalizeTypeName(input));
    }

    [Theory]
    [InlineData("TC2_SYSTEM.T_MAXSTRING", "STRING")]
    [InlineData("Tc2_System.T_STRING", "STRING")]
    [InlineData("TC2_SYSTEM.T_AMSNETID", "STRING")]
    public void NormalizeTypeName_ResolvesKnownStringAliases(string input, string expected)
    {
        Assert.Equal(expected, AdsTypeNames.NormalizeTypeName(input));
    }

    [Fact]
    public void NormalizeTypeName_StripsNamespaceOfScalarType()
    {
        Assert.Equal("MY_ENUM", AdsTypeNames.NormalizeTypeName("MyLib.MY_ENUM"));
    }

    [Fact]
    public void NormalizeTypeName_PlainArrayOfString_Unaffected()
    {
        Assert.Equal("ARRAY [0..9] OF STRING",
            AdsTypeNames.NormalizeTypeName("ARRAY [0..9] OF STRING(80)"));
    }

    // The regression this method fixes: the previous implementation stripped at the type
    // name's *last* dot, which — for an array of a namespace-qualified element type — is
    // inside the element type, not a namespace prefix on the array itself. That ate the whole
    // "ARRAY[...] OF " prefix, so ArrayOfStringRegex/TryGetStringArrayCount then failed to
    // match at all.
    [Fact]
    public void NormalizeTypeName_ArrayOfNamespaceQualifiedStringAlias_KeepsArrayPrefix()
    {
        string result = AdsTypeNames.NormalizeTypeName("ARRAY [0..9] OF TC2_SYSTEM.T_MAXSTRING");
        Assert.Equal("ARRAY [0..9] OF STRING", result);
    }

    [Fact]
    public void NormalizeTypeName_ArrayOfNamespaceQualifiedNonStringType_StripsOnlyElementNamespace()
    {
        string result = AdsTypeNames.NormalizeTypeName("ARRAY [0..9] OF MyLib.MY_ENUM");
        Assert.Equal("ARRAY [0..9] OF MY_ENUM", result);
    }

    [Theory]
    [InlineData("ARRAY [0..9] OF STRING", 10)]
    [InlineData("ARRAY[0..0] OF STRING", 1)]
    [InlineData("ARRAY [ -2 .. 2 ] OF STRING", 5)]
    public void TryGetStringArrayCount_MatchingArrayOfString_ReturnsElementCount(string plcType, int expected)
    {
        Assert.True(AdsTypeNames.TryGetStringArrayCount(plcType, out int count));
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("STRING")]
    [InlineData("ARRAY [0..9] OF INT")]
    [InlineData("ARRAY [0..-1] OF STRING")] // hi < lo → count <= 0 → false
    public void TryGetStringArrayCount_NonMatchingOrEmpty_ReturnsFalse(string plcType)
    {
        Assert.False(AdsTypeNames.TryGetStringArrayCount(plcType, out _));
    }

    // End-to-end of the regression: after normalization, the array-of-string detector must
    // still recognize a namespace-qualified STRING alias array.
    [Fact]
    public void NormalizeTypeName_ThenTryGetStringArrayCount_ArrayOfQualifiedAlias_Detected()
    {
        string normalized = AdsTypeNames.NormalizeTypeName("ARRAY [0..4] OF TC2_SYSTEM.T_MAXSTRING(80)");
        Assert.True(AdsTypeNames.TryGetStringArrayCount(normalized, out int count));
        Assert.Equal(5, count);
    }
}
