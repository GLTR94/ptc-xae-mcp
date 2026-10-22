using System.Xml;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class CoeStartupParserTests
{
    // Trimmed EL3174 ProduceXml with all DefaultSettings and Fixed entries — no user overrides.
    private const string El3174NoOverrides = """
        <TreeItem>
          <EtherCAT>
            <Slave>
              <Info>
                <Name><![CDATA[EL3174]]></Name>
                <VendorId>2</VendorId>
                <ProductCode>208023634</ProductCode>
                <RevisionNo>1114112</RevisionNo>
                <ProductRevision>EL3174-0000-0017</ProductRevision>
              </Info>
              <ProcessData>
                <AlternativeSmMappings>
                  <AlternativeSmMapping Default="1" Selected="true">
                    <Name>Standard</Name>
                    <OperationMode>
                      <InitCmds CompleteAccess="1" DefaultSettings="1" ChannelNo="1">
                        <Transition>PS</Transition>
                        <Index>#x8000</Index>
                        <SubIndex>1</SubIndex>
                        <Data>000400000000010000000000000000000040</Data>
                      </InitCmds>
                      <InitCmds CompleteAccess="1" DefaultSettings="1" ChannelNo="1">
                        <Transition>PS</Transition>
                        <Index>#x800D</Index>
                        <SubIndex>1</SubIndex>
                        <Data>00000200000000000000000000000080FFFFFF7F0000</Data>
                      </InitCmds>
                    </OperationMode>
                  </AlternativeSmMapping>
                </AlternativeSmMappings>
              </ProcessData>
              <Mailbox>
                <CoE>
                  <InitCmds>
                    <InitCmd Fixed="true">
                      <Transition>PS</Transition>
                      <Comment><![CDATA[clear sm pdos (0x1C12)]]></Comment>
                      <Ccs>1</Ccs>
                      <Index>7186</Index>
                      <SubIndex>0</SubIndex>
                      <Data>00</Data>
                    </InitCmd>
                    <InitCmd Fixed="true">
                      <Transition>PS</Transition>
                      <Comment><![CDATA[clear sm pdos (0x1C13)]]></Comment>
                      <Ccs>1</Ccs>
                      <Index>7187</Index>
                      <SubIndex>0</SubIndex>
                      <Data>00</Data>
                    </InitCmd>
                  </InitCmds>
                </CoE>
              </Mailbox>
            </Slave>
          </EtherCAT>
        </TreeItem>
        """;

    // ProduceXml with a user-added SDO-download override (non-Fixed Mailbox/CoE entry
    // writing 0x800D:11 — Input Type — to value 3 = "IIR level 2").
    private const string El3174WithUserOverride = """
        <TreeItem>
          <EtherCAT>
            <Slave>
              <Info>
                <VendorId>2</VendorId>
                <ProductCode>208023634</ProductCode>
                <RevisionNo>1114112</RevisionNo>
                <ProductRevision>EL3174-0000-0017</ProductRevision>
              </Info>
              <ProcessData>
                <AlternativeSmMappings>
                  <AlternativeSmMapping Default="1" Selected="true">
                    <Name>Standard</Name>
                    <OperationMode>
                      <InitCmds CompleteAccess="1" DefaultSettings="1" ChannelNo="1">
                        <Transition>PS</Transition>
                        <Index>#x8000</Index>
                        <SubIndex>1</SubIndex>
                        <Data>000400000000010000000000000000000040</Data>
                      </InitCmds>
                    </OperationMode>
                  </AlternativeSmMapping>
                </AlternativeSmMappings>
              </ProcessData>
              <Mailbox>
                <CoE>
                  <InitCmds>
                    <InitCmd Fixed="true">
                      <Transition>PS</Transition>
                      <Ccs>1</Ccs>
                      <Index>7186</Index>
                      <SubIndex>0</SubIndex>
                      <Data>00</Data>
                    </InitCmd>
                    <InitCmd>
                      <Transition>PS</Transition>
                      <Comment><![CDATA[AI Advanced Settings Ch.1 — Input Type]]></Comment>
                      <Ccs>1</Ccs>
                      <Index>32781</Index>
                      <SubIndex>17</SubIndex>
                      <Data>0300</Data>
                    </InitCmd>
                  </InitCmds>
                </CoE>
              </Mailbox>
            </Slave>
          </EtherCAT>
        </TreeItem>
        """;

    // ProduceXml with a user-modified OperationMode InitCmds (no DefaultSettings attr).
    private const string El3174WithOpModeOverride = """
        <TreeItem>
          <EtherCAT>
            <Slave>
              <Info>
                <VendorId>2</VendorId>
                <ProductCode>208023634</ProductCode>
                <RevisionNo>1114112</RevisionNo>
                <ProductRevision>EL3174-0000-0017</ProductRevision>
              </Info>
              <ProcessData>
                <AlternativeSmMappings>
                  <AlternativeSmMapping Default="1" Selected="true">
                    <Name>Custom</Name>
                    <OperationMode>
                      <InitCmds CompleteAccess="1" ChannelNo="1">
                        <Transition>PS</Transition>
                        <Index>#x800D</Index>
                        <SubIndex>1</SubIndex>
                        <Data>0300000000000000000000000000000080FFFFFF7F0000</Data>
                      </InitCmds>
                    </OperationMode>
                  </AlternativeSmMapping>
                </AlternativeSmMappings>
              </ProcessData>
              <Mailbox>
                <CoE>
                  <InitCmds/>
                </CoE>
              </Mailbox>
            </Slave>
          </EtherCAT>
        </TreeItem>
        """;

    private static XmlDocument Load(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    // ── No overrides (stock device) ──────────────────────────────────────────

    [Fact]
    public void Parse_NoUserEntries_ReturnsEmptyList()
    {
        var list = CoeStartupParser.Parse("TIID^path", "EL3174", Load(El3174NoOverrides));

        Assert.Equal(0, list.EntryCount);
        Assert.Empty(list.Entries);
    }

    [Fact]
    public void Parse_NoUserEntries_NotError()
    {
        // Empty list is a valid, non-error result.
        var list = CoeStartupParser.Parse("TIID^path", "EL3174", Load(El3174NoOverrides));
        Assert.NotNull(list);
    }

    // ── User-added mailbox entry ─────────────────────────────────────────────

    [Fact]
    public void Parse_UserMailboxEntry_Returns_OneEntry()
    {
        var list = CoeStartupParser.Parse("p", "EL3174", Load(El3174WithUserOverride));

        Assert.Equal(1, list.EntryCount);
        Assert.Single(list.Entries);
    }

    [Fact]
    public void Parse_UserMailboxEntry_Index_FormattedAsHex()
    {
        var list = CoeStartupParser.Parse("p", "EL3174", Load(El3174WithUserOverride));

        // Index 32781 decimal = 0x800D
        Assert.Equal("800D", list.Entries[0].Index);
    }

    [Fact]
    public void Parse_UserMailboxEntry_Fields_Correct()
    {
        var entry = CoeStartupParser.Parse("p", "EL3174", Load(El3174WithUserOverride)).Entries[0];

        Assert.Equal("17", entry.SubIndex);
        Assert.Equal("0300", entry.ValueHex);
        Assert.Equal("PS", entry.Transition);
        Assert.NotNull(entry.Comment);
        Assert.Contains("Input Type", entry.Comment);
        Assert.True(entry.Complete);  // Ccs=1
    }

    [Fact]
    public void Parse_UserMailboxEntry_BitLength_FourBitsPerHexNibble()
    {
        var entry = CoeStartupParser.Parse("p", "EL3174", Load(El3174WithUserOverride)).Entries[0];
        // "0300" = 4 hex chars = 16 bits
        Assert.Equal(16, entry.BitLength);
    }

    // ── User-modified OperationMode entry ────────────────────────────────────

    [Fact]
    public void Parse_OpModeUserEntry_Returns_OneEntry()
    {
        var list = CoeStartupParser.Parse("p", "EL3174", Load(El3174WithOpModeOverride));

        Assert.Equal(1, list.EntryCount);
    }

    [Fact]
    public void Parse_OpModeUserEntry_Index_StrippedHexPrefix()
    {
        var entry = CoeStartupParser.Parse("p", "EL3174", Load(El3174WithOpModeOverride)).Entries[0];

        Assert.Equal("800D", entry.Index);
    }

    [Fact]
    public void Parse_OpModeUserEntry_CompleteAccess_True()
    {
        var entry = CoeStartupParser.Parse("p", "EL3174", Load(El3174WithOpModeOverride)).Entries[0];
        Assert.True(entry.Complete);
    }

    // ── Metadata propagation ─────────────────────────────────────────────────

    [Fact]
    public void Parse_TreePathAndSlaveType_Propagated()
    {
        const string path = "TIID^Device 1^Box 1^-1AI5 (EL3174)";
        var list = CoeStartupParser.Parse(path, "EL3174", Load(El3174NoOverrides));

        Assert.Equal(path, list.TreePath);
        Assert.Equal("EL3174", list.SlaveType);
    }
}
