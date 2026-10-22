using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.TwinCat.Apply;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #155 step 6: the linker on the ISysManTree seam. E-3 — a PLC variable is the exact
/// process-image symbol of the run's GVL, never a same-named variable of another GVL; E-17 —
/// LinkVariables is called once per point (no side-effecting probe); E-16 — the master subtree is
/// read once per run.
/// </summary>
public class LinkerTests
{
    private const string Master = "TIID^Dev (EtherCAT)";
    private const string Coupler = Master + "^K1 (EK1100)";
    private const string Terminal = Coupler + "^K2 (EL1008)";
    private const string Leaf1 = Terminal + "^Channel 1^Input";
    private const string Leaf2 = Terminal + "^Channel 2^Input";
    private const string Inputs = "TIPC^Plc^Plc Instance^PlcTask Inputs";
    private const string GvlPath = "TIPC^Plc^Plc Project^GVLs^GVL_IO";

    private static readonly EthercatNode Schema = new()
    {
        Name = "K1 (EK1100)",
        VInfo = "EK1100",
        Children = [new EthercatNode { Name = "K2 (EL1008)", VInfo = "EL1008" }],
    };

    private static FakeSysManTree Tree(params string[] imageSymbols)
    {
        var tree = new FakeSysManTree();
        tree.Add("TIID");
        tree.Add(Master, itemType: 5, subType: 111);
        tree.Add(Coupler, itemType: 5, subType: 9099);
        tree.Add(Terminal, itemType: 5, subType: 9099);
        // Channel variables are addressable by path but not listed as children (as in TwinCAT).
        tree.Add(Terminal + "^Channel 1", enumerable: false);
        tree.Add(Leaf1, enumerable: false);
        tree.Add(Terminal + "^Channel 2", enumerable: false);
        tree.Add(Leaf2, enumerable: false);
        tree.Add("TIRT");
        tree.Add("TIRT^PlcTask", itemType: 1);
        tree.Add("TIPC");
        tree.Add("TIPC^Plc", itemType: 56);
        tree.Add("TIPC^Plc^Plc Instance", itemType: 57);
        tree.Add(Inputs, itemType: 8);
        foreach (string symbol in imageSymbols) tree.Add($"{Inputs}^{symbol}", itemType: 7);
        return tree;
    }

    private static IoPoint Point(string varName, int channel = 1) => new()
    {
        TerminalName = "K2 (EL1008)",
        Channel = channel,
        Direction = "I",
        VarName = varName,
    };

    private static (Linker Linker, List<string> Warnings) Create(FakeSysManTree tree, bool strict, string gvlPath = "")
    {
        var warnings = new List<string>();
        var linker = new Linker(tree, new LinkTarget("Plc", "GVL_IO", Master, strict, gvlPath), Schema, warn: warnings.Add);
        return (linker, warnings);
    }

    [Fact]
    public void LinksTheExactSymbolOfTheRunsGvl_Once()
    {
        var tree = Tree("GVL_Other.I_Start", "GVL_IO.I_Start");
        var (linker, _) = Create(tree, strict: true);

        LinkOutcome outcome = linker.LinkAll([Point("I_Start")], failIfUnresolved: true);

        outcome.HadUnresolvedFailure.Should().BeFalse();
        outcome.Links.Should().ContainSingle().Which.PlcPath.Should().Be($"{Inputs}^GVL_IO.I_Start");
        // E-17: exactly one LinkVariables call, no probe link.
        tree.Mutations.Should().Equal($"link {Inputs}^GVL_IO.I_Start {Leaf1}");
    }

    [Fact]
    public void E3_Strict_SameNamedVariableOfAnotherGvl_IsNeverLinked()
    {
        var tree = Tree("GVL_Other.I_Start");
        tree.Add(GvlPath, itemType: 615);
        tree.Add($"{GvlPath}^I_Start");
        var (linker, _) = Create(tree, strict: true, gvlPath: GvlPath);

        LinkOutcome outcome = linker.LinkAll([Point("I_Start")], failIfUnresolved: true);

        outcome.HadUnresolvedFailure.Should().BeTrue();
        outcome.Links.Should().BeEmpty();
        outcome.Unresolved.Should().ContainSingle().Which.Should()
            .Contain("'GVL_IO.I_Start' not found in the process image").And.Contain(Inputs);
        tree.Mutations.Should().BeEmpty();
    }

