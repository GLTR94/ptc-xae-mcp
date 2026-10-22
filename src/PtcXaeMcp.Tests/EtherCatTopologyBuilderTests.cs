using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.TwinCat.Apply;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #155 step 5: the EtherCAT topology builder on the ISysManTree seam. E-19 — a thrown
/// CreateChild is followed by an exact lookup of the child before any alternate vInfo is tried,
/// so a box TwinCAT added despite the error is adopted (when it matches) instead of duplicated.
/// </summary>
public class EtherCatTopologyBuilderTests
{
    private const string Master = "TIID^Dev (EtherCAT)";

    private static FakeSysManTree Tree()
    {
        var tree = new FakeSysManTree();
        tree.Add("TIID");
        tree.Add(Master, itemType: 5, subType: 111);
        return tree;
    }

    private static EthercatNode Box(string name, string vInfo, params EthercatNode[] children) =>
        new() { Name = name, VInfo = vInfo, Children = [.. children] };

    private static (EtherCatTopologyBuilder Builder, List<string> Warnings) Builder(FakeSysManTree tree)
    {
        var warnings = new List<string>();
        return (new EtherCatTopologyBuilder(tree, warn: warnings.Add), warnings);
    }

    [Fact]
    public void Greenfield_CreatesEveryNodeAtItsExactPath()
    {
        var tree = Tree();
        var (builder, _) = Builder(tree);

        builder.Build(tree.Get(Master), Box("K1 (EK1100)", "EK1100", Box("K2 (EL1008)", "EL1008"), Box("K3 (EL2008)", "EL2008")));

        tree.Mutations.Should().Equal(
            $"create {Master}^K1 (EK1100)",
            $"create {Master}^K1 (EK1100)^K2 (EL1008)",
            $"create {Master}^K1 (EK1100)^K3 (EL2008)");
        builder.MissingNodes.Should().BeEmpty();
        builder.CreatedPaths["K2 (EL1008)"].Should().Be($"{Master}^K1 (EK1100)^K2 (EL1008)");
        builder.CreatedPaths["__KEY__K2"].Should().Be($"{Master}^K1 (EK1100)^K2 (EL1008)");
    }

    [Fact]
    public void MissingEsi_RecordsADirectFailureAndSkipsTheSubtree()
    {
        var tree = Tree();
        tree.RejectedVInfos.Add("EL9999-0000-0017");
        tree.RejectedVInfos.Add("EL9999");
        var (builder, _) = Builder(tree);

        builder.Build(tree.Get(Master), Box("K1 (EK1100)", "EK1100",
            Box("K2 (EL9999)", "EL9999-0000-0017", Box("K3 (EL1008)", "EL1008"))));

        tree.CreateAttempts.Should().Equal("K1 (EK1100)|EK1100", "K2 (EL9999)|EL9999-0000-0017", "K2 (EL9999)|EL9999");
        builder.MissingNodes.Should().HaveCount(2);
        builder.MissingNodes[0].Name.Should().Be("K2 (EL9999)");
        builder.MissingNodes[0].IsDirectFailure.Should().BeTrue();
        builder.MissingNodes[0].Reason.Should().Contain("CreateChild failed");
        builder.MissingNodes[1].Name.Should().Be("K3 (EL1008)");
        builder.MissingNodes[1].IsDirectFailure.Should().BeFalse();
        tree.Exists($"{Master}^K1 (EK1100)^K2 (EL9999)").Should().BeFalse();
    }

    [Fact]
    public void RejectedRevision_FallsBackToTheBaseVInfo()
    {
        var tree = Tree();
        tree.RejectedVInfos.Add("EL1008-0000-0099");
        var (builder, warnings) = Builder(tree);

        builder.Build(tree.Get(Master), Box("K1 (EL1008)", "EL1008-0000-0099"));

        tree.CreateAttempts.Should().Equal("K1 (EL1008)|EL1008-0000-0099", "K1 (EL1008)|EL1008");
        builder.MissingNodes.Should().BeEmpty();
        warnings.Should().Contain(w => w.Contains("substitute vInfo 'EL1008'"));
    }

    [Fact]
    public void E19_ThrownCreateChildThatAddedAMatchingBox_IsAdoptedNotDuplicated()
    {
        var tree = Tree();
        tree.CreateChildThrowsAfterCreating.Add("K1 (EL1008)");
        var (builder, warnings) = Builder(tree);

        builder.Build(tree.Get(Master), Box("K1 (EL1008)", "EL1008-0000-0017"));

        // One attempt only: the base vInfo "EL1008" is never tried next to the adopted box.
        tree.CreateAttempts.Should().Equal("K1 (EL1008)|EL1008-0000-0017");
        tree.Mutations.Should().Equal($"create {Master}^K1 (EL1008)");
        builder.MissingNodes.Should().BeEmpty();
        builder.CreatedPaths["K1 (EL1008)"].Should().Be($"{Master}^K1 (EL1008)");
        warnings.Should().Contain(w => w.Contains("adopted"));
    }

    [Fact]
    public void E19_ThrownCreateChildThatAddedAMismatchingBox_FailsWithoutRetry()
    {
        var tree = Tree();
        tree.CreateChildThrowsAfterCreating.Add("K1 (EL1008)");
        tree.ProductRevisionOverride["K1 (EL1008)"] = "EL2008-0000-0017";
        var (builder, _) = Builder(tree);

        builder.Build(tree.Get(Master), Box("K1 (EL1008)", "EL1008-0000-0017", Box("K2 (EL1008)", "EL1008")));

        tree.CreateAttempts.Should().Equal("K1 (EL1008)|EL1008-0000-0017");
        builder.MissingNodes.Should().HaveCount(2);
        builder.MissingNodes[0].IsDirectFailure.Should().BeTrue();
        builder.MissingNodes[0].Reason.Should().Contain("does not match").And.Contain("EL2008-0000-0017");
        builder.CreatedPaths.Should().NotContainKey("K1 (EL1008)");
    }

    [Fact]
    public void NodeThatExistedBeforeTheRun_IsNotAdopted()
    {
        var tree = Tree();
        tree.Add($"{Master}^K1 (EK1100)", itemType: 5, subType: 9099);
        var (builder, _) = Builder(tree);

        builder.Build(tree.Get(Master), Box("K1 (EK1100)", "EK1100"));

        tree.CreateAttempts.Should().BeEmpty();
        builder.MissingNodes.Should().ContainSingle(n => n.IsDirectFailure && n.Reason.Contains("already exists"));
    }

    [Fact]
    public void CxPseudoRoot_ChildrenAreCreatedDirectlyUnderTheMaster()
    {
        var tree = Tree();
        var (builder, _) = Builder(tree);

        builder.Build(tree.Get(Master), Box("CX5140", "CX5140", Box("K1 (EK1100)", "EK1100")));

        tree.Mutations.Should().Equal($"create {Master}^K1 (EK1100)");
        builder.CreatedPaths["CX5140"].Should().Be(Master);
    }

    [Theory]
    [InlineData("EL1008-0000-0018", "EL1008", true)]
    [InlineData("EL1008-0000-0018", "EL1008-0000-0018", true)]
    [InlineData("EL1008-0000-0018", "EL1008-0000-0017", false)]
    [InlineData("EL1008-0000-0018", "EL100", false)]
    [InlineData("EL2008-0000-0017", "EL1008", false)]
    [InlineData("", "EL1008", false)]
    public void RevisionMatchesVInfo(string revision, string vInfo, bool expected) =>
        EtherCatTopologyBuilder.RevisionMatchesVInfo(revision, vInfo).Should().Be(expected);
}
