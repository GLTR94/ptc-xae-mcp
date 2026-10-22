using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #152 D-15: an EtherCAT node that already exists is an error — never "Added", never
/// adopted, and its children are not created again underneath it. A failure after the first node
/// lists what this call created.
/// </summary>
public class ReadBackEtherCatTests
{
    private const string Master = "TIID^Device 1 (EtherCAT)";

    private static (FakeSysManTree Tree, EtherCatNodeAdder Adder, List<string> Ports) Setup()
    {
        var tree = new FakeSysManTree();
        tree.Add("TIID");
        tree.Add(Master, itemType: 2, subType: 111);
        var ports = new List<string>();
        var adder = new EtherCatNodeAdder(tree, (node, port, name, _) => ports.Add($"{node.Path}:{port}"));
        return (tree, adder, ports);
    }

    [Fact]
    public void AddBox_ExistingName_IsErrorAndCreatesNothing()
    {
        var (tree, adder, _) = Setup();
        tree.Add($"{Master}^Term 1 (EK1100)", itemType: 5, subType: 9099);
        var spec = new EtherCatBoxSpec("EK1100", "Term 1 (EK1100)",
            Children: [new EtherCatBoxSpec("EL1008", "Term 2 (EL1008)")]);

        var act = () => adder.AddBox(tree.Get(Master), spec, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*nothing was added*");
        tree.CreateAttempts.Should().BeEmpty();
        tree.Exists($"{Master}^Term 1 (EK1100)^Term 2 (EL1008)").Should().BeFalse();
    }

    [Fact]
    public void AddBox_Subtree_CreatesEveryNodeAndAppliesPort()
    {
        var (tree, adder, ports) = Setup();
        var spec = new EtherCatBoxSpec("EK1100", "Term 1 (EK1100)", "A",
            [new EtherCatBoxSpec("EL1008", "Term 2 (EL1008)")]);

        int count = adder.AddBox(tree.Get(Master), spec, null);

        count.Should().Be(2);
        adder.Created.Should().Equal($"{Master}^Term 1 (EK1100)", $"{Master}^Term 1 (EK1100)^Term 2 (EL1008)");
        ports.Should().Equal($"{Master}^Term 1 (EK1100):A");
    }

    [Fact]
    public void AddBox_ChildFails_ListsWhatWasCreated()
    {
        var (tree, adder, _) = Setup();
        tree.RejectedVInfos.Add("EL9999");
        var spec = new EtherCatBoxSpec("EK1100", "Term 1 (EK1100)",
            Children: [new EtherCatBoxSpec("EL9999", "Term 2 (EL9999)")]);

        var act = () => adder.AddBox(tree.Get(Master), spec, null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Already created by this call: {Master}^Term 1 (EK1100)*NOT saved*");
    }

    [Fact]
    public void AddBox_CreateThrowsButNodeMatches_IsAdoptedWithoutSecondCreate()
    {
        var (tree, adder, _) = Setup();
        tree.CreateChildThrowsAfterCreating.Add("Term 1 (EL1008)");

        int count = adder.AddBox(tree.Get(Master), new EtherCatBoxSpec("EL1008", "Term 1 (EL1008)"), null);

        count.Should().Be(1);
        tree.CreateAttempts.Should().ContainSingle();
    }

    // Issue #190: same shape as TwinCatComSession.AddEtherCatBox/AddEtherCatDevice — a fresh adder
    // per attempt inside the outer busy retry. Without the guard the replay finds the node the first
    // attempt created and reports "already exists".
    private static T InOuterBusyRetry<T>(Func<T> attempt) =>
        ComCall.Run(attempt, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1), findBlockingDialog: () => null);

    private static EtherCatNodeAdder NewAdder(FakeSysManTree tree) => new(tree, (_, _, _, _) => { });

    [Fact]
    public void AddBox_BusyAfterCreate_IsFinalAndNeverReplayed()
    {
        var (tree, _, _) = Setup();
        tree.NameReadBusyOnce.Add("Term 1 (EK1100)");
        var spec = new EtherCatBoxSpec("EK1100", "Term 1 (EK1100)");

        var act = () => InOuterBusyRetry(() => NewAdder(tree).AddBox(tree.Get(Master), spec, null));

        act.Should().Throw<XaeBlockedException>()
            .WithMessage($"*busy*possibly also '{Master}^Term 1 (EK1100)'*NOT saved*not repeated*");
        tree.CreateAttempts.Should().ContainSingle("a busy XAE after the CreateChild must not replay it");
    }

    [Fact]
    public void AddBox_BusyInTheSubtree_IsFinalAndListsTheCreatedParent()
    {
        var (tree, _, _) = Setup();
        tree.NameReadBusyOnce.Add("Term 2 (EL1008)");
        var spec = new EtherCatBoxSpec("EK1100", "Term 1 (EK1100)",
            Children: [new EtherCatBoxSpec("EL1008", "Term 2 (EL1008)")]);

        var act = () => InOuterBusyRetry(() => NewAdder(tree).AddBox(tree.Get(Master), spec, null));

        act.Should().Throw<XaeBlockedException>()
            .WithMessage($"*Already created by this call: {Master}^Term 1 (EK1100) (possibly also '{Master}^Term 1 (EK1100)^Term 2 (EL1008)'*");
        tree.CreateAttempts.Should().HaveCount(2);
    }

    [Fact]
    public void AddBox_BusyBeforeAnythingWasCreated_StaysReplayable()
    {
        // A rejected CreateChild did not run: the outer retry may repeat it, and the add then succeeds.
        var (tree, _, _) = Setup();
        int attempts = 0;
        var spec = new EtherCatBoxSpec("EK1100", "Term 1 (EK1100)");

        int count = InOuterBusyRetry(() =>
        {
            if (attempts++ == 0)
                throw new System.Runtime.InteropServices.COMException("fake: call rejected", unchecked((int)0x80010001));
            return NewAdder(tree).AddBox(tree.Get(Master), spec, null);
        });

        count.Should().Be(1);
        tree.CreateAttempts.Should().ContainSingle();
    }

    [Fact]
    public void AddDevice_BusyAfterCreate_IsFinalAndNeverReplayed()
    {
        var (tree, _, _) = Setup();
        tree.NameReadBusyOnce.Add("Device 2 (EtherCAT)");

        var act = () => InOuterBusyRetry(() => NewAdder(tree).AddDevice(tree.Get("TIID"), "Device 2 (EtherCAT)", 111, "master"));

        act.Should().Throw<XaeBlockedException>().WithMessage("*possibly also 'TIID^Device 2 (EtherCAT)'*");
        tree.CreateAttempts.Should().ContainSingle();
    }

    [Fact]
    public void AddDevice_ExistingName_IsError()
    {
        var (tree, adder, _) = Setup();

        var act = () => adder.AddDevice(tree.Get("TIID"), "Device 1 (EtherCAT)", 111, "master");

        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*nothing was created*");
        tree.CreateAttempts.Should().BeEmpty();
    }

    [Fact]
    public void AddDevice_New_ReturnsCreatedPath()
    {
        var (tree, adder, _) = Setup();

        adder.AddDevice(tree.Get("TIID"), "Device 2 (EtherCAT)", 111, "master")
            .Should().Be("TIID^Device 2 (EtherCAT)");
    }
}