    [Fact]
    public void E3_Lenient_FallsBackToTheRunsOwnGvlDeclaration_NotToAnotherGvl()
    {
        var tree = Tree("GVL_Other.I_Start");
        tree.Add(GvlPath, itemType: 615);
        tree.Add($"{GvlPath}^I_Start");
        var (linker, warnings) = Create(tree, strict: false, gvlPath: GvlPath);

        LinkOutcome outcome = linker.LinkAll([Point("I_Start")], failIfUnresolved: true);

        outcome.Links.Should().ContainSingle().Which.PlcPath.Should().Be($"{GvlPath}^I_Start");
        tree.Mutations.Should().Equal($"link {GvlPath}^I_Start {Leaf1}");
        warnings.Should().Contain(w => w.Contains("lenient"));
    }

    [Fact]
    public void AmbiguousSymbolInTwoImageFolders_IsAnError()
    {
        var tree = Tree("GVL_IO.I_Start");
        tree.Add("TIRT^FastTask", itemType: 1);
        tree.Add("TIPC^Plc^Plc Instance^FastTask Inputs^GVL_IO.I_Start", itemType: 7);
        var (linker, _) = Create(tree, strict: true);

        LinkOutcome outcome = linker.LinkAll([Point("I_Start")], failIfUnresolved: false);

        outcome.HadUnresolvedFailure.Should().BeFalse();
        outcome.Unresolved.Should().ContainSingle().Which.Should().Contain("ambiguous");
        tree.Mutations.Should().BeEmpty();
    }

    [Fact]
    public void MissingLeaf_ReasonNamesTheTriedPaths_NotAChildCount()
    {
        var tree = Tree("GVL_IO.I_Start");
        var (linker, _) = Create(tree, strict: true);

        LinkOutcome outcome = linker.LinkAll([Point("I_Start", channel: 7)], failIfUnresolved: true);

        string reason = outcome.Unresolved.Should().ContainSingle().Subject;
        reason.Should().Contain($"'{Terminal}^Channel 7^Input'").And.NotContain("ChildCount");
    }

    [Fact]
    public void E16_MasterSubtreeIsReadOnce_ForManyPoints()
    {
        var tree = Tree("GVL_IO.I_A", "GVL_IO.I_B", "GVL_IO.I_C");
        // A schema whose coupler name does not match the tree forces the name index.
        var schema = new EthercatNode { Name = "K9 (EK1100)", VInfo = "EK1100", Children = [new EthercatNode { Name = "K2 (EL1008)", VInfo = "EL1008" }] };
        var linker = new Linker(tree, new LinkTarget("Plc", "GVL_IO", Master, true), schema);

        linker.LinkAll([Point("I_A")], failIfUnresolved: true).Links.Should().HaveCount(1);
        int readsAfterFirst = tree.ChildReads;
        linker.LinkAll([Point("I_B", 2), Point("I_C")], failIfUnresolved: true).Links.Should().HaveCount(2);

        tree.ChildReads.Should().Be(readsAfterFirst);
    }

    [Fact]
    public void Strict_AmbiguousTerminalName_IsAnError()
    {
        var tree = Tree("GVL_IO.I_Start");
        tree.Add(Master + "^K5 (EK1100)", itemType: 5, subType: 9099);
        tree.Add(Master + "^K5 (EK1100)^K2 (EL1008)", itemType: 5, subType: 9099);
        var schema = new EthercatNode { Name = "Kx (EK1100)", VInfo = "EK1100", Children = [new EthercatNode { Name = "K2 (EL1008)", VInfo = "EL1008" }] };
        var linker = new Linker(tree, new LinkTarget("Plc", "GVL_IO", Master, true), schema);

        LinkOutcome outcome = linker.LinkAll([Point("I_Start")], failIfUnresolved: true);

        outcome.Unresolved.Should().ContainSingle().Which.Should().Contain("ambiguous");
        tree.Mutations.Should().BeEmpty();
    }

    [Fact]
    public void SchemaIndex_UsesTheResolvedMasterPath()
    {
        var index = Linker.BuildSchemaTerminalPathIndex(Schema, "TIID^Other (EtherCAT)");

        index["K2 (EL1008)"].Should().Be("TIID^Other (EtherCAT)^K1 (EK1100)^K2 (EL1008)");
        index["__KEY__K2"].Should().Be("TIID^Other (EtherCAT)^K1 (EK1100)^K2 (EL1008)");
    }
}
