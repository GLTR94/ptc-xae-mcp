using PtcXaeMcp.WinForms.TwinCat.Schema;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Coverage for the pure "skipped as a consequence" grouping extracted from
/// NativeTwinCatRunner.LogMissingEthercatSummary (BuildSkippedNodeGroups / FindOwningDirectFailure)
/// — Hardening 9, step 1.
/// </summary>
public class MissingNodeReportTests
{
    private static MissingEthercatNodeInfo Node(string name, string schemaPath, bool isDirectFailure)
        => new()
        {
            Name = name,
            SchemaPath = schemaPath,
            IsDirectFailure = isDirectFailure
        };

    [Fact]
    public void GroupSkipped_NoSkippedNodes_ReturnsEmpty()
    {
        var directFailures = new List<MissingEthercatNodeInfo> { Node("A", "Root^A", true) };
        var groups = MissingNodeReport.GroupSkipped(directFailures, []);

        groups.Should().BeEmpty();
    }

    [Fact]
    public void GroupSkipped_ChildOfFailedNode_GroupedUnderThatFailure()
    {
        var directFailures = new List<MissingEthercatNodeInfo> { Node("A", "Root^A", true) };
        var skipped = new List<MissingEthercatNodeInfo> { Node("A1", "Root^A^A1", false) };

        var groups = MissingNodeReport.GroupSkipped(directFailures, skipped).ToList();

        groups.Should().HaveCount(1);
        groups[0].Cause.Should().NotBeNull();
        groups[0].Cause!.Name.Should().Be("A");
        groups[0].Nodes.Should().ContainSingle(n => n.Name == "A1");
    }

    [Fact]
    public void GroupSkipped_NoOwningFailureFound_GroupedUnderNullCause()
    {
        var directFailures = new List<MissingEthercatNodeInfo> { Node("A", "Root^A", true) };
        var skipped = new List<MissingEthercatNodeInfo> { Node("B1", "Root^B^B1", false) };

        var groups = MissingNodeReport.GroupSkipped(directFailures, skipped).ToList();

        groups.Should().HaveCount(1);
        groups[0].Cause.Should().BeNull();
    }

    [Fact]
    public void GroupSkipped_PicksDeepestMatchingAncestorAsCause()
    {
        // Two direct failures where one's schema path is a prefix of the other's; the
        // skipped node under the deeper one must attribute to the deeper (longer) match,
        // not the shallower ancestor.
        var directFailures = new List<MissingEthercatNodeInfo>
        {
            Node("A", "Root^A", true),
            Node("B", "Root^A^B", true)
        };
        var skipped = new List<MissingEthercatNodeInfo> { Node("B1", "Root^A^B^B1", false) };

        var groups = MissingNodeReport.GroupSkipped(directFailures, skipped).ToList();

        groups.Should().HaveCount(1);
        groups[0].Cause!.Name.Should().Be("B");
    }

    [Fact]
    public void GroupSkipped_MultipleSkippedUnderSameCause_GroupedTogether()
    {
        var directFailures = new List<MissingEthercatNodeInfo> { Node("A", "Root^A", true) };
        var skipped = new List<MissingEthercatNodeInfo>
        {
            Node("A1", "Root^A^A1", false),
            Node("A2", "Root^A^A2", false)
        };

        var groups = MissingNodeReport.GroupSkipped(directFailures, skipped).ToList();

        groups.Should().HaveCount(1);
        groups[0].Nodes.Should().HaveCount(2);
    }

    [Fact]
    public void GroupSkipped_OrdersLargestGroupFirst()
    {
        var directFailures = new List<MissingEthercatNodeInfo>
        {
            Node("A", "Root^A", true),
            Node("B", "Root^B", true)
        };
        var skipped = new List<MissingEthercatNodeInfo>
        {
            Node("B1", "Root^B^B1", false),
            Node("A1", "Root^A^A1", false),
            Node("A2", "Root^A^A2", false)
        };

        var groups = MissingNodeReport.GroupSkipped(directFailures, skipped).ToList();

        groups[0].Cause!.Name.Should().Be("A", "group with 2 nodes sorts before the group with 1");
        groups[0].Nodes.Should().HaveCount(2);
    }
}
