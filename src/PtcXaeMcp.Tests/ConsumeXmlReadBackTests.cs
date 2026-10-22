using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #152 D-23: configure_ethercat_slave verifies what ConsumeXml actually applied.</summary>
public class ConsumeXmlReadBackTests
{
    private const string Path = "TIID^Device 1 (EtherCAT)^Box 1 (EK1100)";

    [Fact]
    public void SameDocument_HasNoDifferences()
    {
        const string xml = "<TreeItem><EtherCAT><Slave><Info><Name>Box 1</Name><PhysAddr>1001</PhysAddr></Info></Slave></EtherCAT></TreeItem>";

        Assert.Empty(ConsumeXmlReadBack.Differences(xml, xml));
    }

    [Fact]
    public void PartialInput_IsFoundInTheFullReadBack_RegardlessOfSiblingPosition()
    {
        const string written = "<TreeItem><EtherCAT><Slave><InitCmds><InitCmd><Data>0A</Data></InitCmd></InitCmds></Slave></EtherCAT></TreeItem>";
        const string read = "<TreeItem><ItemName>Box 1</ItemName><EtherCAT><Slave><InitCmds>" +
                            "<InitCmd><Data>01</Data></InitCmd><InitCmd><Data>0a</Data></InitCmd>" +
                            "</InitCmds></Slave></EtherCAT></TreeItem>";

        Assert.Empty(ConsumeXmlReadBack.Differences(written, read));
    }

    [Fact]
    public void ChangedValue_IsReported_WithWhatWasRead()
    {
        const string written = "<TreeItem><EtherCAT><Slave><Info><PhysAddr>1005</PhysAddr></Info></Slave></EtherCAT></TreeItem>";
        const string read = "<TreeItem><EtherCAT><Slave><Info><PhysAddr>1001</PhysAddr></Info></Slave></EtherCAT></TreeItem>";

        var d = Assert.Single(ConsumeXmlReadBack.Differences(written, read));
        Assert.Contains("TreeItem/EtherCAT/Slave/Info/PhysAddr", d, StringComparison.Ordinal);
        Assert.Contains("'1005'", d, StringComparison.Ordinal);
        Assert.Contains("'1001'", d, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingElementAndAttribute_AreReported()
    {
        const string written = "<TreeItem><EtherCAT><Slave Enabled=\"false\"><Extra>1</Extra></Slave></EtherCAT></TreeItem>";
        const string read = "<TreeItem><EtherCAT><Slave Enabled=\"true\"/></EtherCAT></TreeItem>";

        var d = ConsumeXmlReadBack.Differences(written, read);
        Assert.Equal(2, d.Count);
        Assert.Contains(d, x => x.Contains("@Enabled", StringComparison.Ordinal));
        Assert.Contains(d, x => x.Contains("Extra", StringComparison.Ordinal) && x.Contains("not present", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("#x1000", "4096")]
    [InlineData("0x1000", "#x1000")]
    [InlineData("1.50", "1.5")]
    [InlineData("TRUE", "true")]
    [InlineData(" a ", "A")]
    public void Values_CompareNormalized(string a, string b) => Assert.True(ConsumeXmlReadBack.SameValue(a, b));

    [Fact]
    public void Coordinator_WriteAppliedAndReadBack_IsSuccess()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.DeviceXmlByPath[Path] = "<TreeItem/>";

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave(Path, "<TreeItem><Name>Box 1</Name></TreeItem>");

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("read back", result, StringComparison.Ordinal);
        Assert.Contains($"GetDeviceXml({Path})", fake.CallLog);
    }

    [Fact]
    public void Coordinator_WriteIgnoredByXae_IsAnError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true, ConfigureIgnoresWrite = true };
        fake.DeviceXmlByPath[Path] = "<TreeItem><Name>Old</Name></TreeItem>";

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave(Path, "<TreeItem><Name>New</Name></TreeItem>");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("did not read back", result, StringComparison.Ordinal);
        Assert.Contains("'New'", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinator_ReadBackFails_IsAnError_ThatSaysTheWriteRan()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.DeviceXmlByPath[Path] = "<TreeItem/>";
        fake.GetDeviceXmlThrows = new InvalidOperationException("ProduceXml failed");

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave(Path, "<TreeItem><Name>X</Name></TreeItem>");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("project was saved", result, StringComparison.Ordinal);
        Assert.Contains("read-back failed", result, StringComparison.Ordinal);
    }
}
