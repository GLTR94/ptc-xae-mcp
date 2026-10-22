using System.Xml.Linq;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// Pure read-modify-write coverage for the axis ProduceXml patcher. The
// sample below is a faithful (trimmed) slice of a real axis ProduceXml(false)
// captured from the live SampleProject fixture (TC3 4026.22) — the element
// names and nesting are exactly as TwinCAT emits them.
public class NcAxisXmlTests
{
    private const string AxisXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>Transport</ItemName>" +
        "<PathName>TINC^NC-Task 1 SAF^Axes^Transport</PathName><ItemType>22</ItemType>" +
        "<NcAxisDef><AxisType>1</AxisType>" +
        "<AxisParam><Unit>mm</Unit>" +
        "<Dynamic><Acceleration>1500</Acceleration><Deceleration>1500</Deceleration><Jerk>2250</Jerk></Dynamic>" +
        "<Velo><Fast>2000</Fast><Maximum>2000</Maximum></Velo></AxisParam>" +
        "<Encoder><EncType>1</EncType><EncParam>" +
        "<ScaleFactorNumerator>0.0001</ScaleFactorNumerator><ScaleFactorDenominator>1</ScaleFactorDenominator>" +
        "<Offset>0</Offset><ModuloFactor>360</ModuloFactor><Inverse>false</Inverse><EncMode>2</EncMode>" +
        "<SoftEndMinControl><Enable>false</Enable><Range>0</Range></SoftEndMinControl>" +
        "<SoftEndMaxControl><Enable>false</Enable><Range>0</Range></SoftEndMaxControl>" +
        "</EncParam></Encoder></NcAxisDef></TreeItem>";

    // A default (freshly created) axis: NO <AxisIoType>, EncType 1 / DrvType 5,
    // and a <Drive> block — the shape ApplyAxisType needs. Captured live (100000).
    private const string DefaultAxisXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>Axis_Test</ItemName>" +
        "<NcAxisDef><AxisType>1</AxisType>" +
        "<AxisParam><Unit>mm</Unit></AxisParam>" +
        "<Encoder><EncType>1</EncType><EncParam><ModuloFactor>360</ModuloFactor></EncParam></Encoder>" +
        "<Drive><DrvType>5</DrvType><DrvParam><DrvMode>1</DrvMode></DrvParam></Drive>" +
        "</NcAxisDef></TreeItem>";

    private static string ValueAt(string xml, params string[] chain)
    {
        var el = XDocument.Parse(xml).Root!;
        foreach (var step in chain) el = el.Element(step)!;
        return el.Value;
    }

    [Fact]
    public void ApplyFields_PatchesScaleFactor_AtCapturedPath()
    {
        var (xml, applied) = NcAxisXml.ApplyFields(AxisXml,
            new Dictionary<string, string> { ["scaleFactorNumerator"] = "0.0002" });

        Assert.Equal("0.0002", ValueAt(xml, "NcAxisDef", "Encoder", "EncParam", "ScaleFactorNumerator"));
        Assert.Contains("scaleFactorNumerator=0.0002", applied);
    }

    [Fact]
    public void ApplyFields_LeavesOtherElementsUntouched()
    {
        var (xml, _) = NcAxisXml.ApplyFields(AxisXml,
            new Dictionary<string, string> { ["velocityMax"] = "1000" });

        Assert.Equal("1000", ValueAt(xml, "NcAxisDef", "AxisParam", "Velo", "Maximum"));
        // Sibling and unrelated values preserved verbatim.
        Assert.Equal("2000", ValueAt(xml, "NcAxisDef", "AxisParam", "Velo", "Fast"));
        Assert.Equal("0.0001", ValueAt(xml, "NcAxisDef", "Encoder", "EncParam", "ScaleFactorNumerator"));
        Assert.Equal("Transport", ValueAt(xml, "ItemName"));
    }

