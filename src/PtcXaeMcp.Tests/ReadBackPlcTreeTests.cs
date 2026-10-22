using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #152 B-13: delete / rename report success only after an independent re-lookup of the
/// paths — a session call that returns without effect is an [Error].
/// </summary>
public class ReadBackPlcTreeTests
{
    private static FakeTwinCatSession OpenFake() => new() { IsConnected = true, SolutionOpen = true };

    [Fact]
    public void Delete_Landed_ReportsReadBack()
    {
        var fake = OpenFake();
        var summary = new PouMembersCoordinator(fake).DeletePlcObject("^POUs^FB_Foo");

        summary.Should().StartWith("Deleted").And.Contain("no longer resolves");
        fake.CallLog.Should().ContainInOrder("DeletePlcObject(^POUs^FB_Foo)", "ProbePlcNode(^POUs^FB_Foo)");
    }

    [Fact]
    public void Delete_NoOp_IsError()
    {
        var fake = OpenFake();
        fake.NoOpMutations.Add(nameof(FakeTwinCatSession.DeletePlcObject));

        var summary = new PouMembersCoordinator(fake).DeletePlcObject("^POUs^FB_Foo");

        summary.Should().StartWith("[Error] Delete of '^POUs^FB_Foo' did not land")
            .And.Contain("still resolves");
    }

    [Fact]
    public void Delete_ReadBackUnreadable_IsErrorNotSuccess()
    {
        var fake = OpenFake();
        fake.ProbeThrowsOnceMutated = new InvalidOperationException("tree unreadable");

        var summary = new PouMembersCoordinator(fake).DeletePlcObject("^POUs^FB_Foo");

        summary.Should().StartWith("[Error]").And.Contain("not confirmed");
    }

    [Fact]
    public void Rename_Landed_ReportsBothPaths()
    {
        var fake = OpenFake();
        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New");

        summary.Should().StartWith("Renamed '^POUs^FB_Old' to '^POUs^FB_New'");
        fake.CallLog.Should().Contain("ProbePlcNode(^POUs^FB_New)").And.Contain("ProbePlcNode(^POUs^FB_Old)");
    }

    [Fact]
    public void Rename_NoOp_IsError()
    {
        var fake = OpenFake();
        fake.NoOpMutations.Add(nameof(FakeTwinCatSession.RenamePlcObject));

        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New");

        summary.Should().StartWith("[Error] Rename of '^POUs^FB_Old' to 'FB_New' did not land")
            .And.Contain("^POUs^FB_New");
    }

    [Fact]
    public void Rename_OldPathStillResolves_IsError()
    {
        var fake = OpenFake();
        // The session reports the new path, but the old node is still there (e.g. a copy).
        fake.RenameKeepsOldPath = true;

        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New");

        summary.Should().StartWith("[Error]").And.Contain("'^POUs^FB_Old' still resolves");
    }

    // ---- B-16 multi-step creates ---------------------------------------------------------------

    public static TheoryData<string> Kinds => new() { "DUT", "GVL", "INTERFACE" };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_DeclarationStepFails_SaysWhatExists(string kind)
    {
        var fake = OpenFake();
        fake.PlcNodeItemTypes["^POUs"] = PlcItemType.Folder;
        fake.WritePouBodyThrows = new InvalidOperationException("DeclarationText verify failed");
        var coordinator = new PlcCodeCoordinator(fake);

        string summary = kind switch
        {
            "DUT" => coordinator.CreateDut("^POUs", "ST_X", "STRUCT", "TYPE ST_X : STRUCT a : INT; END_STRUCT END_TYPE"),
            "GVL" => coordinator.CreateGvl("^POUs", "GVL_X", false, "VAR_GLOBAL a : INT; END_VAR"),
            _ => coordinator.CreateInterface("^POUs", "I_X", "INTERFACE I_X END_INTERFACE"),
        };

        summary.Should().StartWith("[Error]").And.Contain("was created at ^POUs^")
            .And.Contain("declaration was NOT applied").And.Contain("DeclarationText verify failed")
            .And.Contain("The object exists");
    }
}
