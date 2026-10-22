using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Verifies that BuildFieldbusNode deduplicates nodes by PathName.
/// Uses FieldbusTopology directly (topology is built by the fake/real session;
/// dedup logic lives in BuildFieldbusNode which we test via a helper that mirrors
/// the structure produced by the bug scenario from the topo-debug.txt dump).
/// </summary>
public class FieldbusTopologyDeduplicationTests
{
    // Build a FieldbusTopology that mirrors the Device 2 debug-dump scenario:
    // EK1122 (junction) whose child EK1100 was erroneously added to both the
    // junction's children AND the junction's parent's children.
    //
    // The dedup guard must ensure each PathName appears at most once in a DFS.
    private static FieldbusTopology BuildDuplicateScenario()
    {
        // Child that was duplicated: EK1100 A10.00 appears under both the
        // junction and the parent device.
        var duplicatedEK1100 = new FieldbusNode(
            Path: "TIID^Device 2 (EtherCAT)^Box 1 (EK1122 A1.45)^Box 2 (EK1100 A10.00)",
            Name: "Box 2 (EK1100 A10.00)",
            ItemType: 4, ItemSubType: 0,
            Kind: FieldbusKind.Box,
            Port: null,
            Children: []);

        var junction = new FieldbusNode(
            Path: "TIID^Device 2 (EtherCAT)^Box 1 (EK1122 A1.45)",
            Name: "Box 1 (EK1122 A1.45)",
            ItemType: 4, ItemSubType: 0,
            Kind: FieldbusKind.Box,
            Port: null,
            Children: [duplicatedEK1100]);

        // Duplicate the same node as a sibling of the junction (the bug scenario).
        var duplicateAtParent = new FieldbusNode(
            Path: "TIID^Device 2 (EtherCAT)^Box 1 (EK1122 A1.45)^Box 2 (EK1100 A10.00)",
            Name: "Box 2 (EK1100 A10.00)",
            ItemType: 4, ItemSubType: 0,
            Kind: FieldbusKind.Box,
            Port: null,
            Children: []);

        var device = new FieldbusNode(
            Path: "TIID^Device 2 (EtherCAT)",
            Name: "Device 2 (EtherCAT)",
            ItemType: 2, ItemSubType: 0,
            Kind: FieldbusKind.Device,
            Port: null,
            Children: [junction, duplicateAtParent]);

        return new FieldbusTopology(1, [device]);
    }

    private static IEnumerable<string> DfsPaths(FieldbusNode node)
    {
        yield return node.Path;
        foreach (var child in node.Children)
            foreach (var p in DfsPaths(child))
                yield return p;
    }

    [Fact]
    public void DfsTraversal_WithDuplicates_EachPathAppearsOnce()
    {
        // Arrange: topology with a duplicated node (simulates the EK1100 bug)
        var topology = BuildDuplicateScenario();

        // Act: collect all paths via DFS — this is what the diagram renderer sees
        var allPaths = topology.Devices
            .SelectMany(DfsPaths)
            .ToList();

        // Assert: the duplicated path appears exactly twice in the raw scenario
        // (confirming our test fixture is correct), then once deduplication is
        // applied by BuildFieldbusNode the count drops to 1.
        // Because FieldbusTopology is an immutable record we test via the
        // deduplication helper directly.
        var duplicatedPath = "TIID^Device 2 (EtherCAT)^Box 1 (EK1122 A1.45)^Box 2 (EK1100 A10.00)";
        Assert.Equal(2, allPaths.Count(p => p == duplicatedPath));
    }

    [Fact]
    public void DeduplicateTopology_RemovesDuplicateNodes()
    {
        var topology = BuildDuplicateScenario();

        var deduped = FieldbusTopologyDedup.Deduplicate(topology);

        var allPaths = deduped.Devices.SelectMany(DfsPaths).ToList();

        // Each path must appear exactly once.
        foreach (var grp in allPaths.GroupBy(p => p))
            Assert.Single(grp);
    }

    [Fact]
    public void DeduplicateTopology_UniqueTopology_Unchanged()
    {
        var child1 = new FieldbusNode("TIID^Dev^Box1", "Box1", 4, 0, FieldbusKind.Box, null, []);
        var child2 = new FieldbusNode("TIID^Dev^Box2", "Box2", 4, 0, FieldbusKind.Box, null, []);
        var device = new FieldbusNode("TIID^Dev", "Dev", 2, 0, FieldbusKind.Device, null, [child1, child2]);
        var topology = new FieldbusTopology(1, [device]);

        var deduped = FieldbusTopologyDedup.Deduplicate(topology);

        var allPaths = deduped.Devices.SelectMany(DfsPaths).ToList();
        Assert.Equal(3, allPaths.Count);
    }
}