    [Fact]
    public void ApplyFields_AppliesMultipleFields_NestedAndShallow()
    {
        var (xml, applied) = NcAxisXml.ApplyFields(AxisXml, new Dictionary<string, string>
        {
            ["unit"]          = "deg",
            ["acceleration"]  = "3000",
            ["softMaxEnable"] = "true",
            ["softMaxRange"]  = "500",
        });

        Assert.Equal("deg", ValueAt(xml, "NcAxisDef", "AxisParam", "Unit"));
        Assert.Equal("3000", ValueAt(xml, "NcAxisDef", "AxisParam", "Dynamic", "Acceleration"));
        Assert.Equal("true", ValueAt(xml, "NcAxisDef", "Encoder", "EncParam", "SoftEndMaxControl", "Enable"));
        Assert.Equal("500", ValueAt(xml, "NcAxisDef", "Encoder", "EncParam", "SoftEndMaxControl", "Range"));
        Assert.Equal(4, applied.Count);
    }

    [Fact]
    public void ApplyFields_UnknownKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NcAxisXml.ApplyFields(AxisXml, new Dictionary<string, string> { ["nope"] = "1" }));
        Assert.Contains("Unknown axis field", ex.Message);
    }

    [Fact]
    public void ApplyFields_MissingElement_ThrowsWithPath()
    {
        // An axis whose Encoder block has no ScaleFactorNumerator yet.
        const string bare =
            "<TreeItem><NcAxisDef><Encoder><EncParam><Offset>0</Offset></EncParam></Encoder></NcAxisDef></TreeItem>";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NcAxisXml.ApplyFields(bare, new Dictionary<string, string> { ["scaleFactorNumerator"] = "1" }));
        Assert.Contains("ScaleFactorNumerator", ex.Message);
    }

    [Fact]
    public void ApplyFields_EmptyFields_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NcAxisXml.ApplyFields(AxisXml, new Dictionary<string, string>()));
    }

    [Fact]
    public void ApplyFields_EmptyXml_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NcAxisXml.ApplyFields("  ", new Dictionary<string, string> { ["unit"] = "mm" }));
    }

    [Fact]
    public void ApplyFields_ResultIsParseableForConsumeXml()
    {
        var (xml, _) = NcAxisXml.ApplyFields(AxisXml,
            new Dictionary<string, string> { ["jerk"] = "9000" });
        var doc = XDocument.Parse(xml);   // must round-trip cleanly
        Assert.Equal("TreeItem", doc.Root!.Name.LocalName);
    }

    // ---- ReadFields ----------------------------------------------------------

    [Fact]
    public void ReadFields_ExtractsCurrentValues_AtCapturedPaths()
    {
        var f = NcAxisXml.ReadFields(AxisXml);
        Assert.Equal("mm", f["unit"]);
        Assert.Equal("0.0001", f["scaleFactorNumerator"]);
        Assert.Equal("1", f["scaleFactorDenominator"]);
        Assert.Equal("2000", f["velocityMax"]);
        Assert.Equal("1500", f["acceleration"]);
        Assert.Equal("2250", f["jerk"]);
        Assert.Equal("false", f["softMaxEnable"]);
    }

    [Fact]
    public void ReadFields_OmitsAbsentElements_NeverThrows()
    {
        const string bare =
            "<TreeItem><NcAxisDef><AxisParam><Unit>deg</Unit></AxisParam></NcAxisDef></TreeItem>";
        var f = NcAxisXml.ReadFields(bare);
        Assert.Equal("deg", f["unit"]);
        Assert.False(f.ContainsKey("scaleFactorNumerator"));
        Assert.False(f.ContainsKey("velocityMax"));
    }

    [Fact]
    public void ReadFields_EmptyOrInvalidXml_ReturnsEmpty()
    {
        Assert.Empty(NcAxisXml.ReadFields("  "));
        Assert.Empty(NcAxisXml.ReadFields("not xml <"));
    }

    [Fact]
    public void ReadFields_RoundTripsThroughApplyFields()
    {
        var (xml, _) = NcAxisXml.ApplyFields(AxisXml,
            new Dictionary<string, string> { ["unit"] = "deg", ["velocityMax"] = "999" });
        var f = NcAxisXml.ReadFields(xml);
        Assert.Equal("deg", f["unit"]);
        Assert.Equal("999", f["velocityMax"]);
    }

    // ---- axisType (composite Achstyp) ----------------------------------------

    [Fact]
    public void ApplyFields_AxisType_Coe_CreatesIoType_AndSetsEncDrv()
    {
        var (xml, applied) = NcAxisXml.ApplyFields(DefaultAxisXml,
            new Dictionary<string, string> { ["axisType"] = "canopen_ds402" });

        Assert.Equal("CANopen-DS402", ValueAt(xml, "NcAxisDef", "AxisIoType"));
        Assert.Equal("19", ValueAt(xml, "NcAxisDef", "Encoder", "EncType"));
        Assert.Equal("16", ValueAt(xml, "NcAxisDef", "Drive", "DrvType"));
        Assert.Contains(applied, a => a.StartsWith("axisType=canopen_ds402", System.StringComparison.Ordinal));

        // AxisIoType must be inserted immediately after AxisType (element order).
        var ncDef = XDocument.Parse(xml).Root!.Element("NcAxisDef")!;
        var names = ncDef.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal("AxisType", names[0]);
        Assert.Equal("AxisIoType", names[1]);
    }

    [Fact]
    public void ApplyFields_AxisType_Sercos_SetsTriple()
    {
        var (xml, _) = NcAxisXml.ApplyFields(DefaultAxisXml,
            new Dictionary<string, string> { ["axisType"] = "sercos_soe" });

        Assert.Equal("Sercos", ValueAt(xml, "NcAxisDef", "AxisIoType"));
        Assert.Equal("8", ValueAt(xml, "NcAxisDef", "Encoder", "EncType"));
        Assert.Equal("9", ValueAt(xml, "NcAxisDef", "Drive", "DrvType"));
    }

    [Fact]
    public void ApplyFields_AxisType_ReplacesExistingIoType_NoDuplicate()
    {
        // Start from a Sercos axis, switch to CoE: AxisIoType replaced, not doubled.
        var (sercos, _) = NcAxisXml.ApplyFields(DefaultAxisXml,
            new Dictionary<string, string> { ["axisType"] = "sercos_soe" });
        var (coe, _) = NcAxisXml.ApplyFields(sercos,
            new Dictionary<string, string> { ["axisType"] = "canopen_ds402" });

        var ncDef = XDocument.Parse(coe).Root!.Element("NcAxisDef")!;
        Assert.Single(ncDef.Elements("AxisIoType"));
        Assert.Equal("CANopen-DS402", ValueAt(coe, "NcAxisDef", "AxisIoType"));
    }

    [Fact]
    public void ApplyFields_AxisType_WithLeafField_BothApplied()
    {
        var (xml, applied) = NcAxisXml.ApplyFields(DefaultAxisXml,
            new Dictionary<string, string> { ["axisType"] = "coe", ["unit"] = "deg" });

        Assert.Equal("CANopen-DS402", ValueAt(xml, "NcAxisDef", "AxisIoType"));
        Assert.Equal("deg", ValueAt(xml, "NcAxisDef", "AxisParam", "Unit"));
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public void ApplyFields_AxisType_UnknownValue_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NcAxisXml.ApplyFields(DefaultAxisXml,
                new Dictionary<string, string> { ["axisType"] = "profinet" }));
        Assert.Contains("Unknown axisType", ex.Message);
    }

    [Fact]
    public void ApplyFields_AxisType_NoDriveElement_ThrowsWithPath()
    {
        // AxisXml fixture has no <Drive> block.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NcAxisXml.ApplyFields(AxisXml,
                new Dictionary<string, string> { ["axisType"] = "canopen_ds402" }));
        Assert.Contains("Drive", ex.Message);
    }

    [Fact]
    public void ApplyFields_AxisType_ResultIsParseableForConsumeXml()
    {
        var (xml, _) = NcAxisXml.ApplyFields(DefaultAxisXml,
            new Dictionary<string, string> { ["axisType"] = "canopen_ds402" });
        Assert.Equal("TreeItem", XDocument.Parse(xml).Root!.Name.LocalName);
    }

    [Fact]
    public void ReadFields_AxisType_ReportsExplicitIoType()
    {
        var (xml, _) = NcAxisXml.ApplyFields(DefaultAxisXml,
            new Dictionary<string, string> { ["axisType"] = "canopen_ds402" });
        Assert.Equal("CANopen-DS402", NcAxisXml.ReadFields(xml)["axisType"]);
    }

    [Fact]
    public void ReadFields_AxisType_InferredWhenAbsent()
    {
        // Default axis has no AxisIoType → reported as "(none; EncType=1, DrvType=5)".
        var v = NcAxisXml.ReadFields(DefaultAxisXml)["axisType"];
        Assert.Contains("none", v);
        Assert.Contains("EncType=1", v);
        Assert.Contains("DrvType=5", v);
    }
}
