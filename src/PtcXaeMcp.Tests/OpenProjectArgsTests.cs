using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class OpenProjectArgsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NullOrWhitespace_ReturnsError(string? path)
    {
        Assert.Equal("slnPath is required.", OpenProjectArgs.Validate(path));
    }

    [Theory]
    [InlineData("relative.sln")]
    [InlineData(@".\relative.sln")]
    [InlineData("subdir\\file.sln")]
    public void Validate_RelativePath_ReturnsError(string path)
    {
        Assert.Equal("slnPath must be an absolute path.", OpenProjectArgs.Validate(path));
    }

    [Theory]
    [InlineData(@"C:\file.txt")]
    [InlineData(@"C:\file")]
    [InlineData(@"C:\foo.csproj")]
    public void Validate_WrongExtension_ReturnsError(string path)
    {
        Assert.Equal("slnPath must end with .sln, .tsproj, .tspproj or .hmiproj.", OpenProjectArgs.Validate(path));
    }

    [Theory]
    [InlineData(".tsproj")]
    [InlineData(".tspproj")]
    [InlineData(".hmiproj")]
    public void Validate_ProjectFile_IsAccepted(string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), "p_" + Guid.NewGuid().ToString("N") + ext);
        File.WriteAllText(path, "");
        try { Assert.Null(OpenProjectArgs.Validate(path)); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("A.tsproj", "{B1E792BE-AA5F-4E3C-8C82-674BF9C0715B}")]
    [InlineData("A.hmiproj", "{FE7A1B72-C5B7-4D7C-BB7D-76384D4DE8E1}")]
    public void SolutionTextFor_UsesTheProjectTypeGuid_AndIsClassifiedAsDeclared(string file, string typeGuid)
    {
        var path = Path.Combine(Path.GetTempPath(), "x", file);
        var text = SolutionFile.SolutionTextFor(path);
        Assert.Contains($"Project(\"{typeGuid}\")", text);
        Assert.Contains(path, SolutionFile.ProjectPaths(text));
        var k = SolutionFile.Classify(text);
        Assert.Equal(file.EndsWith(".tsproj", StringComparison.Ordinal), k.HasTwinCat);
        Assert.Equal(file.EndsWith(".hmiproj", StringComparison.Ordinal), k.HasHmi);
    }

    [Fact]
    public void ResolveSolution_ProjectFile_WritesASolutionThatNamesIt_AndSlnPassesThrough()
    {
        var proj = Path.Combine(Path.GetTempPath(), "r_" + Guid.NewGuid().ToString("N") + ".tsproj");
        File.WriteAllText(proj, "");
        string? sln = null;
        try
        {
            sln = SolutionFile.ResolveSolution(proj);
            Assert.EndsWith(".sln", sln);
            Assert.Contains(proj, SolutionFile.ProjectPaths(File.ReadAllText(sln)));
            Assert.Equal(proj, PtcXaeMcp.WinForms.TwinCat.HostSelection.TsprojLocator.FindForSolution(sln));
            Assert.Equal(@"C:.sln", SolutionFile.ResolveSolution(@"C:.sln"));
        }
        finally
        {
            File.Delete(proj);
            if (sln is not null) File.Delete(sln);
        }
    }

    [Fact]
    public void Validate_NonExistentFile_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".sln");
        var result = OpenProjectArgs.Validate(path);
        Assert.NotNull(result);
        Assert.StartsWith("File not found", result);
    }

    [Fact]
    public void Validate_ExistingSln_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sln");
        File.WriteAllText(path, "dummy");
        try
        {
            Assert.Null(OpenProjectArgs.Validate(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
