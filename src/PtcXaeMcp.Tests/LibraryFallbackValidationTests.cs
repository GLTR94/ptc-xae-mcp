using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #148 (B-2) — library file fallbacks validate before closing the solution.</summary>
public class LibraryFallbackValidationTests
{
    private const string PouPath = "TIPC^Plc^Plc Project^POUs^FB_A";

    private static FakeTwinCatSession OpenFake() => new() { IsConnected = true, SolutionOpen = true };

    // =====================================================================
    // Library fallback: validate before closing (B-2)
    // =====================================================================

    private const string Plcproj = """
        <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <ItemGroup>
            <PlaceholderReference Include="Sample-Library">
              <DefaultResolution>Sample_Library, * (Sample Vendor)</DefaultResolution>
            </PlaceholderReference>
          </ItemGroup>
        </Project>
        """;

    private static string TempPlcproj()
    {
        string p = Path.Combine(Path.GetTempPath(), $"ptc148-{Guid.NewGuid():N}.plcproj");
        File.WriteAllText(p, Plcproj);
        return p;
    }

    [Fact]
    public void LibraryValidate_UnknownPlaceholder_Throws_AndLeavesFileUntouched()
    {
        string p = TempPlcproj();
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                LibraryParsing.ValidateParameterUpsert(p, "Nope", "GVL", "K", "1"));
            Assert.Throws<InvalidOperationException>(() =>
                LibraryParsing.ValidatePlaceholderResolutionUpsert(p, "Nope", "Nope, 1.0 (X)"));
            Assert.Equal(Plcproj, File.ReadAllText(p));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void LibraryValidate_Happy_DoesNotWrite()
    {
        string p = TempPlcproj();
        try
        {
            LibraryParsing.ValidateParameterUpsert(p, "Sample-Library", "GVL", "K", "1");
            LibraryParsing.ValidatePlaceholderResolutionUpsert(p, "Sample-Library", "Sample_Library, 1.0 (Sample Vendor)");
            Assert.Equal(Plcproj, File.ReadAllText(p));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void LibraryValidate_ReadOnlyFile_Throws()
    {
        string p = TempPlcproj();
        try
        {
            File.SetAttributes(p, FileAttributes.ReadOnly);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                LibraryParsing.ValidateParameterUpsert(p, "Sample-Library", "GVL", "K", "1"));
            Assert.Contains("read-only", ex.Message);
        }
        finally
        {
            File.SetAttributes(p, FileAttributes.Normal);
            File.Delete(p);
        }
    }
}
