using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class IoLinkXmlParserTests
{
    // Faithful miniature of a real EL6224 ProduceXml(false) (project 100001):
    // <Xml> wraps the IO-Link <Port> slots and the RxPdo/TxPdo maps. Port 1 is a
    // configured Festo VABX; port 2 is an empty <Port/>. The "DeviceState Inputs"
    // TxPdo is NOT port-bound and must be ignored; "IO-Link Port1" PDOs are.
    private const string El6224Xml = """
        <TreeItem>
          <ItemName>Klemme 28 (EL6224)</ItemName>
          <PathName>TIID^Device 1 (EtherCAT)^+CAB1-A1 (EK1200)^Term 28 (EL6224)</PathName>
          <ItemSubTypeName>EL6224 (IO Link Master)</ItemSubTypeName>
          <Xml>
            <Port>
              <FileName>Festo-VABX-A-P-EL-20230815-IODD1.1.xml</FileName>
              <VendorName>Festo</VendorName>
              <DeviceName>VABX-A-P-EL-E12-IOS</DeviceName>
              <ProductId>8000870 VABX-A-P-EL-E12-IOS</ProductId>
              <DeviceFamilyName>VTUX</DeviceFamilyName>
            </Port>
            <Port/>
            <TxPdo Fixed="true" Sm="3">
              <Index>#x1a04</Index><Name>DeviceState Inputs</Name>
              <Entry><Index>#xf100</Index><SubIndex>1</SubIndex><BitLen>8</BitLen><Name>State Ch1</Name><DataType>USINT</DataType></Entry>
            </TxPdo>
            <TxPdo Sm="3">
              <Index>#x1a00</Index><Name>IO-Link Port1</Name>
              <Entry><Index>#xf101</Index><SubIndex>1</SubIndex><BitLen>8</BitLen><Name>Device status Port1</Name><DataType>USINT</DataType></Entry>
            </TxPdo>
            <RxPdo Sm="2">
              <Index>#x1600</Index><Name>IO-Link Port1</Name>
              <Entry><Index>#x7000</Index><SubIndex>1</SubIndex><BitLen>1</BitLen><Name>Valve 1 solenoid 00</Name><DataType>BIT</DataType></Entry>
              <Entry><Index>#x7000</Index><SubIndex>2</SubIndex><BitLen>1</BitLen><Name>Valve 1 solenoid 01</Name><DataType>BIT</DataType></Entry>
              <Entry><Index>#x7000</Index><SubIndex>3</SubIndex><BitLen>1</BitLen><Name/><DataType>BIT</DataType></Entry>
            </RxPdo>
          </Xml>
        </TreeItem>
        """;

    [Fact]
    public void Parse_El6224_FindsTwoPortsAndMasterType()
    {
        var cfg = IoLinkXmlParser.Parse("TIID^...^Term 28 (EL6224)", El6224Xml);

        Assert.Equal("EL6224", cfg.MasterType);
        Assert.Equal(2, cfg.PortCount);
        Assert.Equal(2, cfg.Ports.Count);
        Assert.Equal(1, cfg.Ports[0].Port);
        Assert.Equal(2, cfg.Ports[1].Port);
    }

    [Fact]
    public void Parse_Port1_CarriesDeviceIdentity()
    {
        var p1 = IoLinkXmlParser.Parse("m", El6224Xml).Ports[0];

        Assert.True(p1.Configured);
        Assert.Equal("VABX-A-P-EL-E12-IOS", p1.DeviceName);
        Assert.Equal("Festo", p1.VendorName);
        Assert.Equal("8000870 VABX-A-P-EL-E12-IOS", p1.ProductId);
        Assert.Equal("VTUX", p1.DeviceFamily);
        Assert.Equal("Festo-VABX-A-P-EL-20230815-IODD1.1.xml", p1.IoddFileName);
    }

    [Fact]
    public void Parse_Port2_IsUnconfigured()
    {
        var p2 = IoLinkXmlParser.Parse("m", El6224Xml).Ports[1];

        Assert.False(p2.Configured);
        Assert.Null(p2.DeviceName);
        Assert.Empty(p2.ProcessData);
    }

    [Fact]
    public void Parse_Port1_ProcessData_OutputsAndInputsButNotDeviceState()
    {
        var p1 = IoLinkXmlParser.Parse("m", El6224Xml).Ports[0];

        // Two named valve outputs (the unnamed padding Entry is dropped) + one named input.
        var outputs = p1.ProcessData.Where(e => e.Direction == "Q").ToList();
        var inputs = p1.ProcessData.Where(e => e.Direction == "I").ToList();

        Assert.Equal(["Valve 1 solenoid 00", "Valve 1 solenoid 01"], outputs.Select(e => e.Name));
        Assert.All(outputs, e => Assert.Equal("BIT", e.DataType));
        Assert.Equal(1, outputs[0].BitLen);
        Assert.Equal("#x7000", outputs[0].Index);
        Assert.Equal(1, outputs[0].SubIndex);

        Assert.Equal(["Device status Port1"], inputs.Select(e => e.Name));
        // "DeviceState Inputs" (State Ch1) is not port-bound → excluded.
        Assert.DoesNotContain(p1.ProcessData, e => e.Name == "State Ch1");
    }

    [Fact]
    public void Parse_NonIoLinkXml_ReturnsZeroPorts()
    {
        var cfg = IoLinkXmlParser.Parse("TIID^Device 1^Box 1 (EL1809)",
            "<TreeItem><ItemSubTypeName>EL1809</ItemSubTypeName><Xml/></TreeItem>");

        Assert.Equal("EL1809", cfg.MasterType);
        Assert.Equal(0, cfg.PortCount);
        Assert.Empty(cfg.Ports);
    }

    [Fact]
    public void Parse_EmptyXml_ReturnsZeroPortsWithTypeFromPath()
    {
        var cfg = IoLinkXmlParser.Parse("TIID^...^Term 28 (EL6224)", "");

        Assert.Equal("EL6224", cfg.MasterType);
        Assert.Equal(0, cfg.PortCount);
    }

    [Fact]
    public void Parse_MalformedXml_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => IoLinkXmlParser.Parse("m", "<TreeItem><Xml></TreeItem>"));
        Assert.Contains("not well-formed", ex.Message);
    }
}
