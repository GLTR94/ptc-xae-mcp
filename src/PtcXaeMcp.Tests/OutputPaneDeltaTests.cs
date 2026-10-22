using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #152 C-10/C-21: what an Output-window pane gained during one build.</summary>
public class OutputPaneDeltaTests
{
    [Fact]
    public void AppendedPane_ReturnsOnlyTheNewLines()
    {
        var mark = OutputPaneDelta.Mark("old line 1\nold line 2\n");
        OutputPaneDelta.Appended(mark, "old line 1\nold line 2\nnew 0 Fehler, 0 Warnungen\n")
            .Should().Be("new 0 Fehler, 0 Warnungen\n");
    }

    [Fact]
    public void ClearedPane_ReturnsAllOfIt()
    {
        // Live 4026.22: the build pane is cleared when a build starts; the new output can be as
        // long as the old one — the old length must not be cut off its front.
        var mark = OutputPaneDelta.Mark("------ Build started: old ------\nCompile complete -- 0 errors, 0 warnings\nDone\n");
        string now = "------ Build started: new ------\nCompile complete -- 1 errors, 0 warnings\nDone again\n";

        OutputPaneDelta.Appended(mark, now).Should().Be(now);
    }

    [Fact]
    public void CutInsideALine_DropsThePartialLine()
    {
        var mark = OutputPaneDelta.Mark("abc");
        OutputPaneDelta.Appended(mark, "abcdef\nnext\n").Should().Be("next\n");
    }

    [Fact]
    public void NoSnapshot_ReturnsEverything()
    {
        OutputPaneDelta.Appended(null, "x\n").Should().Be("x\n");
    }
}
