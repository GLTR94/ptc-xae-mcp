using PtcXaeMcp.WinForms.TwinCat.HostSelection;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Finds the <c>.tsproj</c> associated with a solution directory or path so
/// host resolution has a TcVersion to read. Missing/ambiguous input must
/// degrade to "no context" (null), never throw — callers then keep the
/// legacy probe order.
/// </summary>
public class TsprojLocatorTests
{
    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "tsl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void FindNearSln_TsprojInSubfolder_IsFound()
    {
        var root = NewDir();
        try
        {
            var sln = Path.Combine(root, "Proj.sln");
            File.WriteAllText(sln, "dummy");
            var sub = Path.Combine(root, "Proj");
            Directory.CreateDirectory(sub);
            var tsproj = Path.Combine(sub, "Proj.tsproj");
            File.WriteAllText(tsproj, "<TcSmProject TcVersion=\"3.1.4026.22\"/>");

            var found = TsprojLocator.FindForSolution(sln);

            Assert.Equal(tsproj, found, ignoreCase: true);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FindNearSln_NoTsproj_ReturnsNull()
    {
        var root = NewDir();
        try
        {
            var sln = Path.Combine(root, "Proj.sln");
            File.WriteAllText(sln, "dummy");

            Assert.Null(TsprojLocator.FindForSolution(sln));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FindNearSln_NullOrMissing_ReturnsNull()
    {
        Assert.Null(TsprojLocator.FindForSolution(null));
        Assert.Null(TsprojLocator.FindForSolution(""));
        Assert.Null(TsprojLocator.FindForSolution(@"X:\does\not\exist\a.sln"));
    }

    [Fact]
    public void FindNearSln_TsprojPathPassedDirectly_IsReturnedAsIs()
    {
        var root = NewDir();
        try
        {
            var tsproj = Path.Combine(root, "Direct.tsproj");
            File.WriteAllText(tsproj, "<TcSmProject TcVersion=\"3.1.4024.1\"/>");

            Assert.Equal(tsproj, TsprojLocator.FindForSolution(tsproj), ignoreCase: true);
        }
        finally { Directory.Delete(root, true); }
    }
}
