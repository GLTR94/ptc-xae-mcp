using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class PlcItemTypeTests
{
    [Theory]
    [InlineData("ST",  1)]
    [InlineData("st",  1)]
    [InlineData(" ST ", 1)]
    [InlineData("IL",  2)]
    [InlineData("SFC", 3)]
    [InlineData("FBD", 4)]
    [InlineData("CFC", 5)]
    [InlineData("LD",  6)]
    public void ToCode_KnownLanguages_ReturnsExpected(string input, int expected)
    {
        Assert.Equal(expected, PlcImplLanguage.ToCode(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ToCode_Empty_DefaultsToSt(string? input)
    {
        Assert.Equal(PlcImplLanguage.St, PlcImplLanguage.ToCode(input));
        Assert.Null(PlcImplLanguage.Validate(input));
        Assert.Equal("ST", PlcImplLanguage.Normalize(input));
    }

    // Issue #148 (LIVE-9 / B-8): "FDB" (typo for FBD) silently became ST.
    [Theory]
    [InlineData("FDB")]
    [InlineData("Unknown")]
    [InlineData("Pascal")]
    public void Unknown_IsAnError_NeverSilentlySt(string input)
    {
        Assert.Throws<ArgumentException>(() => PlcImplLanguage.ToCode(input));
        Assert.False(PlcImplLanguage.TryToCode(input, out _));
        string? err = PlcImplLanguage.Validate(input);
        Assert.NotNull(err);
        Assert.StartsWith($"[Error] Unknown implLanguage '{input}'. Expected ", err);
        Assert.Contains("FBD", err);
    }

    [Theory]
    [InlineData(1, "ST")]
    [InlineData(2, "IL")]
    [InlineData(3, "SFC")]
    [InlineData(4, "FBD")]
    [InlineData(5, "CFC")]
    [InlineData(6, "LD")]
    [InlineData(999, "ST")]
    public void FromCode_RoundTripsKnownCodes_AndDefaultsUnknownToSt(int code, string expected)
    {
        Assert.Equal(expected, PlcImplLanguage.FromCode(code));
    }

    [Fact]
    public void Constants_MatchInfosysTreeItemTypes()
    {
        // Pin the Infosys-published values so an accidental edit fails loudly.
        Assert.Equal(601, PlcItemType.Folder);
        Assert.Equal(602, PlcItemType.PouProgram);
        Assert.Equal(603, PlcItemType.PouFunction);
        Assert.Equal(604, PlcItemType.PouFunctionBlock);
        Assert.Equal(605, PlcItemType.Dut);
        Assert.Equal(615, PlcItemType.Gvl);
        // 629 is a Parameter List (PLCGVLPARAMLIST), not a persistent-variables list.
        Assert.Equal(629, PlcItemType.GvlParamList);
    }
}
