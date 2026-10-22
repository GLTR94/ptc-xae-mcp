using PtcXaeMcp.WinForms.TwinCat.HostSelection;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Reads the root <c>TcVersion</c> attribute out of a <c>.tsproj</c> file.
/// Malformed input (missing file, not XML, no attribute) must produce a
/// defined failure result, never an exception into the caller.
/// </summary>
public class TsprojReaderTests
{
    private const string Tsproj4026 =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <TcSmProject TcVersion="3.1.4026.22" TcSmVersion="1.0" TcVersionFixed="true">
          <Project />
        </TcSmProject>
        """;

    private const string Tsproj4024 =
        """
        <TcSmProject TcVersion="3.1.4024.55"><Project/></TcSmProject>
        """;

    private static string WriteTemp(string content, string ext = ".tsproj")
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void ReadFromString_4026_ReturnsVersion()
    {
        Assert.True(TsprojReader.TryReadTcVersion(Tsproj4026, out var v, out var err));
        Assert.Null(err);
        Assert.Equal("3.1.4026.22", v!.ToString());
        Assert.Equal(XaeHostClass.Shell64, v.Classify());
    }

    [Fact]
    public void ReadFromString_4024_ClassifiesShell32()
    {
        Assert.True(TsprojReader.TryReadTcVersion(Tsproj4024, out var v, out _));
        Assert.Equal(XaeHostClass.Shell32, v!.Classify());
    }

    [Fact]
    public void ReadFromString_NotXml_FailsWithMessage()
    {
        Assert.False(TsprojReader.TryReadTcVersion("not xml at all {", out var v, out var err));
        Assert.Null(v);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void ReadFromString_NoTcVersionAttribute_FailsWithMessage()
    {
        Assert.False(TsprojReader.TryReadTcVersion(
            "<TcSmProject TcSmVersion=\"1.0\"><Project/></TcSmProject>",
            out var v, out var err));
        Assert.Null(v);
        Assert.Contains("TcVersion", err!);
    }

    [Fact]
    public void ReadFromString_TcVersionPresentButMalformed_FailsWithMessage()
    {
        Assert.False(TsprojReader.TryReadTcVersion(
            "<TcSmProject TcVersion=\"garbage\"/>", out var v, out var err));
        Assert.Null(v);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void ReadFile_Missing_FailsWithMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N") + ".tsproj");
        Assert.False(TsprojReader.TryReadTcVersionFromFile(path, out var v, out var err));
        Assert.Null(v);
        Assert.Contains("not found", err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFile_Valid_ReturnsVersion()
    {
        var path = WriteTemp(Tsproj4026);
        try
        {
            Assert.True(TsprojReader.TryReadTcVersionFromFile(path, out var v, out var err));
            Assert.Null(err);
            Assert.Equal("3.1.4026.22", v!.ToString());
        }
        finally { File.Delete(path); }
    }
}
