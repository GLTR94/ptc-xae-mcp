using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.TwinCat;
using PtcXaeMcp.WinForms.TwinCat.Apply;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #155 step 7: the smaller runner pieces moved onto testable seams — export paths (E-25),
/// the diagnostics folder (E-22), the exact LinkAlways write, and "explicit > schema > GUI
/// default" (a stale GUI selection never overrides the schema).
/// </summary>
public class ApplyRunnerPiecesTests
{
    [Fact]
    public void ExportPath_Relative_ResolvesNextToTheSchemaFile_NotTheWorkingDirectory()
    {
        string schemaDir = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp-test-export");
        string schema = Path.Combine(schemaDir, "schema.json");

        string resolved = ApplyFiles.ResolveExport("out\\mapping.csv", "export.mappingCsvPath", schema, solutionDirectory: null);

        resolved.Should().Be(Path.Combine(schemaDir, "out", "mapping.csv"));
    }

    [Fact]
    public void ExportPath_OutsideEveryAllowedRoot_IsRefused()
    {
        string schema = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp-test-export", "schema.json");
        string outside = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Windows", "ptc-export.xti");

        Action act = () => ApplyFiles.ResolveExports(outside, "", schema, solutionDirectory: null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*export.xtiPath*not an allowed export location*Nothing was changed*");
    }

    [Fact]
    public void ExportPath_Empty_MeansNotRequested()
    {
        ApplyFiles.ResolveExports("", " ", "C:\\x\\schema.json", null).Should().Be(new ApplyExportPaths("", ""));
    }

    [Fact]
    public void DiagnosticsFolder_IsUnderLocalAppData_NotTheExeDirectory()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ApplyFiles.DiagnosticsFolder.Should().Be(Path.Combine(local, "ptc-xae-mcp", "diagnostics"));
        ApplyFiles.DiagnosticsFolder.Should().NotStartWith(AppContext.BaseDirectory);
    }

    private const string GvlPath = "TIPC^Plc^Plc Project^GVLs^GVL_IO";

    private static string GvlXml(string linkAlways) =>
        "<TreeItem><ItemName>GVL_IO</ItemName><VSProperties><VSProperty category=\"Advanced\"><Name>LinkAlways</Name>" +
        $"<Value>{linkAlways}</Value></VSProperty><VSProperty category=\"Advanced\"><Name>Exclude</Name><Value>False</Value></VSProperty>" +
        "</VSProperties></TreeItem>";

    [Fact]
    public void SetAlwaysBind_WritesAndVerifiesTheExactGvlOnly()
    {
        var tree = new FakeSysManTree();
        tree.Add(GvlPath, itemType: PlcItemType.Gvl).Xml = GvlXml("False");
        tree.Add("TIPC^Other^Other Project^GVLs^GVL_IO", itemType: PlcItemType.Gvl).Xml = GvlXml("False");

        AlwaysBindOutcome outcome = new GvlWriter(tree).SetAlwaysBind(GvlPath);

        outcome.Verified.Should().BeTrue();
        tree.Mutations.Should().Equal($"consume {GvlPath}");
        tree.Get("TIPC^Other^Other Project^GVLs^GVL_IO").Xml.Should().Contain("<Value>False</Value>");
    }

    [Fact]
    public void SetAlwaysBind_NoFieldInTheXml_IsReportedNotClaimed()
    {
        var tree = new FakeSysManTree();
        tree.Add(GvlPath, itemType: PlcItemType.Gvl).Xml = "<TreeItem><ItemName>GVL_IO</ItemName></TreeItem>";

        AlwaysBindOutcome outcome = new GvlWriter(tree).SetAlwaysBind(GvlPath);

        outcome.Verified.Should().BeFalse();
        outcome.Message.Should().Contain("No LinkAlways");
        tree.Mutations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Explicit", "Schema", "Gui", "Explicit")]
    [InlineData("", "Schema", "Gui", "Schema")]
    [InlineData(null, " ", "Gui", "Gui")]
    [InlineData(null, null, null, "")]
    public void Effective_ExplicitThenSchemaThenGuiDefault(string? explicitValue, string? schema, string? gui, string expected) =>
        NativeTwinCatRunner.Effective(explicitValue, schema, gui).Should().Be(expected);
}
