using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #152 C-10 / C-19: build success needs more than LastBuildInfo; Error List by ErrorLevel.</summary>
public class BuildTruthTests
{
    private static readonly DateTime Start = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private static BuildArtifactInfo Artifact(DateTime written) =>
        new(@"C:\p\_CompileInfo\x.compileinfo", written, 1);

    [Fact]
    public void AllEvidencePresent_Succeeds()
    {
        BuildTruth.FailureReason(0, "0 errors, 2 warnings", Artifact(Start.AddSeconds(5)), Start, expectsPlcCompile: true)
            .Should().BeNull();
    }

    [Fact]
    public void LastBuildInfoNonZero_Fails()
    {
        BuildTruth.FailureReason(1, "0 errors, 0 warnings", Artifact(Start.AddSeconds(5)), Start, true)
            .Should().Contain("LastBuildInfo=1");
    }

    [Fact]
    public void NoSummaryLine_FailsEvenWithLastBuildInfoZero()
    {
        BuildTruth.FailureReason(0, null, Artifact(Start.AddSeconds(5)), Start, true)
            .Should().Contain("no compiler summary line");
    }

    [Fact]
    public void SummaryWithErrors_Fails()
    {
        BuildTruth.FailureReason(0, "3 Fehler, 0 Warnungen", Artifact(Start.AddSeconds(5)), Start, true)
            .Should().Contain("3 Fehler");
    }

    [Fact]
    public void StaleArtifact_Fails()
    {
        BuildTruth.FailureReason(0, "0 errors, 0 warnings", Artifact(Start.AddMinutes(-10)), Start, true)
            .Should().Contain("stale");
        BuildTruth.FailureReason(0, "0 errors, 0 warnings", null, Start, true)
            .Should().Contain("no compile artifact");
    }

    [Fact]
    public void ArtifactWithinTimestampSlack_IsFresh()
    {
        BuildTruth.FailureReason(0, "0 errors, 0 warnings", Artifact(Start.AddSeconds(-1)), Start, true)
            .Should().BeNull();
    }

    [Fact]
    public void SolutionWithoutPlc_LastBuildInfoDecides()
    {
        BuildTruth.FailureReason(0, null, null, Start, expectsPlcCompile: false).Should().BeNull();
    }

    [Theory]
    [InlineData(4, "C0077: Unknown type: 'FOO'", "error")]
    [InlineData(2, "C0373: Unused variable", "warning")]
    [InlineData(1, "Build started: Application: PLC", "message")]
    [InlineData(1, "error occurred during download (reported as a message)", "message")]
    [InlineData(null, "Some warning text", "warning")]
    [InlineData(null, "3 Fehler gefunden", "error")]
    [InlineData(null, "Build complete", "message")]
    public void ClassifyErrorLevel_UsesLevelThenKeywords(int? level, string description, string expected)
    {
        BuildTruth.ClassifyErrorLevel(level, description).Should().Be(expected);
    }

    // Issue #178, seen live on TcXaeShell 4026.22: PLC compiler errors AND warnings both arrive
    // with ErrorLevel 2, so the level alone cannot tell them apart; the compiler line in the
    // Output window ("file(line) : error: C0046: <description>") decides.
    private const string CompilerOutput =
        "------ Build started: Application: Fixture.FixturePlc -------\r\n" +
        @"C:\p\POUs\PRG_Scratch.TcPOU(4) : warning: C0373: probe warning" + "\r\n" +
        @"C:\p\POUs\PRG_Scratch.TcPOU(5) : : probe info" + "\r\n" +
        @"C:\p\POUs\PRG_Scratch.TcPOU(6) : error: C0046: Identifier 'nX' not defined" + "\r\n" +
        @"C:\p\POUs\PRG_Scratch.TcPOU(6) : error: C0018: 'nX' is no valid assignment target" + "\r\n" +
        "Build complete -- 2 errors, 1 warnings : No download possible";

