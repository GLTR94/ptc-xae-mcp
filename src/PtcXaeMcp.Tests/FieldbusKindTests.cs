using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Classifier guards for Beckhoff terminal prefixes. The ED-series cases pin the
/// behaviour added when Beckhoff introduced EDxxxx terminals alongside ELxxxx:
/// ED terminals are standard ring I/O (shown as a box when they have channels) and
/// are NOT couplers, so they must never consume a downstream EtherCAT port letter.
/// </summary>
public class FieldbusKindTests
{
    [Theory]
    [InlineData("Term 5 (ED3174)")]   // new ED-series terminal — regression guard
    [InlineData("Box 1 (EK1100)")]
    [InlineData("Term 7 (EL2809)")]
    [InlineData("Box 9 (EP1008-0001)")]
    [InlineData("Box 1")]
    public void LooksLikeSegmentMaster_KnownPrefixes_True(string name) =>
        Assert.True(FieldbusKind.LooksLikeSegmentMaster(name));

    [Theory]
    [InlineData("")]
    [InlineData("Channel 1")]
    [InlineData("Some Drive (AX5206)")]
    public void LooksLikeSegmentMaster_NonSegmentMaster_False(string name) =>
        Assert.False(FieldbusKind.LooksLikeSegmentMaster(name));

    [Fact]
    public void LooksLikeEtherCatCoupler_EkCoupler_True() =>
        Assert.True(FieldbusKind.LooksLikeEtherCatCoupler("Box 1 (EK1100)"));

    [Theory]
    [InlineData("Term 5 (ED3174)")]   // ED rides the ring like EL — must NOT be a coupler
    [InlineData("Term 7 (EL2809)")]
    [InlineData("Box 9 (EP1008-0001)")]
    public void LooksLikeEtherCatCoupler_NonCoupler_False(string name) =>
        Assert.False(FieldbusKind.LooksLikeEtherCatCoupler(name));

    [Theory]
    [InlineData("Term 5 (ED3174)")]   // ED classifies as box, same as EL below
    [InlineData("Term 7 (EL3174)")]
    public void Classify_TerminalWithChildren_IsBox(string name)
    {
        string kind = FieldbusKind.Classify(
            name, itemType: 0, itemSubType: 0, depth: 2, childCount: 2, hasProduceXml: true);

        Assert.Equal(FieldbusKind.Box, kind);
    }

    [Fact]
    public void Classify_TerminalWithoutChildren_IsChannel()
    {
        string kind = FieldbusKind.Classify(
            "Term 5 (ED3174)", itemType: 0, itemSubType: 0, depth: 3, childCount: 0, hasProduceXml: true);

        Assert.Equal(FieldbusKind.Channel, kind);
    }
}
