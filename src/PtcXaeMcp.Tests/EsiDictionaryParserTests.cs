using System.Xml;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class EsiDictionaryParserTests
{
    // Trimmed fixture: EL3174-like device with DT800D RECORD and DT0802EN16 enum.
    // ProductCode #x0c663052 = 208023634 decimal, RevisionNo #x00100000 = 1048576 decimal.
    private const string El3174Esi = """
        <EtherCATInfo>
          <Descriptions>
            <Devices>
              <Device>
                <Type ProductCode="#x0c663052" RevisionNo="#x00100000" CheckRevisionNo="LW_EQ">EL3174</Type>
                <Profile>
                  <Dictionary>
                    <DataTypes>
                      <DataType>
                        <Name>USINT</Name>
                        <BitSize>8</BitSize>
                      </DataType>
                      <DataType>
                        <Name>UDINT</Name>
                        <BitSize>32</BitSize>
                      </DataType>
                      <DataType>
                        <Name>DINT</Name>
                        <BitSize>32</BitSize>
                      </DataType>
                      <DataType>
                        <Name>DT0802EN16</Name>
                        <BaseType>UINT</BaseType>
                        <BitSize>16</BitSize>
                        <EnumInfo><Text>50 Hz FIR</Text><Enum>0</Enum></EnumInfo>
                        <EnumInfo><Text>60 Hz FIR</Text><Enum>1</Enum></EnumInfo>
                        <EnumInfo><Text>IIR level 1</Text><Enum>2</Enum></EnumInfo>
                        <EnumInfo><Text>IIR level 2</Text><Enum>3</Enum></EnumInfo>
                      </DataType>
                      <DataType>
                        <Name>DT800D</Name>
                        <BitSize>192</BitSize>
                        <SubItem>
                          <SubIdx>0</SubIdx>
                          <Name>SubIndex 000</Name>
                          <Type>USINT</Type>
                          <BitSize>8</BitSize>
                          <BitOffs>0</BitOffs>
                          <Flags><Access>ro</Access></Flags>
                        </SubItem>
                        <SubItem>
                          <SubIdx>17</SubIdx>
                          <Name>Input Type</Name>
                          <Type>DT0802EN16</Type>
                          <BitSize>16</BitSize>
                          <BitOffs>32</BitOffs>
                          <Flags>
                            <Access>rw</Access>
                            <Setting>1</Setting>
                          </Flags>
                        </SubItem>
                        <SubItem>
                          <SubIdx>18</SubIdx>
                          <Name>Scaler</Name>
                          <Type>DINT</Type>
                          <BitSize>32</BitSize>
                          <BitOffs>48</BitOffs>
                          <Flags>
                            <Access>rw</Access>
                            <Setting>1</Setting>
                          </Flags>
                        </SubItem>
                      </DataType>
                    </DataTypes>
                    <Objects>
                      <Object>
                        <Index>#x1000</Index>
                        <Name>Device type</Name>
                        <Type>UDINT</Type>
                        <BitSize>32</BitSize>
                        <Info><DefaultData>89132c01</DefaultData></Info>
                        <Flags><Access>ro</Access></Flags>
                      </Object>
                      <Object>
                        <Index>#x800d</Index>
                        <Name>AI Advanced Settings Ch.1</Name>
                        <Type>DT800D</Type>
                        <BitSize>192</BitSize>
                        <Info>
                          <SubItem>
                            <Name>SubIndex 000</Name>
                            <Info><DefaultData>18</DefaultData></Info>
                          </SubItem>
                          <SubItem>
                            <Name>Input Type</Name>
                            <Info><DefaultData>0200</DefaultData></Info>
                          </SubItem>
                          <SubItem>
                            <Name>Scaler</Name>
                            <Info><DefaultData>0000</DefaultData></Info>
                          </SubItem>
                        </Info>
                        <Flags><Access>rw</Access></Flags>
                      </Object>
                    </Objects>
                  </Dictionary>
                </Profile>
              </Device>
            </Devices>
          </Descriptions>
        </EtherCATInfo>
        """;

    private static XmlDocument LoadFixture(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    // ── Device selection ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_ExactRevision_Finds_Device()
    {
        var doc = LoadFixture(El3174Esi);
        // ProductCode 208023634 = 0x0c663052; RevisionNo 1048576 = 0x00100000
        var (dict, note) = EsiDictionaryParser.Parse("TIID^path", "EL3174", doc, 208023634L, 1048576L, null);

        Assert.Null(note);
        Assert.True(dict.ObjectCount > 0);
    }

    [Fact]
    public void Parse_RevisionFallback_ReturnsNote()
    {
        var doc = LoadFixture(El3174Esi);
        // Request a different revision — fixture only has 0x00100000
        var (_, note) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 0x00110000L, null);

        Assert.NotNull(note);
        Assert.Contains("fallback", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ProductCodeNotFound_Throws()
    {
        var doc = LoadFixture(El3174Esi);
        Assert.Throws<InvalidOperationException>(
            () => EsiDictionaryParser.Parse("p", "Unknown", doc, 0x12345678L, 0L, null));
    }

    // ── Object extraction ────────────────────────────────────────────────────

    [Fact]
    public void Parse_TwoObjects_CorrectCount()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, null);
        Assert.Equal(2, dict.ObjectCount);
        Assert.Equal(2, dict.Objects.Count);
    }

    [Fact]
    public void Parse_SimpleVar_Correct()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, null);

        var obj1000 = dict.Objects.First(o => o.Index == "1000");
        Assert.Equal("Device type", obj1000.Name);
        Assert.Equal("ro", obj1000.Access);
        Assert.Equal("VAR", obj1000.ObjectType);
        Assert.Single(obj1000.SubIndexes);
        Assert.Equal("89132c01", obj1000.SubIndexes[0].DefaultValue);
    }

    [Fact]
    public void Parse_RecordObject_CorrectSubIndexes()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, null);

        var obj800d = dict.Objects.FirstOrDefault(o => o.Index == "800D");
        Assert.NotNull(obj800d);
        Assert.Equal("AI Advanced Settings Ch.1", obj800d.Name);
        Assert.Equal("RECORD", obj800d.ObjectType);
        Assert.Equal(3, obj800d.SubIndexes.Count);
    }

    [Fact]
    public void Parse_SubIndex00_IsReadOnly()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, null);

        var si0 = dict.Objects.First(o => o.Index == "800D").SubIndexes[0];
        Assert.Equal("00", si0.SubIndex);
        Assert.Equal("ro", si0.Access);
        Assert.False(si0.IsSetting);
    }

    // ── Sub-index field mapping ───────────────────────────────────────────────

    [Fact]
    public void Parse_InputType_SubIndex11_HasCorrectFields()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, null);

        var si11 = dict.Objects.First(o => o.Index == "800D").SubIndexes[1]; // SubIdx 17 = 0x11
        Assert.Equal("11", si11.SubIndex);
        Assert.Equal("Input Type", si11.Name);
        Assert.Equal("DT0802EN16", si11.DataType);
        Assert.Equal(16, si11.BitSize);
        Assert.Equal("rw", si11.Access);
        Assert.True(si11.IsSetting);
        Assert.Equal("0200", si11.DefaultValue);
    }

    // ── Enum resolution ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_InputType_DefaultText_EnumResolved()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, null);

        // DefaultData "0200" → little-endian UINT16 → value 2 → "IIR level 1 (2)"
        var si11 = dict.Objects.First(o => o.Index == "800D").SubIndexes[1];
        Assert.NotNull(si11.DefaultText);
        Assert.Contains("IIR level 1", si11.DefaultText);
        Assert.Contains("(2)", si11.DefaultText);
    }

    [Fact]
    public void Parse_Scaler_NoEnum_DefaultTextNull()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, null);

        var si12 = dict.Objects.First(o => o.Index == "800D").SubIndexes[2]; // SubIdx 18 = 0x12
        Assert.Null(si12.DefaultText);
    }

    // ── Index filter ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_IndexFilter_SingleIndex_MatchesOnly800D()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, "800D");

        Assert.Equal(1, dict.ObjectCount);
        Assert.Equal("800D", dict.Objects[0].Index);
    }

    [Fact]
    public void Parse_IndexFilter_Range_ExcludesOutside()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, "8000-80FF");

        Assert.All(dict.Objects, o => Assert.InRange(Convert.ToInt32(o.Index, 16), 0x8000, 0x80FF));
    }

    [Fact]
    public void Parse_IndexFilter_Range_IncludesMatchingObjects()
    {
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse("p", "EL3174", doc, 208023634L, 1048576L, "1000-1FFF");

        Assert.Equal(1, dict.ObjectCount);
        Assert.Equal("1000", dict.Objects[0].Index);
    }

    // ── TreePath and SlaveType propagated ────────────────────────────────────

    [Fact]
    public void Parse_TreePathAndSlaveType_Propagated()
    {
        const string path = "TIID^Device 1^Box 1^-1AI5 (EL3174)";
        var doc = LoadFixture(El3174Esi);
        var (dict, _) = EsiDictionaryParser.Parse(path, "EL3174", doc, 208023634L, 1048576L, null);

        Assert.Equal(path, dict.TreePath);
        Assert.Equal("EL3174", dict.SlaveType);
    }
}
