using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// Unit coverage for the feature/pou-handling coordinator: input validation
// for every sub-tool, conflictMode string→enum mapping (and default-to-Skip),
// and the ST-only refusal path for write_pou_member.
public class PouMembersCoordinatorTests
{
    private static FakeTwinCatSession OpenFake() =>
        new() { IsConnected = true, SolutionOpen = true };

    private static FakeTwinCatSession ClosedFake() =>
        new() { IsConnected = true, SolutionOpen = false };

    private static FakeTwinCatSession DisconnectedFake() =>
        new() { IsConnected = false };

    // ---- ListPouMembers ------------------------------------------------------

    [Fact]
    public void ListPouMembers_EmptyPath_ReturnsErrorBeforeSession()
    {
        var fake = OpenFake();
        var (summary, members) = new PouMembersCoordinator(fake).ListPouMembers("  ");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("pouPath", summary);
        Assert.Empty(members);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("ListPouMembers", StringComparison.Ordinal));
    }

    [Fact]
    public void ListPouMembers_NotConnected_ReturnsError()
    {
        var (summary, members) = new PouMembersCoordinator(DisconnectedFake())
            .ListPouMembers("^POUs^FB_Foo");
        Assert.StartsWith("[Error]", summary);
        Assert.Empty(members);
    }

    [Fact]
    public void ListPouMembers_NoSolution_ReturnsError()
    {
        var (summary, _) = new PouMembersCoordinator(ClosedFake()).ListPouMembers("^POUs^FB_Foo");
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void ListPouMembers_Empty_ReportsNone()
    {
        var fake = OpenFake();
        fake.MembersByPou["^POUs^FB_Foo"] = new();
        var (summary, members) = new PouMembersCoordinator(fake).ListPouMembers("^POUs^FB_Foo");

        Assert.Contains("no METHOD/ACTION/PROPERTY", summary);
        Assert.Empty(members);
    }

    [Fact]
    public void ListPouMembers_CountsByKind()
    {
        var fake = OpenFake();
        fake.MembersByPou["^POUs^FB_Foo"] = new()
        {
            new PouMemberEntry("^POUs^FB_Foo^Calc", "Calc", PlcMemberKind.Method),
            new PouMemberEntry("^POUs^FB_Foo^Reset", "Reset", PlcMemberKind.Action),
            new PouMemberEntry("^POUs^FB_Foo^Value", "Value", PlcMemberKind.Property),
            new PouMemberEntry("^POUs^FB_Foo^Value^Get", "Get", PlcMemberKind.Get),
        };

        var (summary, members) = new PouMembersCoordinator(fake).ListPouMembers("^POUs^FB_Foo");

        Assert.Equal(4, members.Count);
        Assert.Contains("1 METHOD", summary);
        Assert.Contains("1 ACTION", summary);
        Assert.Contains("1 PROPERTY", summary);
    }

    // ---- AddPouMember --------------------------------------------------------

    [Theory]
    [InlineData("", "ACTION", "Reset", "pouPath")]
    [InlineData("^POUs^FB_Foo", "", "Reset", "kind")]
    [InlineData("^POUs^FB_Foo", "ACTION", "", "name")]
    public void AddPouMember_MissingArg_ReturnsError(string pou, string kind, string name, string token)
    {
        var summary = new PouMembersCoordinator(OpenFake()).AddPouMember(pou, kind, name, null, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains(token, summary);
    }

    [Fact]
    public void AddPouMember_Method_DelegatesToSession()
    {
        // Issue #19: METHOD is supported again. The earlier refusal turned out
        // to be reserved-word method names (CALC/ADD); with a valid name the
        // coordinator must pass METHOD straight through.
        var fake = OpenFake();
        fake.MembersByPou["^POUs^FB_Foo"] = new();

        var summary = new PouMembersCoordinator(fake)
            .AddPouMember("^POUs^FB_Foo", "method", "Compute", "INT", null);

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains(fake.CallLog,
            c => c.StartsWith("AddPouMember", StringComparison.Ordinal) && c.Contains("kind=METHOD"));
    }

    [Fact]
    public void AddPouMember_UnknownKind_ReturnsError()
    {
        var summary = new PouMembersCoordinator(OpenFake())
            .AddPouMember("^POUs^FB_Foo", "WIDGET", "X", null, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("WIDGET", summary);
    }

    // Issue #19: reject reserved-word / invalid method names up front instead
    // of letting them surface as TC3's misleading "Name mismatch".
    [Theory]
    [InlineData("Calc")]   // CALC — IL operator
    [InlineData("ADD")]    // IL operator
    [InlineData("BOOL")]   // elementary type
    [InlineData("IF")]     // keyword
    public void AddPouMember_ReservedName_RefusedBeforeSession(string name)
    {
        var fake = OpenFake();
        fake.MembersByPou["^POUs^FB_Foo"] = new();

        var summary = new PouMembersCoordinator(fake)
            .AddPouMember("^POUs^FB_Foo", "METHOD", name, "INT", null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("reserved", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("AddPouMember", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("fb-foo")]
    [InlineData("a b")]
    public void AddPouMember_InvalidIdentifier_RefusedBeforeSession(string name)
    {
        var fake = OpenFake();
        fake.MembersByPou["^POUs^FB_Foo"] = new();

        var summary = new PouMembersCoordinator(fake)
            .AddPouMember("^POUs^FB_Foo", "ACTION", name, null, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("valid IEC 61131-3 identifier", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("AddPouMember", StringComparison.Ordinal));
    }

    [Fact]
    public void AddPouMember_NotConnected_DoesNotCallSession()
    {
        var fake = DisconnectedFake();
        var summary = new PouMembersCoordinator(fake)
            .AddPouMember("^POUs^FB_Foo", "ACTION", "Reset", null, null);
        Assert.StartsWith("[Error]", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("AddPouMember", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Method")]
    [InlineData("Action")]
    [InlineData("PROPERTY")]
    [InlineData("get")]
    [InlineData("set")]
    public void AddPouMember_KnownKind_NormalisesAndDelegates(string kind)
    {
        var fake = OpenFake();
        fake.MembersByPou["^POUs^FB_Foo"] = new();

        var summary = new PouMembersCoordinator(fake)
            .AddPouMember("^POUs^FB_Foo", kind, "X", null, null);

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains(fake.CallLog, c => c.StartsWith("AddPouMember", StringComparison.Ordinal));
        Assert.Contains(fake.CallLog,
            c => c.Contains($"kind={kind.ToUpperInvariant()}"));
    }

    [Fact]
    public void AddPouMember_StillOnlineAfterLogout_RefusesToAdd()
    {
        var fake = OpenFake();
        fake.MembersByPou["^POUs^FB_Foo"] = new();
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "still online (fake)");

        var summary = new PouMembersCoordinator(fake)
            .AddPouMember("^POUs^FB_Foo", "METHOD", "Compute", "INT", null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Online", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("AddPouMember", StringComparison.Ordinal));
    }

    // ---- ReadPouMember -------------------------------------------------------

    [Fact]
    public void ReadPouMember_EmptyPath_ReturnsError()
    {
        var (summary, body) = new PouMembersCoordinator(OpenFake()).ReadPouMember("");
        Assert.StartsWith("[Error]", summary);
        Assert.Null(body);
    }

    [Fact]
    public void ReadPouMember_Ok_ReturnsBody()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^FB_Foo^Calc"] =
            new PouBody("METHOD Calc : BOOL", "Calc := TRUE;", "ST");

        var (summary, body) = new PouMembersCoordinator(fake).ReadPouMember("^POUs^FB_Foo^Calc");

        Assert.DoesNotContain("[Error]", summary);
        Assert.NotNull(body);
        Assert.Equal("ST", body!.Language);
    }

    // ---- WritePouMember (ST-only guard) --------------------------------------

    [Fact]
    public void WritePouMember_NoSections_ReturnsError()
    {
        var summary = new PouMembersCoordinator(OpenFake())
            .WritePouMember("^POUs^FB_Foo^Calc", null, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("declaration or implementation", summary);
    }

    [Fact]
    public void WritePouMember_NonStMember_RefusedBeforeWriting()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^FB_Foo^Calc"] =
            new PouBody("METHOD Calc", "", "FBD");

        var summary = new PouMembersCoordinator(fake)
            .WritePouMember("^POUs^FB_Foo^Calc", null, "Calc := TRUE;");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("FBD", summary);
        Assert.Contains("ReadPouMember(^POUs^FB_Foo^Calc)", fake.CallLog);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WritePouMember", StringComparison.Ordinal));
    }

    [Fact]
    public void WritePouMember_StMember_WritesThrough()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^FB_Foo^Calc"] =
            new PouBody("METHOD Calc : BOOL", "", "ST");

        var summary = new PouMembersCoordinator(fake)
            .WritePouMember("^POUs^FB_Foo^Calc", null, "Calc := TRUE;");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains(fake.CallLog, c => c.StartsWith("WritePouMember", StringComparison.Ordinal));
    }

    [Fact]
    public void WritePouMember_StillOnlineAfterLogout_RefusesToWrite()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^FB_Foo^Calc"] =
            new PouBody("METHOD Calc : BOOL", "", "ST");
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "still online (fake)");

        var summary = new PouMembersCoordinator(fake)
            .WritePouMember("^POUs^FB_Foo^Calc", null, "Calc := TRUE;");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Online", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WritePouMember", StringComparison.Ordinal));
    }

    // ---- DeletePlcObject / RenamePlcObject -----------------------------------

    [Fact]
    public void DeletePlcObject_EmptyPath_ReturnsError()
    {
        var summary = new PouMembersCoordinator(OpenFake()).DeletePlcObject("");
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void DeletePlcObject_Ok_MentionsBuildCheck()
    {
        var fake = OpenFake();
        fake.Pous.Add(new PouEntry("^POUs^FB_Foo", "FB_Foo", PouKind.Pou, "ST"));

        var summary = new PouMembersCoordinator(fake).DeletePlcObject("^POUs^FB_Foo");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("twincat_build_plc", summary);
        Assert.Contains("DeletePlcObject(^POUs^FB_Foo)", fake.CallLog);
    }

    [Fact]
    public void DeletePlcObject_StillOnlineAfterLogout_RefusesToDelete()
    {
        var fake = OpenFake();
        fake.Pous.Add(new PouEntry("^POUs^FB_Foo", "FB_Foo", PouKind.Pou, "ST"));
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "still online (fake)");

        var summary = new PouMembersCoordinator(fake).DeletePlcObject("^POUs^FB_Foo");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Online", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("DeletePlcObject", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "New", "treePath")]
    [InlineData("^POUs^FB_Foo", "", "newName")]
    public void RenamePlcObject_MissingArg_ReturnsError(string path, string newName, string token)
    {
        var summary = new PouMembersCoordinator(OpenFake()).RenamePlcObject(path, newName);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains(token, summary);
    }

    [Fact]
    public void RenamePlcObject_Ok_ReturnsNewPath()
    {
        var fake = OpenFake();
        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("FB_New", summary);
    }

    [Fact]
    public void RenamePlcObject_DefaultsToNotUpdatingReferences_AndSaysNoPromptAppeared()
    {
        var fake = OpenFake();
        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New");

        Assert.False(fake.LastRenameUpdateReferences);
        Assert.Contains("Refactoring prompt: none appeared", summary);
        Assert.Contains("could NOT be enforced", summary);
    }

    [Theory]
    [InlineData("Enabled", false, "could NOT be enforced", "WERE renamed")]
    [InlineData("Enabled", true, "Callers renamed too", "as requested")]
    [InlineData("Disabled", false, "Callers untouched", "as requested")]
    [InlineData("Disabled", true, "could NOT be honoured", "still use the old name")]
    public void RefactoringNote_WithoutPrompt_FollowsTheXaeOption(string setting, bool update, string a, string b)
    {
        string note = PouMembersCoordinator.RefactoringNote(null, update, Enum.Parse<RenameRefactoringSetting>(setting));
        Assert.Contains(a, note, StringComparison.Ordinal);
        Assert.Contains(b, note, StringComparison.Ordinal);
    }

    [Fact]
    public void RefactoringNote_WithoutPrompt_DependsOnUpdateReferences()
    {
        Assert.Contains("could NOT be enforced", PouMembersCoordinator.RefactoringNote(null, updateReferences: false));
        Assert.DoesNotContain("could NOT be enforced", PouMembersCoordinator.RefactoringNote(null, updateReferences: true));
        Assert.Equal("Refactoring prompt: answered 'Ja'.", PouMembersCoordinator.RefactoringNote("answered 'Ja'", true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RenamePlcObject_PassesUpdateReferences_AndReportsThePromptAnswer(bool updateReferences)
    {
        var fake = OpenFake();
        fake.RenameRefactoringPrompt = "'Automatisches Umbenennen über Refactoring' answered 'Nein' (BM_CLICK 'Nein')";

        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New", updateReferences: updateReferences);

        Assert.Equal(updateReferences, fake.LastRenameUpdateReferences);
        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("Refactoring prompt: 'Automatisches Umbenennen über Refactoring' answered 'Nein'", summary);
    }

    [Fact]
    public void RenamePlcObject_PromptNotAnsweredAsAsked_IsAnError()
    {
        var fake = OpenFake();
        fake.RenameRefactoringPrompt = "'Automatisches Umbenennen über Refactoring' could NOT be answered 'Ja' — cancelled instead";
        fake.RenameRefactoringPromptFailed = true;

        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New", updateReferences: true);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("could NOT be answered", summary);
    }

    [Fact]
    public void RenamePlcObject_StillOnlineAfterLogout_RefusesToRename()
    {
        var fake = OpenFake();
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "still online (fake)");

        var summary = new PouMembersCoordinator(fake).RenamePlcObject("^POUs^FB_Old", "FB_New");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Online", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("RenamePlcObject", StringComparison.Ordinal));
    }

    // ---- conflictMode mapping ------------------------------------------------

    [Theory]
    [InlineData(null, 3)]   // default = Skip (non-destructive)
    [InlineData("", 3)]
    [InlineData("none", 0)]
    [InlineData("Rename", 1)]
    [InlineData("REPLACE", 2)]
    [InlineData("skip", 3)]
    public void MapConflictMode_KnownValues_MapToExpectedCode(string? mode, int expected)
    {
        var (ok, code, error) = PouMembersCoordinator.MapConflictMode(mode);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, code);
    }

    [Fact]
    public void MapConflictMode_Unknown_ReturnsError()
    {
        var (ok, _, error) = PouMembersCoordinator.MapConflictMode("clobber");
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("clobber", error);
    }

    // ---- PlcOpen export / import --------------------------------------------

    [Theory]
    [InlineData("", "POUs.FB_Foo", "filePath")]
    [InlineData("out.xml", "", "selector")]
    public void PlcOpenExport_MissingArg_ReturnsError(string file, string sel, string token)
    {
        var summary = new PouMembersCoordinator(OpenFake()).PlcOpenExport(file, sel);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains(token, summary);
    }

    [Fact]
    public void PlcOpenExport_Ok_Delegates()
    {
        var fake = OpenFake();
        var summary = new PouMembersCoordinator(fake)
            .PlcOpenExport(@"C:\tmp\out.xml", "POUs.FB_Foo");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains(fake.CallLog, c => c.StartsWith("PlcOpenExport", StringComparison.Ordinal));
    }

    [Fact]
    public void PlcOpenImport_BadConflictMode_ReturnsErrorBeforeSession()
    {
        var fake = OpenFake();
        var summary = new PouMembersCoordinator(fake)
            .PlcOpenImport(@"C:\tmp\in.xml", "clobber");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("clobber", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("PlcOpenImport", StringComparison.Ordinal));
    }

    // A minimal PLCopen document with one POU (issue #152: the import is checked per object).
    internal const string OnePouPlcOpen =
        "<project xmlns=\"http://www.plcopen.org/xml/tc6_0200\"><types><pous>" +
        "<pou name=\"FB_Imported\" pouType=\"functionBlock\"><interface/></pou>" +
        "</pous></types></project>";

    private static string TempPlcOpenFile()
    {
        string file = Path.GetTempFileName();
        File.WriteAllText(file, OnePouPlcOpen);
        return file;
    }

    [Fact]
    public void PlcOpenImport_DefaultMode_UsesSkip()
    {
        var fake = OpenFake();
        string file = TempPlcOpenFile();
        try
        {
            var summary = new PouMembersCoordinator(fake).PlcOpenImport(file, null);

            Assert.DoesNotContain("[Error]", summary);
            Assert.Contains(fake.CallLog, c => c.Contains($"PlcOpenImport({file}, 3)"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void PlcOpenImport_MissingFile_ErrorsBeforeLogout()
    {
        var fake = OpenFake();
        var summary = new PouMembersCoordinator(fake).PlcOpenImport(@"C:\tmp\does-not-exist-148.xml", null);

        Assert.StartsWith("[Error] PLCopen file not found", summary);
        Assert.DoesNotContain("LogoutPlc()", fake.CallLog);
    }

    [Fact]
    public void PlcOpenImport_StillOnlineAfterLogout_RefusesToImport()
    {
        var fake = OpenFake();
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "still online (fake)");
        string file = TempPlcOpenFile();
        try
        {
            var summary = new PouMembersCoordinator(fake).PlcOpenImport(file, null);

            Assert.StartsWith("[Error]", summary);
            Assert.Contains("Online", summary);
            Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("PlcOpenImport", StringComparison.Ordinal));
        }
        finally { File.Delete(file); }
    }
}
