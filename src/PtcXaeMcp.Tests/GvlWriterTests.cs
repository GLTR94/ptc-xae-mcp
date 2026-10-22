using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;
using PtcXaeMcp.WinForms.TwinCat.Apply;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #155 step 4, E-2: an existing GVL is never deleted implicitly, and even an explicit
/// overwrite deletes only the node at the exact target path.
/// </summary>
public class GvlWriterTests
{
    private const string Folder = "TIPC^Plc^Plc Project^GVLs";
    private const string Target = Folder + "^GVL_IO";
    private const string OldDecl = "VAR_GLOBAL\n\tExisting : BOOL;\nEND_VAR";
    private const string NewDecl = "VAR_GLOBAL\n\tI_New AT %I* : BOOL;\nEND_VAR";

    private static (FakeSysManTree Tree, GvlTarget Target) Fixture(bool withExistingGvl)
    {
        var tree = new FakeSysManTree();
        tree.Add("TIPC");
        tree.Add("TIPC^Plc", itemType: 56);
        tree.Add("TIPC^Plc^Plc Project", itemType: PlcItemType.PlcApp);
        tree.Add(Folder, itemType: PlcItemType.Folder);
        tree.Add("TIPC^Plc^Plc Project^POUs", itemType: PlcItemType.Folder);
        if (withExistingGvl)
            tree.Add(Target, itemType: PlcItemType.Gvl).Xml = OldDecl;

        var plc = new PlcTarget("Plc", "TIPC^Plc", "TIPC^Plc^Plc Project");
        return (tree, new GvlTarget(plc, Folder, "GVL_IO"));
    }

    [Fact]
    public void Greenfield_CreatesTheGvlAtTheExactPath()
    {
        var (tree, target) = Fixture(withExistingGvl: false);
        var writer = new GvlWriter(tree);

        var created = writer.Write(target, NewDecl, overwriteExistingGvl: false);

        created.Path.Should().Be(Target);
        created.ItemType.Should().Be(PlcItemType.Gvl);
        tree.Get(Target).Xml.Should().Be(NewDecl);
        tree.Mutations.Should().Equal($"create {Target}");
        writer.CreatedThisRun(Target).Should().BeTrue();
    }

