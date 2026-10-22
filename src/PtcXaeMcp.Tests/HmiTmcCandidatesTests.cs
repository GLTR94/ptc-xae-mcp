using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// hmi-ads-tmc #166: twincat_hmi_list_plc_tmc_candidates. The parsing of
/// TreeItem/PlcProjectDef/RelativeTmcPath and the resolution against the .tsproj directory are
/// pure and tested here on real files in %TEMP%; the COM walk that feeds them is not (unverified live).
/// </summary>
[Collection(PathPolicyEnvironment.Name)]
public class HmiTmcCandidatesTests : IDisposable
{
    private readonly string _root;
    private readonly string _tsproj;

    public HmiTmcCandidatesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ptc-tmccand-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "Machine"));
        _tsproj = Path.Combine(_root, "Machine", "Machine.tsproj");
        File.WriteAllText(_tsproj, "<TcSmProject/>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string NodeXml(string plcName, string? relativeTmcPath) =>
        "<TreeItem><ItemName>" + plcName + "</ItemName><ItemType>56</ItemType><PlcProjectDef>" +
        "<ProjectGuid>{00000000-0000-0000-0000-000000000001}</ProjectGuid>" +
        (relativeTmcPath is null ? "" : "<RelativeTmcPath>" + relativeTmcPath + "</RelativeTmcPath>") +
        "</PlcProjectDef></TreeItem>";

    /// <summary>A file with the real shape of a PLC project's .tmc, relative to the .tsproj directory.</summary>
    private string TmcOnDisk(string relative, int symbols)
    {
        string path = Path.Combine(Path.GetDirectoryName(_tsproj)!, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string body = string.Concat(Enumerable.Repeat(
            "<Symbol><Name>MAIN.bTest</Name><BaseType>BOOL</BaseType></Symbol>", symbols));
        File.WriteAllText(path,
            $"<?xml version=\"1.0\" encoding=\"utf-8\"?>{TmcFile.RootElement} GeneratedBy=\"TwinCAT XAE Plc\">{body}</TcModuleClass>");
        return path;
    }

    private static string? AllowAll(string _) => null;

    // ── ParseRelativeTmcPath ────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsRelativeTmcPath()
    {
        var (relative, error) = HmiTmcCandidates.ParseRelativeTmcPath(NodeXml("PLC1", @"PLC1\PLC1.tmc"));

        Assert.Null(error);
        Assert.Equal(@"PLC1\PLC1.tmc", relative);
    }

    [Fact]
    public void Parse_MissingRelativeTmcPath_IsAReasonNotAPath()
    {
        var (relative, error) = HmiTmcCandidates.ParseRelativeTmcPath(NodeXml("PLC1", null));

        Assert.Null(relative);
        Assert.Contains("RelativeTmcPath", error);
    }

    [Fact]
    public void Parse_EmptyRelativeTmcPath_IsTreatedAsMissing()
    {
        var (relative, error) = HmiTmcCandidates.ParseRelativeTmcPath(NodeXml("PLC1", "  "));

        Assert.Null(relative);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<TreeItem><ItemName>PLC1</ItemName></TreeItem>")]
    [InlineData("<TreeItem><PlcProjectDef>")]
    public void Parse_NoPlcProjectDefOrBrokenXml_IsAReason(string? xml)
    {
        var (relative, error) = HmiTmcCandidates.ParseRelativeTmcPath(xml);

        Assert.Null(relative);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ── Resolve ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CombinesWithTheTsprojDirectory()
    {
        var (absolute, error) = HmiTmcCandidates.Resolve(_tsproj, @"PLC1\PLC1.tmc");

        Assert.Null(error);
        Assert.Equal(Path.Combine(_root, "Machine", "PLC1", "PLC1.tmc"), absolute);
    }

    [Fact]
    public void Resolve_NormalizesParentSegments()
    {
        var (absolute, _) = HmiTmcCandidates.Resolve(_tsproj, @"..\Shared\PLC.tmc");

        Assert.Equal(Path.Combine(_root, "Shared", "PLC.tmc"), absolute);
    }

    [Fact]
    public void Resolve_WithoutTsprojPath_IsAReason()
    {
        var (absolute, error) = HmiTmcCandidates.Resolve(null, @"PLC1\PLC1.tmc");

        Assert.Null(absolute);
        Assert.Contains(".tsproj", error);
    }

    // ── Build ───────────────────────────────────────────────────────────────

    [Fact]
    public void Build_SeveralPlcProjects_OneCandidateEach()
    {
        string plc1 = TmcOnDisk(@"PLC1\PLC1.tmc", symbols: 3);
        var projects = new[]
        {
            new PlcProjectXml("Machine", _tsproj, "PLC1", NodeXml("PLC1", @"PLC1\PLC1.tmc"), null),
            new PlcProjectXml("Machine", _tsproj, "PLC2", NodeXml("PLC2", null), null),
            new PlcProjectXml("Machine", _tsproj, "PLC3", NodeXml("PLC3", @"PLC3\PLC3.tmc"), null),
            new PlcProjectXml("Machine", _tsproj, "PLC4", null, "ProduceXml failed on TIPC^PLC4: boom"),
        };

        IReadOnlyList<HmiTmcCandidate> c = HmiTmcCandidates.Build(projects, TmcFile.Inspect, AllowAll);

        Assert.Equal(["PLC1", "PLC2", "PLC3", "PLC4"], c.Select(x => x.PlcProject));

        Assert.True(c[0].Exists);
        Assert.Equal(plc1, c[0].AbsolutePath);
        Assert.Equal(@"PLC1\PLC1.tmc", c[0].RelativeTmcPath);
        Assert.Equal(3, c[0].SymbolCount);
        Assert.Equal(File.GetLastWriteTimeUtc(plc1), c[0].LastWriteUtc);
        Assert.Null(c[0].Note);

        // missing RelativeTmcPath
        Assert.False(c[1].Exists);
        Assert.Null(c[1].RelativeTmcPath);
        Assert.Null(c[1].AbsolutePath);
        Assert.Contains("RelativeTmcPath", c[1].Note);

        // file not (yet) written
        Assert.False(c[2].Exists);
        Assert.Equal(Path.Combine(_root, "Machine", "PLC3", "PLC3.tmc"), c[2].AbsolutePath);
        Assert.Null(c[2].LastWriteUtc);
        Assert.Null(c[2].SymbolCount);
        Assert.Contains("does not exist", c[2].Note);

        // unreadable node keeps its reason
        Assert.False(c[3].Exists);
        Assert.Contains("boom", c[3].Note);
    }

    [Fact]
    public void Build_TmcWithoutSymbols_IsFlagged()
    {
        TmcOnDisk(@"PLC1\PLC1.tmc", symbols: 0);

        var c = HmiTmcCandidates.Build(
            [new PlcProjectXml("Machine", _tsproj, "PLC1", NodeXml("PLC1", @"PLC1\PLC1.tmc"), null)],
            TmcFile.Inspect, AllowAll);

        Assert.Equal(0, c[0].SymbolCount);
        Assert.Contains("no <Symbol>", c[0].Note);
    }

    [Fact]
    public void Build_PathRefusedByPolicy_IsNotOpened()
    {
        TmcOnDisk(@"PLC1\PLC1.tmc", symbols: 3);
        bool inspected = false;

        var c = HmiTmcCandidates.Build(
            [new PlcProjectXml("Machine", _tsproj, "PLC1", NodeXml("PLC1", @"PLC1\PLC1.tmc"), null)],
            p => { inspected = true; return TmcFile.Inspect(p); },
            _ => "outside the allowed roots");

        Assert.False(inspected);
        Assert.True(c[0].Exists);
        Assert.NotNull(c[0].LastWriteUtc);
        Assert.Null(c[0].SymbolCount);
        Assert.Contains("not opened", c[0].Note);
    }

    [Fact]
    public void Build_NotATmc_ReportsTheInspectorReason()
    {
        string path = Path.Combine(_root, "Machine", "PLC1", "PLC1.tmc");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<NotATmc/>");

        var c = HmiTmcCandidates.Build(
            [new PlcProjectXml("Machine", _tsproj, "PLC1", NodeXml("PLC1", @"PLC1\PLC1.tmc"), null)],
            TmcFile.Inspect, AllowAll);

        Assert.True(c[0].Exists);
        Assert.Null(c[0].SymbolCount);
        Assert.Contains("TcModuleClass", c[0].Note);
    }

    // ── HmiCoordinator.ListPlcTmcCandidates ──────────────────────────────────

    private FakeTwinCatSession Session() => new()
    {
        IsConnected = true,
        SolutionOpen = true,
        ActiveSlnPath = Path.Combine(_root, "Machine.sln"),
    };

    [Fact]
    public void List_NoSolution_IsAnErrorWithoutWalkingTheTree()
    {
        var fake = Session();
        fake.SolutionOpen = false;

        var (summary, json) = new HmiCoordinator(fake).ListPlcTmcCandidates();

        Assert.StartsWith("[Error]", summary);
        Assert.Equal("{}", json);
        Assert.DoesNotContain("ListPlcProjectXml()", fake.CallLog);
    }

    [Fact]
    public void List_IsReadOnly_AndNeedsNoHmiProject()
    {
        TmcOnDisk(@"PLC1\PLC1.tmc", symbols: 2);
        var fake = Session();
        fake.PlcProjectXmls.Add(new PlcProjectXml("Machine", _tsproj, "PLC1", NodeXml("PLC1", @"PLC1\PLC1.tmc"), null));

        var (summary, _) = new HmiCoordinator(fake).ListPlcTmcCandidates();

        Assert.DoesNotContain("[Error]", summary);
        Assert.Equal(["ListPlcProjectXml()"], fake.CallLog);
    }

    [Fact]
    public void List_ReportsCandidatesAndNeverChoosesOne()
    {
        string plc1 = TmcOnDisk(@"PLC1\PLC1.tmc", symbols: 2);
        var fake = Session();
        fake.PlcProjectXmls.Add(new PlcProjectXml("Machine", _tsproj, "PLC1", NodeXml("PLC1", @"PLC1\PLC1.tmc"), null));
        fake.PlcProjectXmls.Add(new PlcProjectXml("Machine", _tsproj, "PLC2", NodeXml("PLC2", @"PLC2\PLC2.tmc"), null));

        var (summary, json) = new HmiCoordinator(fake).ListPlcTmcCandidates();

        Assert.Contains("2 PLC project(s)", summary);
        Assert.Contains("1 with an existing .tmc", summary);
        Assert.Contains(plc1 + " (2 symbols)", summary);
        Assert.Contains("(missing)", summary);
        Assert.Contains("Candidates only", summary);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("existing").GetInt32());
        JsonElement first = doc.RootElement.GetProperty("candidates")[0];
        Assert.Equal("Machine", first.GetProperty("tcProject").GetString());
        Assert.Equal("PLC1", first.GetProperty("plcProject").GetString());
        Assert.Equal(@"PLC1\PLC1.tmc", first.GetProperty("relativeTmcPath").GetString());
        Assert.Equal(plc1, first.GetProperty("absolutePath").GetString());
        Assert.True(first.GetProperty("exists").GetBoolean());
        Assert.Equal(2, first.GetProperty("symbolCount").GetInt32());
        Assert.Equal(JsonValueKind.String, first.GetProperty("lastWriteUtc").ValueKind);
    }

    [Fact]
    public void List_NoPlcProject_SaysSo()
    {
        var (summary, json) = new HmiCoordinator(Session()).ListPlcTmcCandidates();

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("No PLC project", summary);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void List_ComFailure_IsAnError()
    {
        var fake = Session();
        fake.ListPlcProjectXmlThrows = new InvalidOperationException("XAE busy");

        var (summary, json) = new HmiCoordinator(fake).ListPlcTmcCandidates();

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("XAE busy", summary);
        Assert.Equal("{}", json);
    }
}
