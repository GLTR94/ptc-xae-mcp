using PtcXaeMcp.WinForms.TwinCat.Schema;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Coverage for the pure previousPort candidate selection extracted from
/// NativeTwinCatRunner.SetPreviousPort (Hardening 9, step 1).
/// </summary>
public class PreviousPortSelectorTests
{
    [Fact]
    public void Select_NoCandidateForDesiredPort_ReturnsNullWithWarning()
    {
        var candidates = new[]
        {
            new PreviousPortSelector.PortCandidate("A", "1001"),
            new PreviousPortSelector.PortCandidate("B", "1002")
        };

        var selection = PreviousPortSelector.Select(candidates, "C", "");

        selection.ChosenIndex.Should().BeNull();
        selection.Warning.Should().Contain("nicht verfuegbar");
        selection.Warning.Should().Contain("A,B");
    }

    [Fact]
    public void Select_SingleLetterMatch_NoUpstreamAddr_PicksItWithoutWarning()
    {
        var candidates = new[]
        {
            new PreviousPortSelector.PortCandidate("A", "1001")
        };

        var selection = PreviousPortSelector.Select(candidates, "A", "");

        selection.ChosenIndex.Should().Be(0);
        selection.Warning.Should().BeNull();
    }

    [Fact]
    public void Select_DuplicateLetter_PrefersCandidateMatchingUpstreamPhysAddr()
    {
        // EK1122 junction scenario: two candidates share port letter 'A', distinguished by
        // PhysAddr. Must pick the one that actually connects to the intended upstream box.
        var candidates = new[]
        {
            new PreviousPortSelector.PortCandidate("A", "1001"),
            new PreviousPortSelector.PortCandidate("A", "1002")
        };

        var selection = PreviousPortSelector.Select(candidates, "A", "1002");

        selection.ChosenIndex.Should().Be(1);
        selection.Warning.Should().BeNull();
    }

    [Fact]
    public void Select_DuplicateLetter_NoPhysAddrMatch_FallsBackToFirstWithWarning()
    {
        var candidates = new[]
        {
            new PreviousPortSelector.PortCandidate("A", "1001"),
            new PreviousPortSelector.PortCandidate("A", "1002")
        };

        var selection = PreviousPortSelector.Select(candidates, "A", "9999");

        selection.ChosenIndex.Should().Be(0, "legacy fallback: first letter match when no PhysAddr matches");
        selection.Warning.Should().Contain("9999");
        selection.Warning.Should().Contain("1001");
    }

    [Fact]
    public void Select_NoUpstreamPhysAddrGiven_MultipleLetterMatches_PicksFirstNoWarning()
    {
        var candidates = new[]
        {
            new PreviousPortSelector.PortCandidate("A", "1001"),
            new PreviousPortSelector.PortCandidate("A", "1002")
        };

        var selection = PreviousPortSelector.Select(candidates, "A", "");

        selection.ChosenIndex.Should().Be(0);
        selection.Warning.Should().BeNull();
    }

    [Fact]
    public void Select_EmptyCandidateList_ReturnsNullWithKeineMarker()
    {
        var selection = PreviousPortSelector.Select([], "A", "");

        selection.ChosenIndex.Should().BeNull();
        selection.Warning.Should().Contain("<keine>");
    }
}