    [Fact]
    public void Collision_WithoutFlag_RefusesAndDeletesNothing()
    {
        var (tree, target) = Fixture(withExistingGvl: true);
        var writer = new GvlWriter(tree);

        Action act = () => writer.Write(target, NewDecl, overwriteExistingGvl: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{Target}'*")
            .WithMessage("*Nothing was deleted*")
            .WithMessage("*overwriteExistingGvl=true*");
        tree.Mutations.Should().BeEmpty();
        tree.Get(Target).Xml.Should().Be(OldDecl, "the existing declarations stay untouched");
    }

    [Fact]
    public void CheckWritable_IsThePreflightOfTheSameRule()
    {
        var (tree, target) = Fixture(withExistingGvl: true);
        var writer = new GvlWriter(tree);

        writer.Invoking(w => w.CheckWritable(target, overwriteExistingGvl: false))
            .Should().Throw<InvalidOperationException>().WithMessage($"*'{Target}'*");
        writer.Invoking(w => w.CheckWritable(target, overwriteExistingGvl: true)).Should().NotThrow();
        tree.Mutations.Should().BeEmpty("the pre-flight never changes the tree");
    }

    [Fact]
    public void Overwrite_DeletesOnlyTheExactNode()
    {
        var (tree, target) = Fixture(withExistingGvl: true);
        // Same-named GVLs elsewhere: another PLC, and another folder of this PLC. The old fuzzy
        // search could pick either of them as the delete victim.
        tree.Add("TIPC^Plc2", itemType: 56);
        tree.Add("TIPC^Plc2^Plc2 Project", itemType: PlcItemType.PlcApp);
        tree.Add("TIPC^Plc2^Plc2 Project^GVLs", itemType: PlcItemType.Folder);
        tree.Add("TIPC^Plc2^Plc2 Project^GVLs^GVL_IO", itemType: PlcItemType.Gvl).Xml = OldDecl;
        tree.Add("TIPC^Plc^Plc Project^Legacy^GVL_IO", itemType: PlcItemType.Gvl).Xml = OldDecl;
        var warnings = new List<string>();
        var writer = new GvlWriter(tree, warn: warnings.Add);

        writer.Write(target, NewDecl, overwriteExistingGvl: true);

        tree.Deleted.Should().Equal(Target);
        tree.Mutations.Should().Equal($"delete {Target}", $"create {Target}");
        tree.Get(Target).Xml.Should().Be(NewDecl);
        tree.Get("TIPC^Plc2^Plc2 Project^GVLs^GVL_IO").Xml.Should().Be(OldDecl);
        tree.Get("TIPC^Plc^Plc Project^Legacy^GVL_IO").Xml.Should().Be(OldDecl);
        warnings.Should().ContainSingle(w => w.Contains(Target) && w.Contains("overwriteExistingGvl=true"));
    }

    [Fact]
    public void Overwrite_RefusesWhenTheTargetIsNotAGvl()
    {
        var (tree, target) = Fixture(withExistingGvl: false);
        tree.Add(Target, itemType: PlcItemType.PouProgram);
        var writer = new GvlWriter(tree);

        writer.Invoking(w => w.Write(target, NewDecl, overwriteExistingGvl: true))
            .Should().Throw<InvalidOperationException>().WithMessage("*not a plain GVL*");
        tree.Mutations.Should().BeEmpty();
    }

    [Fact]
    public void Overwrite_RefusesWhenTheTargetKindIsUnknown()
    {
        var (tree, target) = Fixture(withExistingGvl: false);
        tree.Add(Target, itemType: -1);
        var writer = new GvlWriter(tree);

        writer.Invoking(w => w.Write(target, NewDecl, overwriteExistingGvl: true))
            .Should().Throw<InvalidOperationException>().WithMessage("*refusing to delete*");
        tree.Mutations.Should().BeEmpty();
    }

    [Fact]
    public void ReapplyOfAGvlThisRunCreated_IsAllowedWithoutTheFlag()
    {
        var (tree, target) = Fixture(withExistingGvl: false);
        var writer = new GvlWriter(tree);

        writer.Write(target, OldDecl, overwriteExistingGvl: false);
        writer.Invoking(w => w.CheckWritable(target, overwriteExistingGvl: false)).Should().NotThrow();
        writer.Write(target, NewDecl, overwriteExistingGvl: false);

        tree.Mutations.Should().Equal($"create {Target}", $"delete {Target}", $"create {Target}");
        tree.Get(Target).Xml.Should().Be(NewDecl);
    }

    [Fact]
    public void AnotherWritersGvl_IsNotSelfCreated()
    {
        // "Created this run" is per writer (= per apply run), not a global marker.
        var (tree, target) = Fixture(withExistingGvl: false);
        new GvlWriter(tree).Write(target, OldDecl, overwriteExistingGvl: false);

        var nextRun = new GvlWriter(tree);
        nextRun.Invoking(w => w.Write(target, NewDecl, overwriteExistingGvl: false))
            .Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
        tree.Get(Target).Xml.Should().Be(OldDecl);
    }

    [Fact]
    public void CreateChildFailure_IsReportedAndNeverAnsweredWithADelete()
    {
        var (tree, target) = Fixture(withExistingGvl: false);
        // TwinCAT names are unique per PLC project: a same-named object in another folder
        // makes CreateChild fail. The old code then deleted whatever its fuzzy search found.
        tree.Add("TIPC^Plc^Plc Project^POUs^GVL_IO", itemType: PlcItemType.Gvl).Xml = OldDecl;
        tree.CreateChildThrows["GVL_IO"] = new InvalidOperationException("Object name already in use");
        var writer = new GvlWriter(tree);

        writer.Invoking(w => w.Write(target, NewDecl, overwriteExistingGvl: true))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Object name already in use*")
            .WithMessage("*Nothing was deleted*")
            .WithMessage("*'TIPC^Plc^Plc Project^POUs^GVL_IO'*");
        tree.Mutations.Should().BeEmpty();
        tree.Get("TIPC^Plc^Plc Project^POUs^GVL_IO").Xml.Should().Be(OldDecl);
        writer.CreatedThisRun(Target).Should().BeFalse();
    }

    [Fact]
    public void MissingFolder_Throws()
    {
        var (tree, _) = Fixture(withExistingGvl: false);
        var plc = new PlcTarget("Plc", "TIPC^Plc", "TIPC^Plc^Plc Project");
        var writer = new GvlWriter(tree);

        writer.Invoking(w => w.Write(new GvlTarget(plc, "TIPC^Plc^Plc Project^Nope", "GVL_IO"), NewDecl, false))
            .Should().Throw<InvalidOperationException>().WithMessage("*not found*");
        tree.Mutations.Should().BeEmpty();
    }
}
