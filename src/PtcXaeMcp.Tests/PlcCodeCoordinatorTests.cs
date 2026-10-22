using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class PlcCodeCoordinatorTests
{
    private static FakeTwinCatSession OpenFake() =>
        new() { IsConnected = true, SolutionOpen = true };

    private static FakeTwinCatSession ClosedFake() =>
        new() { IsConnected = true, SolutionOpen = false };

    private static FakeTwinCatSession DisconnectedFake() =>
        new() { IsConnected = false };

    // ---- ListPous ------------------------------------------------------------

    [Fact]
    public void ListPous_NotConnected_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = DisconnectedFake();
        var (summary, entries) = new PlcCodeCoordinator(fake).ListPous();

        Assert.StartsWith("[Error]", summary);
        Assert.Empty(entries);
        Assert.DoesNotContain("ListPous", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void ListPous_NoSolution_ReturnsError()
    {
        var (summary, entries) = new PlcCodeCoordinator(ClosedFake()).ListPous();
        Assert.StartsWith("[Error]", summary);
        Assert.Empty(entries);
    }

    [Fact]
    public void ListPous_Empty_ReportsNone()
    {
        var (summary, entries) = new PlcCodeCoordinator(OpenFake()).ListPous();
        Assert.Equal("No POUs, GVLs, or DUTs found in the open PLC project.", summary);
        Assert.Empty(entries);
    }

    [Fact]
    public void ListPous_MixedKinds_CountsByKind()
    {
        var fake = OpenFake();
        fake.Pous.Add(new PouEntry("^POUs^MAIN", "MAIN", PouKind.Pou, "ST"));
        fake.Pous.Add(new PouEntry("^POUs^FB_Pump", "FB_Pump", PouKind.Pou, "ST"));
        fake.Pous.Add(new PouEntry("^GVLs^GVL_IO", "GVL_IO", PouKind.Gvl, null));
        fake.Pous.Add(new PouEntry("^DUTs^ST_Pose", "ST_Pose", PouKind.Dut, null));
        fake.Pous.Add(new PouEntry("^POUs^Drives", "Drives", PouKind.Folder, null));

        var (summary, entries) = new PlcCodeCoordinator(fake).ListPous();

        Assert.Equal(5, entries.Count);
        Assert.Contains("Found 5 items", summary);
        Assert.Contains("2 POU", summary);
        Assert.Contains("1 GVL", summary);
        Assert.Contains("1 DUT", summary);
    }

    [Fact]
    public void ListPous_WithInterface_CountsAndSurfacesInterfaceKind()
    {
        var fake = OpenFake();
        fake.Pous.Add(new PouEntry("^POUs^MAIN", "MAIN", PouKind.Pou, "ST"));
        fake.Pous.Add(new PouEntry("^04_Components^I_Counter", "I_Counter", PouKind.Interface, null));
        fake.Pous.Add(new PouEntry("^04_Components^I_Valve", "I_Valve", PouKind.Interface, null));

        var (summary, entries) = new PlcCodeCoordinator(fake).ListPous();

        Assert.Equal(3, entries.Count);
        Assert.Contains("1 POU", summary);
        Assert.Contains("2 INTERFACE", summary);
        Assert.Contains(entries, e => e.Kind == PouKind.Interface && e.Name == "I_Counter");
    }

    [Fact]
    public void ListPous_NoInterface_OmitsInterfaceTally()
    {
        var fake = OpenFake();
        fake.Pous.Add(new PouEntry("^POUs^MAIN", "MAIN", PouKind.Pou, "ST"));

        var (summary, _) = new PlcCodeCoordinator(fake).ListPous();

        Assert.DoesNotContain("INTERFACE", summary);
    }

    [Fact]
    public void ListPous_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.ListPousThrows = new InvalidOperationException("walk failed");

        var (summary, entries) = new PlcCodeCoordinator(fake).ListPous();

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("walk failed", summary);
        Assert.Empty(entries);
    }

    // ---- ReadPou -------------------------------------------------------------

    [Fact]
    public void ReadPou_NoSolution_ReturnsErrorAndNullBody()
    {
        var (summary, body) = new PlcCodeCoordinator(ClosedFake()).ReadPou("^POUs^MAIN");
        Assert.StartsWith("[Error]", summary);
        Assert.Null(body);
    }

    [Fact]
    public void ReadPou_BlankPath_ReturnsError()
    {
        var fake = OpenFake();
        var (summary, body) = new PlcCodeCoordinator(fake).ReadPou("   ");
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("treePath", summary);
        Assert.Null(body);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void ReadPou_HappyPath_ReturnsBody()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^MAIN"] = new PouBody("PROGRAM MAIN", "Init();", "ST");

        var (summary, body) = new PlcCodeCoordinator(fake).ReadPou("^POUs^MAIN");

        Assert.NotNull(body);
        Assert.Equal("PROGRAM MAIN", body!.Declaration);
        Assert.Equal("Init();", body.Implementation);
        Assert.Contains("Read POU '^POUs^MAIN' (ST).", summary);
    }

    [Fact]
    public void ReadPou_UnknownPath_SessionThrowsWrappedAsError()
    {
        var (summary, body) = new PlcCodeCoordinator(OpenFake()).ReadPou("^POUs^Nope");
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("not found", summary);
        Assert.Null(body);
    }

    // ---- ReadGvl -------------------------------------------------------------

    [Fact]
    public void ReadGvl_NoSolution_ReturnsError()
    {
        var (summary, decl) = new PlcCodeCoordinator(ClosedFake()).ReadGvl("^GVLs^GVL_IO");
        Assert.StartsWith("[Error]", summary);
        Assert.Null(decl);
    }

    [Fact]
    public void ReadGvl_HappyPath_ReturnsDeclaration()
    {
        var fake = OpenFake();
        fake.GvlDeclarationsByPath["^GVLs^GVL_IO"] = "VAR_GLOBAL\n    i : INT;\nEND_VAR";

        var (summary, decl) = new PlcCodeCoordinator(fake).ReadGvl("^GVLs^GVL_IO");

        Assert.NotNull(decl);
        Assert.Contains("VAR_GLOBAL", decl);
        Assert.Contains("Read GVL", summary);
    }

    [Fact]
    public void ReadGvl_UnknownPath_WrappedAsError()
    {
        var (summary, decl) = new PlcCodeCoordinator(OpenFake()).ReadGvl("^GVLs^Nope");
        Assert.StartsWith("[Error]", summary);
        Assert.Null(decl);
    }

    // ---- WritePouBody --------------------------------------------------------

    [Fact]
    public void WritePouBody_NoSolution_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(ClosedFake()).WritePouBody("^POUs^MAIN", null, ";");
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void WritePouBody_BlankPath_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(OpenFake()).WritePouBody("", null, ";");
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("treePath", summary);
    }

    [Fact]
    public void WritePouBody_BothNull_ReturnsError()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^MAIN"] = new PouBody("PROGRAM MAIN", ";", "ST");

        var summary = new PlcCodeCoordinator(fake).WritePouBody("^POUs^MAIN", null, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("At least one", summary);
        Assert.DoesNotContain("WritePouBody", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void WritePouBody_NonStPou_RefusedBeforeWriting()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^FB_Fbd"] = new PouBody("FUNCTION_BLOCK FB_Fbd", "", "FBD");

        var summary = new PlcCodeCoordinator(fake).WritePouBody("^POUs^FB_Fbd", null, ";");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("FBD", summary);
        // ReadPou was called (the language probe), but WritePouBody was NOT.
        Assert.Contains("ReadPou(^POUs^FB_Fbd)", fake.CallLog);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WritePouBody", StringComparison.Ordinal));
    }

    [Fact]
    public void WritePouBody_HappyPath_DelegatesAndReports()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^MAIN"] = new PouBody("PROGRAM MAIN", "old();", "ST");

        var summary = new PlcCodeCoordinator(fake).WritePouBody("^POUs^MAIN", null, "new();");

        Assert.Contains("Wrote POU '^POUs^MAIN'", summary);
        Assert.Equal("new();", fake.PouBodiesByPath["^POUs^MAIN"].Implementation);
    }

    [Fact]
    public void WritePouBody_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^MAIN"] = new PouBody("PROGRAM MAIN", "", "ST");
        fake.WritePouBodyThrows = new InvalidOperationException("verify failed: declaration mismatch");

        var summary = new PlcCodeCoordinator(fake).WritePouBody("^POUs^MAIN", null, ";");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("verify failed", summary);
    }

    [Fact]
    public void WritePouBody_LogsOutBeforeWriting()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^MAIN"] = new PouBody("PROGRAM MAIN", "old();", "ST");

        _ = new PlcCodeCoordinator(fake).WritePouBody("^POUs^MAIN", null, "new();");

        int logoutIdx = fake.CallLog.IndexOf("LogoutPlc()");
        int writeIdx = fake.CallLog.FindIndex(c => c.StartsWith("WritePouBody", StringComparison.Ordinal));
        Assert.True(logoutIdx >= 0, "LogoutPlc() was not called before writing.");
        Assert.True(logoutIdx < writeIdx, "LogoutPlc() must run before WritePouBody.");
    }

    [Fact]
    public void WritePouBody_StillOnlineAfterLogout_RefusesToWrite()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath["^POUs^MAIN"] = new PouBody("PROGRAM MAIN", "old();", "ST");
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "Logout command executed but PLC still appears online.");

        var summary = new PlcCodeCoordinator(fake).WritePouBody("^POUs^MAIN", null, "new();");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Online", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WritePouBody", StringComparison.Ordinal));
        Assert.Equal("old();", fake.PouBodiesByPath["^POUs^MAIN"].Implementation);
    }

    // ---- CreatePou -----------------------------------------------------------

    [Fact]
    public void CreatePou_NoSolution_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(ClosedFake())
            .CreatePou("^POUs", "FB_New", "FUNCTION_BLOCK", null, null);
        Assert.StartsWith("[Error]", summary);
    }

    [Theory]
    [InlineData("",  "FB_New", "FUNCTION_BLOCK")]
    [InlineData("^POUs", "",   "FUNCTION_BLOCK")]
    [InlineData("^POUs", "FB_New", "")]
    public void CreatePou_BlankRequired_ReturnsError(string parent, string name, string type)
    {
        var summary = new PlcCodeCoordinator(OpenFake())
            .CreatePou(parent, name, type, null, null);
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void CreatePou_UnknownType_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(OpenFake())
            .CreatePou("^POUs", "FB_New", "INTERFACE", null, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Unknown pouType", summary);
    }

    [Fact]
    public void CreatePou_FunctionWithoutReturn_DefaultsToBool()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake)
            .CreatePou("^POUs", "F_Add", "function", returnType: null, implLanguage: null);

        Assert.Contains("Created FUNCTION", summary);
        Assert.Contains(fake.CallLog,
            c => c.Contains("CreatePou(parent=^POUs") && c.Contains("type=FUNCTION") && c.Contains("ret=BOOL"));
    }

    [Fact]
    public void CreatePou_ProgramIgnoresReturnType()
    {
        var fake = OpenFake();
        new PlcCodeCoordinator(fake)
            .CreatePou("^POUs", "PRG_New", "PROGRAM", "BOOL", "ST");

        // PROGRAM creation should pass return="" (empty) since it's irrelevant. The
        // original assertion checked only `Contains("ret=")`, which is a tautology:
        // FakeTwinCatSession.CreatePou always logs "ret={returnType}, lang=..." so the
        // literal "ret=" substring is present whether returnType is empty or "BOOL" —
        // the test could never fail. Match "ret=," (empty value followed immediately by
        // the next field's separator) to actually assert the value is blank (issue #157
        // item 7). Test-only fix; no coordinator/fake change.
        Assert.Contains(fake.CallLog,
            c => c.Contains("type=PROGRAM") && c.Contains("ret=,"));
    }

    [Fact]
    public void CreatePou_StillOnlineAfterLogout_RefusesToCreate()
    {
        var fake = OpenFake();
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "still online (fake)");

        var summary = new PlcCodeCoordinator(fake)
            .CreatePou("^POUs", "FB_New", "FUNCTION_BLOCK", null, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Online", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("CreatePou", StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePou_LanguageDefaultsToSt()
    {
        var fake = OpenFake();
        new PlcCodeCoordinator(fake)
            .CreatePou("^POUs", "FB_New", "FUNCTION_BLOCK", null, null);

        Assert.Contains(fake.CallLog, c => c.Contains("lang=ST"));
    }

    [Fact]
    public void CreatePou_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.CreatePouThrows = new InvalidOperationException("CreateChild failed: Name mismatch");

        var summary = new PlcCodeCoordinator(fake)
            .CreatePou("^POUs", "F_Calc", "FUNCTION", "BOOL", "IL");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("CreateChild failed", summary);
    }

    // ---- CreateDut -----------------------------------------------------------

    [Fact]
    public void CreateDut_NoSolution_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(ClosedFake())
            .CreateDut("^DUTs", "ST_New", "STRUCT", null);
        Assert.StartsWith("[Error]", summary);
    }

    [Theory]
    [InlineData("", "ST_New")]
    [InlineData("^DUTs", "")]
    public void CreateDut_BlankRequired_ReturnsError(string parent, string name)
    {
        var summary = new PlcCodeCoordinator(OpenFake()).CreateDut(parent, name, "STRUCT", null);
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void CreateDut_UnknownType_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(OpenFake())
            .CreateDut("^DUTs", "ST_New", "RECORD", null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Unknown dutType", summary);
    }

    [Fact]
    public void CreateDut_InvalidName_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(OpenFake())
            .CreateDut("^DUTs", "1Bad Name", "STRUCT", null);
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void CreateDut_TypeOmitted_DefaultsToStruct()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake)
            .CreateDut("^DUTs", "ST_New", dutType: null, declaration: null);

        Assert.Contains("Created DUT STRUCT", summary);
        Assert.Contains(fake.CallLog, c => c.Contains("CreateDut(parent=^DUTs") && c.Contains("type=STRUCT"));
    }

    [Fact]
    public void CreateDut_NoDeclaration_DoesNotWriteBody()
    {
        var fake = OpenFake();
        new PlcCodeCoordinator(fake).CreateDut("^DUTs", "E_New", "ENUM", declaration: null);

        Assert.Contains(fake.CallLog, c => c.Contains("type=ENUM"));
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WritePouBody", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDut_WithDeclaration_DelegatesWrite()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake)
            .CreateDut("^DUTs", "ST_New", "STRUCT", "TYPE ST_New :\nSTRUCT\n  x : INT;\nEND_STRUCT\nEND_TYPE");

        Assert.Contains("Created DUT STRUCT", summary);
        Assert.Contains(fake.CallLog, c => c.StartsWith("WritePouBody(^DUTs^ST_New", StringComparison.Ordinal) && c.Contains("decl=<set>"));
    }

    [Fact]
    public void CreateDut_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.CreateDutThrows = new InvalidOperationException("CreateChild failed: bad parent");
        var summary = new PlcCodeCoordinator(fake).CreateDut("^DUTs", "ST_New", "STRUCT", null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("CreateChild failed", summary);
    }

    // ALIAS needs the base type at CreateChild time (issue #106). Without a
    // declaration the coordinator can't derive it, so it must refuse up front and
    // never call the session.
    [Fact]
    public void CreateDut_Alias_NoDeclaration_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake).CreateDut("^DUTs", "A_New", "ALIAS", declaration: null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("ALIAS", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("CreateDut", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateDut_Alias_BlankBaseType_ReturnsError()
    {
        var fake = OpenFake();
        // Declaration present but no parsable base type between ':' and ';'.
        var summary = new PlcCodeCoordinator(fake).CreateDut("^DUTs", "A_New", "ALIAS", "TYPE A_New : ; END_TYPE");

        Assert.StartsWith("[Error]", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("CreateDut", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("TYPE A_New : INT; END_TYPE", "INT")]
    [InlineData("TYPE A_New : POINTER TO ST_Foo; END_TYPE", "POINTER TO ST_Foo")]
    [InlineData("TYPE A_New : ARRAY[0..9] OF INT; END_TYPE", "ARRAY[0..9] OF INT")]
    public void CreateDut_Alias_ParsesBaseTypeAndPassesToSession(string decl, string expectedBaseType)
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake).CreateDut("^DUTs", "A_New", "ALIAS", decl);

        Assert.Contains("Created DUT ALIAS", summary);
        Assert.Contains(fake.CallLog,
            c => c.Contains("CreateDut(parent=^DUTs") && c.Contains("type=ALIAS") && c.Contains($"baseType={expectedBaseType}"));
    }

    // ---- CreateGvl -----------------------------------------------------------

    [Fact]
    public void CreateGvl_NoSolution_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(ClosedFake())
            .CreateGvl("^GVLs", "GVL_New", false, null);
        Assert.StartsWith("[Error]", summary);
    }

    [Theory]
    [InlineData("", "GVL_New")]
    [InlineData("^GVLs", "")]
    public void CreateGvl_BlankRequired_ReturnsError(string parent, string name)
    {
        var summary = new PlcCodeCoordinator(OpenFake()).CreateGvl(parent, name, false, null);
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void CreateGvl_InvalidName_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(OpenFake())
            .CreateGvl("^GVLs", "has space", false, null);
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void CreateGvl_Normal_CreatesPlainGvlWithoutDeclarationWrite()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake).CreateGvl("^GVLs", "GVL_New", false, null);

        Assert.Contains("Created GVL 'GVL_New'", summary);
        Assert.Contains(fake.CallLog, c => c.StartsWith("CreateGvl(parent=^GVLs", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WritePouBody(", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateGvl_Persistent_ReportsPersistent()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake).CreateGvl("^GVLs", "GVL_Pers", true, null);

        Assert.Contains("Created persistent GVL", summary);
    }

    [Fact]
    public void CreateGvl_Persistent_WithPersistentDeclaration_WritesIt()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake)
            .CreateGvl("^GVLs", "GVL_Pers", true, "VAR_GLOBAL PERSISTENT\n  g : INT;\nEND_VAR");

        Assert.Contains("Created persistent GVL", summary);
        Assert.Contains("g : INT", fake.PouBodiesByPath["^GVLs^GVL_Pers"].Declaration);
    }

    // F1 (fix/gvl-persistent-629): nSubType 629 is "Parameter List" (TREEITEMTYPES PLCGVLPARAMLIST,
    // live: the saved file carries ParameterList="True" and the skeleton is `VAR_GLOBAL CONSTANT`),
    // NOT a persistent-variables list. Persistent variables are an ordinary GVL (615) whose
    // declaration carries `VAR_GLOBAL PERSISTENT` — so the coordinator must write that section itself.
    [Fact]
    public void CreateGvl_Persistent_WithoutDeclaration_WritesPersistentSectionItself()
    {
        var fake = OpenFake();
        new PlcCodeCoordinator(fake).CreateGvl("^GVLs", "GVL_Pers", true, null);

        Assert.Contains(fake.CallLog, c =>
            c.StartsWith("WritePouBody(^GVLs^GVL_Pers", StringComparison.Ordinal) && c.Contains("decl=<set>"));
        Assert.Contains("PERSISTENT", fake.PouBodiesByPath["^GVLs^GVL_Pers"].Declaration);
        Assert.DoesNotContain("CONSTANT", fake.PouBodiesByPath["^GVLs^GVL_Pers"].Declaration);
    }

    [Fact]
    public void CreateGvl_Persistent_DeclarationWithoutPersistentKeyword_RefusedBeforeAnyCreate()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake)
            .CreateGvl("^GVLs", "GVL_Pers", true, "VAR_GLOBAL\n  g : INT;\nEND_VAR");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("PERSISTENT", summary);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("CreateGvl(", StringComparison.Ordinal));
        Assert.DoesNotContain("LogoutPlc()", fake.CallLog);
    }

    [Fact]
    public void CreateGvl_WithDeclaration_DelegatesWrite()
    {
        var fake = OpenFake();
        new PlcCodeCoordinator(fake)
            .CreateGvl("^GVLs", "GVL_New", false, "VAR_GLOBAL\n  g : BOOL;\nEND_VAR");

        Assert.Contains(fake.CallLog, c => c.StartsWith("WritePouBody(^GVLs^GVL_New", StringComparison.Ordinal) && c.Contains("decl=<set>"));
    }

    [Fact]
    public void CreateGvl_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.CreateGvlThrows = new InvalidOperationException("Cannot create GVL under POU");
        var summary = new PlcCodeCoordinator(fake).CreateGvl("^POUs^MAIN", "GVL_New", false, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("Cannot create GVL", summary);
    }

    // ---- CreateInterface -----------------------------------------------------

    [Fact]
    public void CreateInterface_NoSolution_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(ClosedFake())
            .CreateInterface("^04_Components", "I_New", null);
        Assert.StartsWith("[Error]", summary);
    }

    [Theory]
    [InlineData("", "I_New")]
    [InlineData("^04_Components", "")]
    public void CreateInterface_BlankRequired_ReturnsError(string parent, string name)
    {
        var summary = new PlcCodeCoordinator(OpenFake()).CreateInterface(parent, name, null);
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void CreateInterface_InvalidName_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(OpenFake())
            .CreateInterface("^04_Components", "1 Bad", null);
        Assert.StartsWith("[Error]", summary);
    }

    [Fact]
    public void CreateInterface_NoDeclaration_ReportsAndDoesNotWriteBody()
    {
        var fake = OpenFake();
        var summary = new PlcCodeCoordinator(fake).CreateInterface("^04_Components", "I_New", declaration: null);

        Assert.Contains("Created INTERFACE 'I_New'", summary);
        Assert.Contains(fake.CallLog, c => c.Contains("CreateInterface(parent=^04_Components"));
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("WritePouBody", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateInterface_WithDeclaration_DelegatesWrite()
    {
        var fake = OpenFake();
        new PlcCodeCoordinator(fake)
            .CreateInterface("^04_Components", "I_New", "INTERFACE I_New\nEND_INTERFACE");

        Assert.Contains(fake.CallLog, c => c.StartsWith("WritePouBody(^04_Components^I_New", StringComparison.Ordinal) && c.Contains("decl=<set>"));
    }

    [Fact]
    public void CreateInterface_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.CreateInterfaceThrows = new InvalidOperationException("CreateChild failed: bad parent");
        var summary = new PlcCodeCoordinator(fake).CreateInterface("^04_Components", "I_New", null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("CreateChild failed", summary);
    }

    // ---- OpenPlcObject (visible-for-video) -----------------------------------

    [Fact]
    public void OpenPlcObject_BlankPath_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = OpenFake();
        var (summary, result) = new PlcCodeCoordinator(fake).OpenPlcObject("   ");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("treePath", summary);
        Assert.Null(result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void OpenPlcObject_NoSolution_ReturnsError()
    {
        var (summary, result) = new PlcCodeCoordinator(ClosedFake()).OpenPlcObject("^POUs^MAIN");
        Assert.StartsWith("[Error]", summary);
        Assert.Null(result);
    }

    [Fact]
    public void OpenPlcObject_NotConnected_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = DisconnectedFake();
        var (summary, result) = new PlcCodeCoordinator(fake).OpenPlcObject("^POUs^MAIN");

        Assert.StartsWith("[Error]", summary);
        Assert.Null(result);
        Assert.DoesNotContain("OpenPlcObject", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void OpenPlcObject_HappyPath_OpenedAndForegroundConfirmed()
    {
        var fake = OpenFake();
        fake.FakeOpenPlcObjectResult = new OpenPlcObjectResult(
            "^POUs^MAIN", Opened: true, AlreadyOpen: false,
            ForegroundRequested: true, ForegroundConfirmed: true, Note: "open via OpenFile(disk path)");

        var (summary, result) = new PlcCodeCoordinator(fake).OpenPlcObject("^POUs^MAIN");

        Assert.NotNull(result);
        Assert.True(result!.Opened);
        Assert.Contains("Opened PLC object", summary);
        Assert.Contains("opened '^POUs^MAIN'", summary);
        Assert.Contains("foreground", summary);
        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("OpenPlcObject(^POUs^MAIN)", fake.CallLog);
    }

    [Fact]
    public void OpenPlcObject_AlreadyOpen_ReportsReactivated()
    {
        var fake = OpenFake();
        fake.FakeOpenPlcObjectResult = new OpenPlcObjectResult(
            "^POUs^MAIN", Opened: true, AlreadyOpen: true,
            ForegroundRequested: true, ForegroundConfirmed: true, Note: null);

        var (summary, _) = new PlcCodeCoordinator(fake).OpenPlcObject("^POUs^MAIN");

        Assert.Contains("already open", summary);
        Assert.Contains("re-activated", summary);
    }

    [Fact]
    public void OpenPlcObject_ForegroundNotConfirmed_ReportsBestEffortNotError()
    {
        var fake = OpenFake();
        fake.FakeOpenPlcObjectResult = new OpenPlcObjectResult(
            "^POUs^MAIN", Opened: true, AlreadyOpen: false,
            ForegroundRequested: true, ForegroundConfirmed: false, Note: null);

        var (summary, _) = new PlcCodeCoordinator(fake).OpenPlcObject("^POUs^MAIN");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("not confirmed", summary);
    }

    [Fact]
    public void OpenPlcObject_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.OpenPlcObjectThrows = new InvalidOperationException(
            "'^POUs' (ItemType 601) is a container, not an openable source object.");

        var (summary, result) = new PlcCodeCoordinator(fake).OpenPlcObject("^POUs");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("container", summary);
        Assert.Null(result);
    }

    // ---- OpenObject / CloseObject (generalization) ---------------------------

    [Fact]
    public void OpenObject_BlankPath_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = OpenFake();
        var (summary, result) = new PlcCodeCoordinator(fake).OpenObject("   ");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("treePath", summary);
        Assert.Null(result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void OpenObject_NoSolution_ReturnsError()
    {
        var (summary, result) = new PlcCodeCoordinator(ClosedFake()).OpenObject("TIID^Device 1 (EtherCAT)");
        Assert.StartsWith("[Error]", summary);
        Assert.Null(result);
    }

    [Fact]
    public void OpenObject_PlcKind_ProducesByteIdenticalPlcSummary()
    {
        var fake = OpenFake();
        fake.FakeOpenObjectResult = new OpenObjectResult(
            "^POUs^MAIN", OpenObjectKind.PlcObject, Opened: true, AlreadyOpen: false,
            ForegroundRequested: true, ForegroundConfirmed: true, Note: "open via OpenFile(disk path)");

        var (summary, result) = new PlcCodeCoordinator(fake).OpenObject("^POUs^MAIN");

        // Must match the deprecated alias's PLC phrasing so back-compat callers / the
        // live integration assertions keep passing.
        Assert.Contains("Opened PLC object", summary);
        Assert.Contains("activated its tab", summary);
        Assert.Contains("foreground", summary);
        Assert.DoesNotContain("[Error]", summary);
        Assert.Equal(OpenObjectKind.PlcObject, result!.Kind);
        Assert.Contains("OpenObject(^POUs^MAIN)", fake.CallLog);
    }

    [Fact]
    public void OpenObject_ActivatedNode_SaysItsEditorIsOpen()
    {
        var fake = OpenFake();
        fake.FakeOpenObjectResult = new OpenObjectResult(
            "ScopeOpenProbe^YT Scope Project", OpenObjectKind.SysmanNode, Opened: true, AlreadyOpen: false,
            ForegroundRequested: true, ForegroundConfirmed: true,
            Note: "selected 'YT Scope Project' in the Solution Explorer; Enter sent to open its editor");

        var (summary, _) = new PlcCodeCoordinator(fake).OpenObject("ScopeOpenProbe^YT Scope Project");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("activated system-manager node", summary);
        Assert.Contains("Scope view", summary);
    }

    [Fact]
    public void OpenObject_SysmanKind_ReportsSelectedNodeNotTab()
    {
        var fake = OpenFake();
        fake.FakeOpenObjectResult = new OpenObjectResult(
            "TIID^Device 1 (EtherCAT)", OpenObjectKind.SysmanNode, Opened: true, AlreadyOpen: false,
            ForegroundRequested: true, ForegroundConfirmed: true, Note: "selected 'Gerät 1 (EtherCAT)'");

        var (summary, result) = new PlcCodeCoordinator(fake).OpenObject("TIID^Device 1 (EtherCAT)");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("selected system-manager node", summary);
        Assert.Contains("shared sysman editor", summary);
        Assert.DoesNotContain("activated its tab", summary);
        Assert.Equal(OpenObjectKind.SysmanNode, result!.Kind);
    }

    [Fact]
    public void OpenObject_SysmanForegroundNotConfirmed_NotError()
    {
        var fake = OpenFake();
        fake.FakeOpenObjectResult = new OpenObjectResult(
            "TIID^Device 1 (EtherCAT)", OpenObjectKind.SysmanNode, Opened: true, AlreadyOpen: false,
            ForegroundRequested: true, ForegroundConfirmed: false, Note: null);

        var (summary, _) = new PlcCodeCoordinator(fake).OpenObject("TIID^Device 1 (EtherCAT)");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("not confirmed", summary);
    }

    [Fact]
    public void OpenObject_SessionThrows_WrappedAsError()
    {
        var fake = OpenFake();
        fake.OpenObjectThrows = new InvalidOperationException(
            "Could not surface system-manager node 'Gerät 9': node not found in the Solution Explorer tree");

        var (summary, result) = new PlcCodeCoordinator(fake).OpenObject("TIID^Device 9");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("not found", summary);
        Assert.Null(result);
    }

    [Fact]
    public void CloseObject_BlankPath_ReturnsError()
    {
        var summary = new PlcCodeCoordinator(OpenFake()).CloseObject("");
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("treePath", summary);
    }

    [Fact]
    public void CloseObject_SysmanKind_ReportsNoTabNotError()
    {
        var fake = OpenFake();
        fake.FakeCloseObjectResult = new CloseObjectResult(
            "TIID^Device 1 (EtherCAT)", OpenObjectKind.SysmanNode, Closed: false, WasOpen: false, Saved: false,
            Note: "system-manager node shares the single sysman editor — no per-node tab to close");

        var summary = new PlcCodeCoordinator(fake).CloseObject("TIID^Device 1 (EtherCAT)");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("No tab to close", summary);
    }

    [Fact]
    public void CloseObject_PlcKind_ReportsClosedTab()
    {
        var fake = OpenFake();
        fake.FakeCloseObjectResult = new CloseObjectResult(
            "^POUs^MAIN", OpenObjectKind.PlcObject, Closed: true, WasOpen: true, Saved: true, Note: null);

        var summary = new PlcCodeCoordinator(fake).CloseObject("^POUs^MAIN");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("Closed object", summary);
        Assert.Contains("saved and", summary);
    }

    // ── COM exception propagation (threading-fixes #13) ───────────────────────

    [Fact]
    public void ListPous_ComException_SurfacedAsStructuredError()
    {
        var comEx = new System.Runtime.InteropServices.COMException(
            "ToolWindows.ErrorList: RPC call failed (0x80010001)", unchecked((int)0x80010001));
        var fake = OpenFake();
        fake.ListPousThrows = comEx;

        var (summary, entries) = new PlcCodeCoordinator(fake).ListPous();

        Assert.StartsWith("[Error]", summary);
        Assert.True(summary.Length > 10, $"Error response must not be empty: '{summary}'");
        Assert.Empty(entries);
    }

    [Fact]
    public void ReadPou_ComException_SurfacedAsStructuredError()
    {
        var comEx = new System.Runtime.InteropServices.COMException(
            "ToolWindows.ErrorList: RPC call failed (0x80010001)", unchecked((int)0x80010001));
        var fake = OpenFake();
        fake.ReadPouThrows = comEx;

        var (summary, body) = new PlcCodeCoordinator(fake).ReadPou("^POUs^MAIN");

        Assert.StartsWith("[Error]", summary);
        Assert.True(summary.Length > 10, $"Error response must not be empty: '{summary}'");
        Assert.Null(body);
    }
}