    [Theory]
    [InlineData(2, "Identifier 'nX' not defined", "error")]
    [InlineData(2, "'nX' is no valid assignment target", "error")]
    [InlineData(2, "probe warning", "warning")]
    [InlineData(1, "probe info", "message")]
    [InlineData(4, "not in the output at all", "error")]
    [InlineData(2, "not in the output at all", "warning")]
    public void ClassifyErrorItem_Level2IsDecidedByTheCompilerLine(int? level, string description, string expected)
    {
        BuildTruth.ClassifyErrorItem(level, description, CompilerOutput).Should().Be(expected);
    }

    [Fact]
    public void ClassifyErrorItem_WithoutOutput_FallsBackToTheLevel()
    {
        BuildTruth.ClassifyErrorItem(2, "Identifier 'nX' not defined", null).Should().Be("warning");
        BuildTruth.ClassifyErrorItem(null, "3 Fehler gefunden", "").Should().Be("error");
    }

    [Fact]
    public void ClassifyErrorItem_TheLatestCompilerLineWins()
    {
        string output = "a.TcPOU(1) : warning: C0001: same text\nb.TcPOU(2) : error: C0002: same text";
        BuildTruth.ClassifyErrorItem(2, "same text", output).Should().Be("error");
    }

    [Fact]
    public void ClassifyErrorItem_MatchesTheWholeDescription_NotASuffix()
    {
        string output = "a.TcPOU(1) : error: C0046: Identifier 'nX' not defined";
        BuildTruth.ClassifyErrorItem(2, "not defined", output).Should().Be("warning");
    }

    [Fact]
    public void Coordinator_ShowsTheReasonForAFailedBuild()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.FakeBuildPlcResult = new BuildResult(false, 0, [], "", null, null,
            FailureReason: "LastBuildInfo reports success, but this build's output has no compiler summary line");

        string text = new BuildCoordinator(fake).BuildPlc(null);

        text.Should().StartWith("Build failed").And.Contain("Reason: LastBuildInfo reports success");
    }

    // ---- ExtractCompilerSummary / CapBuildLog (issue #157 item 7) ---------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Build started: Project: Plc\nno summary here")]
    public void ExtractCompilerSummary_NoSummaryLine_ReturnsNull(string? log)
    {
        BuildTruth.ExtractCompilerSummary(log).Should().BeNull();
    }

    [Fact]
    public void ExtractCompilerSummary_SingleLine_ReturnsIt()
    {
        BuildTruth.ExtractCompilerSummary("Compile complete -- 0 errors, 3 warnings\nDone.")
            .Should().Be("0 errors, 3 warnings");
    }

    [Fact]
    public void ExtractCompilerSummary_SeveralLines_LastOneWins()
    {
        const string log = "Plc1: 2 errors, 1 warning\nPlc2: 0 errors, 0 warnings\nTotal: 2 errors, 1 warning";

        BuildTruth.ExtractCompilerSummary(log).Should().Be("2 errors, 1 warning");
    }

    [Fact]
    public void ExtractCompilerSummary_GermanXae_IsRecognisedAndCounted()
    {
        string? summary = BuildTruth.ExtractCompilerSummary("Kompilierung abgeschlossen -- 1 Fehler, 4 Warnungen");

        summary.Should().Be("1 Fehler, 4 Warnungen");
        BuildTruth.SummaryErrorCount(summary!).Should().Be(1);
    }

    [Fact]
    public void CapBuildLog_AtOrBelowLimit_IsUnchanged()
    {
        string log = new('x', BuildTruth.MaxBuildLogChars);

        BuildTruth.CapBuildLog(log).Should().BeSameAs(log);
    }

    [Fact]
    public void CapBuildLog_OverLimit_KeepsTheTailWithANote()
    {
        string head = new('h', 500);
        string tail = new string('t', BuildTruth.MaxBuildLogChars - 20) + "0 errors, 0 warnings";
        string capped = BuildTruth.CapBuildLog(head + tail);

        capped.Should().StartWith("[… 500 earlier characters omitted");
        capped[(capped.IndexOf('\n') + 1)..].Should().Be(tail, "exactly the last MaxBuildLogChars characters are kept");
    }
}
