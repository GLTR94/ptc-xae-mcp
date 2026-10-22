using PtcXaeMcp.WinForms.TwinCat.HostSelection;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Parsing and classification of the <c>.tsproj</c> root <c>TcVersion</c>
/// attribute. The documented split is build &gt;= 4026 → 64-bit XAE Shell,
/// build &lt; 4026 → 32-bit XAE Shell. Comparison is on major.minor.build;
/// the revision (4th part) is ignored.
/// </summary>
public class TcVersionTests
{
    [Theory]
    [InlineData("3.1.4026.22", 3, 1, 4026, 22)]
    [InlineData("3.1.4024.55", 3, 1, 4024, 55)]
    [InlineData("3.1.4026.0", 3, 1, 4026, 0)]
    [InlineData("3.1.4026", 3, 1, 4026, 0)]
    public void Parse_WellFormed_ExtractsParts(string s, int ma, int mi, int b, int r)
    {
        TcVersion.TryParse(s, out var v).Should().BeTrue();
        v!.Major.Should().Be(ma);
        v.Minor.Should().Be(mi);
        v.Build.Should().Be(b);
        v.Revision.Should().Be(r);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("3.1")]
    [InlineData("3.1.x.y")]
    [InlineData("3.1.4026.22-rc1")]
    public void Parse_Malformed_ReturnsFalse(string? s)
    {
        TcVersion.TryParse(s, out var v).Should().BeFalse();
        v.Should().BeNull();
    }

    [Theory]
    [InlineData("3.1.4026.22", XaeHostClass.Shell64)]
    [InlineData("3.1.4026.0", XaeHostClass.Shell64)]
    [InlineData("3.1.4026", XaeHostClass.Shell64)]      // boundary, exactly 4026
    [InlineData("3.1.4027.5", XaeHostClass.Shell64)]
    [InlineData("3.1.4024.55", XaeHostClass.Shell32)]
    [InlineData("3.1.4025.99", XaeHostClass.Shell32)]   // conservative: < 4026
    [InlineData("3.1.4000.0", XaeHostClass.Shell32)]
    public void Classify_MapsBuildToHostClass(string s, XaeHostClass expected)
    {
        TcVersion.TryParse(s, out var v).Should().BeTrue();
        v!.Classify().Should().Be(expected);
    }

    [Fact]
    public void Classify_RevisionDoesNotAffectBoundary()
    {
        // 4026 with any revision is still Shell64; 4025 with any revision Shell32.
        TcVersion.TryParse("3.1.4026.999", out var hi).Should().BeTrue();
        TcVersion.TryParse("3.1.4025.0", out var lo).Should().BeTrue();
        hi!.Classify().Should().Be(XaeHostClass.Shell64);
        lo!.Classify().Should().Be(XaeHostClass.Shell32);
    }

    [Fact]
    public void ToString_RoundTripsCanonicalForm()
    {
        TcVersion.TryParse("3.1.4026.22", out var v).Should().BeTrue();
        v!.ToString().Should().Be("3.1.4026.22");
    }
}
