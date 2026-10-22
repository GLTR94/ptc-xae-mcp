using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// twincat_plc_list_entries / twincat_plc_edit_entries on the coordinator: element resolution, the refusals that must
// happen BEFORE the logout, and the write-back through the verified path. The text work itself is covered by
// DeclarationEntryEditorTests.
public class PlcEntryCoordinatorTests
{
    private const string Struct = "^DUTs^ST_Point";
    private const string Enum = "^DUTs^E_Color";
    private const string Alias = "^DUTs^A_Handle";
    private const string Gvl = "^GVLs^GVL_Main";
    private const string Fb = "^POUs^FB_Motor";
    private const string Itf = "^POUs^I_Motor";
    private const string Method = "^POUs^FB_Motor^Run";
    private const string Action = "^POUs^FB_Motor^Reset";
    private const string Prop = "^POUs^FB_Motor^Speed";

    private const string StructText = "TYPE ST_Point :\nSTRUCT\n    X : INT;\n    Y : INT;\nEND_STRUCT\nEND_TYPE\n";
    private const string EnumText = "TYPE E_Color :\n(\n    Red := 0,\n    Green := 1\n);\nEND_TYPE\n";
    private const string FbText = "FUNCTION_BLOCK FB_Motor\nVAR_INPUT\n    bEnable : BOOL;\nEND_VAR\nVAR\n    nCount : INT;\nEND_VAR\n";
    private const string MethodText = "METHOD Run : BOOL\nVAR_INPUT\n    bGo : BOOL;\nEND_VAR\n";

    private static FakeTwinCatSession OpenFake()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        void Node(string path, int itemType, string decl, string impl = "", string lang = "")
        {
            fake.PlcNodeItemTypes[path] = itemType;
            fake.PouBodiesByPath[path] = new PouBody(decl, impl, lang);
            fake.GvlDeclarationsByPath[path] = decl;
        }
        Node(Struct, PlcItemType.DutStruct, StructText);
        Node(Enum, PlcItemType.Dut, EnumText);
        Node(Alias, PlcItemType.DutAlias, "TYPE A_Handle : UDINT;\nEND_TYPE\n");
        Node(Gvl, PlcItemType.Gvl, "VAR_GLOBAL\n    gA : INT;\nEND_VAR\n");
        Node(Fb, PlcItemType.PouFunctionBlock, FbText, "x := 1;", "ST");
        Node(Itf, PlcItemType.Interface, "INTERFACE I_Motor\n");
        Node(Method, PlcItemType.Method, MethodText, "Run := TRUE;", "ST");
        Node(Action, PlcItemType.Action, "", "x := 0;", "ST");
        Node(Prop, PlcItemType.Property, "PROPERTY Speed : REAL\n");
        fake.PlcNodeItemTypes["^POUs"] = PlcItemType.Folder;
        return fake;
    }

    private static PlcObjectCoordinator Coord(FakeTwinCatSession fake) => new(fake);

    private static void AssertNothingHappened(FakeTwinCatSession fake)
    {
        fake.CallLog.Should().NotContain(c => c.StartsWith("LogoutPlc", StringComparison.Ordinal));
        fake.CallLog.Should().NotContain(c => c.StartsWith("WritePou", StringComparison.Ordinal));
    }

    // ---- list_entries ---------------------------------------------------------

    [Fact]
    public void ListEntries_Struct_ReadsTheFields()
    {
        var fake = OpenFake();
        var (summary, entries) = Coord(fake).ListEntries(Struct, "dut");
        summary.Should().Be($"Found 2 entries in DUT/STRUCT '{Struct}'.");
        entries.Select(e => (e.Name, e.Type)).Should().Equal(("X", "INT"), ("Y", "INT"));
    }

    [Fact]
    public void ListEntries_Enum_AndSectionFilterOfAPou()
    {
        var fake = OpenFake();
        Coord(fake).ListEntries(Enum, "DUT").Entries.Select(e => e.Name).Should().Equal("Red", "Green");
        Coord(fake).ListEntries(Fb, "POU", "var").Entries.Select(e => e.Name).Should().Equal("nCount");
        Coord(fake).ListEntries(Fb, "POU").Entries.Select(e => e.Section).Should().Equal("VAR_INPUT", "VAR");
    }

    [Fact]
    public void ListEntries_Member_ReadsTheMethodVariables()
    {
        var fake = OpenFake();
        Coord(fake).ListEntries(Method, "MEMBER").Entries.Select(e => e.Name).Should().Equal("bGo");
    }

    [Fact]
    public void ListEntries_NoEntries_SaysSo()
    {
        var fake = OpenFake();
        Coord(fake).ListEntries(Fb, "POU", "VAR_OUTPUT").Summary.Should().StartWith("[Error] No section 'VAR_OUTPUT'");
        fake.PouBodiesByPath[Fb] = new PouBody("FUNCTION_BLOCK FB_Motor\nVAR\nEND_VAR\n", "", "ST");
        Coord(fake).ListEntries(Fb, "POU").Summary.Should().Be($"No entries in POU/FUNCTION_BLOCK '{Fb}'.");
    }

    [Fact]
    public void ListEntries_NotesEntriesThatCannotBeEdited()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath[Fb] = new PouBody("VAR\n    a, b : INT;\n    c : INT;\nEND_VAR\n", "", "ST");
        var (summary, entries) = Coord(fake).ListEntries(Fb, "POU");
        summary.Should().Contain("1 cannot be edited");
        entries[0].Problem.Should().NotBeNull();
    }

    [Theory]
    [InlineData(Alias, "DUT", "alias only names a base type")]
    [InlineData(Itf, "INTERFACE", "holds only the header")]
    [InlineData(Action, "MEMBER", "no declaration section")]
    [InlineData(Prop, "MEMBER", "carries only its header")]
    [InlineData("^POUs", "FOLDER", "no declaration entries")]
    public void ListEntries_ElementsWithoutEntries_SayWhy(string path, string kind, string reason)
    {
        var fake = OpenFake();
        Coord(fake).ListEntries(path, kind).Summary.Should().StartWith("[Error]").And.Contain(reason);
    }

    [Fact]
    public void ListEntries_WrongKind_NamesBoth()
    {
        var fake = OpenFake();
        Coord(fake).ListEntries(Gvl, "DUT").Summary.Should().Be($"[Error] '{Gvl}' is a GVL, not a DUT.");
    }

    [Theory]
    [InlineData("TEXTLIST")]
    [InlineData("RECIPE")]
    public void ListEntries_TableKinds_AreNotSupportedYet(string kind)
    {
        var fake = OpenFake();
        Coord(fake).ListEntries(Struct, kind).Summary.Should().Contain("not supported");
    }

    // ---- edit_entries ---------------------------------------------------------

    [Fact]
    public void Edit_AddField_LogsOutThenWritesTheEditedDeclaration()
    {
        var fake = OpenFake();
        string result = Coord(fake).EditEntries(Struct, "DUT", "add", "Z", type: "INT", comment: "depth");
        result.Should().StartWith("Added entry 'Z' to section 'STRUCT'.").And.Contain("verified by read-back").And.Contain("twincat_build_plc");
        fake.PouBodiesByPath[Struct].Declaration.Should().Be(StructText.Replace("END_STRUCT", "    Z : INT; // depth\nEND_STRUCT", StringComparison.Ordinal));

        int logout = fake.CallLog.FindIndex(c => c.StartsWith("LogoutPlc", StringComparison.Ordinal));
        int write = fake.CallLog.FindIndex(c => c.StartsWith("WritePouBody(" + Struct, StringComparison.Ordinal));
        logout.Should().BeGreaterThanOrEqualTo(0);
        write.Should().BeGreaterThan(logout);
        fake.PouBodiesByPath[Struct].Implementation.Should().BeEmpty();
    }

    [Fact]
    public void Edit_WritesOnlyTheDeclaration()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Fb, "POU", "set", "nCount", type: "DINT");
        fake.CallLog.Should().Contain($"WritePouBody({Fb}, decl=<set>, impl=<null>)");
        fake.PouBodiesByPath[Fb].Implementation.Should().Be("x := 1;");
    }

    [Fact]
    public void Edit_Member_UsesTheMemberWritePath()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Method, "MEMBER", "add", "bStop", "VAR_INPUT", type: "BOOL").Should().StartWith("Added entry 'bStop'");
        fake.CallLog.Should().Contain($"WritePouMember({Method}, decl=<set>, impl=<null>)");
    }

    [Fact]
    public void Edit_Remove_AndEnumValue()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Enum, "DUT", "add", "Blue", value: "2").Should().StartWith("Added entry 'Blue'");
        fake.PouBodiesByPath[Enum].Declaration.Should().Contain("Green := 1,\n    Blue := 2\n)");

        string removal = Coord(fake).EditEntries(Struct, "DUT", "remove", "Y");
        removal.Should().StartWith("Removed entry 'Y'").And.Contain("twincat_rebuild_plc");
        fake.PouBodiesByPath[Struct].Declaration.Should().NotContain("Y : INT");
    }

    [Fact]
    public void Edit_Persistent_SectionOfAGvl_IsCreated()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Gvl, "GVL", "add", "gP", "VAR_GLOBAL PERSISTENT", type: "DINT").Should().Contain("new section 'VAR_GLOBAL PERSISTENT'");
        fake.PouBodiesByPath[Gvl].Declaration.Should().EndWith("VAR_GLOBAL PERSISTENT\n    gP : DINT;\nEND_VAR\n");
    }

    [Theory]
    [InlineData("", "Unknown op")]
    [InlineData("insert", "Unknown op 'insert'")]
    [InlineData("delete", "Unknown op 'delete'")]
    public void Edit_UnknownOp_IsRefusedBeforeAnything(string op, string expected)
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Struct, "DUT", op, "X").Should().StartWith("[Error]").And.Contain(expected);
        fake.CallLog.Should().BeEmpty();
    }

    [Fact]
    public void Edit_Op_IsCaseInsensitive()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Struct, "DUT", " REMOVE ", "Y").Should().StartWith("Removed entry");
    }

    [Fact]
    public void Edit_RefusalsHappenBeforeTheLogout()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Struct, "DUT", "add", "X", type: "INT").Should().Contain("already exists");
        Coord(fake).EditEntries(Struct, "DUT", "remove", "nope").Should().Contain("No entry 'nope'");
        Coord(fake).EditEntries(Struct, "DUT", "set", "X").Should().Contain("changes nothing");
        Coord(fake).EditEntries(Struct, "DUT", "add", "Q", type: "INT; evil : INT").Should().StartWith("[Error]");
        Coord(fake).EditEntries(Gvl, "DUT", "remove", "gA").Should().Be($"[Error] '{Gvl}' is a GVL, not a DUT.");
        Coord(fake).EditEntries(Alias, "DUT", "add", "x", type: "INT").Should().Contain("alias only names a base type");
        Coord(fake).EditEntries(Itf, "INTERFACE", "add", "x", type: "INT").Should().Contain("holds only the header");
        Coord(fake).EditEntries(Fb, "POU", "add", "x", type: "INT").Should().Contain("Several sections exist");
        AssertNothingHappened(fake);
    }

    [Fact]
    public void Edit_TableKinds_AreNotSupportedYet()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Struct, "TEXTLIST", "add", "x").Should().Contain("not supported");
        fake.CallLog.Should().BeEmpty();
    }

    [Fact]
    public void Edit_ADeclarationThatAlreadyEmbedsAMemberHeader_IsRefusedByTheLint()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath[Fb] = new PouBody("FUNCTION_BLOCK FB_Motor\nMETHOD Foo : BOOL\nVAR\n    a : INT;\nEND_VAR\n", "", "ST");
        Coord(fake).EditEntries(Fb, "POU", "add", "b", type: "INT").Should().Contain("embedded 'METHOD' header");
        AssertNothingHappened(fake);
    }

    [Fact]
    public void Edit_StillOnline_IsRefusedAndNothingIsWritten()
    {
        var fake = OpenFake();
        fake.FakeLogoutPlcResult = new LogoutPlcResult(true, true, "still online");
        Coord(fake).EditEntries(Struct, "DUT", "remove", "Y").Should().Contain("Online");
        fake.CallLog.Should().NotContain(c => c.StartsWith("WritePou", StringComparison.Ordinal));
    }

    [Fact]
    public void Edit_WriteFails_IsReportedAsAnError()
    {
        var fake = OpenFake();
        fake.WritePouBodyThrows = new InvalidOperationException("DeclarationText verify failed");
        Coord(fake).EditEntries(Struct, "DUT", "remove", "Y").Should().Be("[Error] DeclarationText verify failed");
    }

    [Fact]
    public void Edit_Attributes_AreForwardedToTheEditor()
    {
        var fake = OpenFake();
        Coord(fake).EditEntries(Struct, "DUT", "set", "X", attributes: ["TcInitSymbol"]).Should().StartWith("Changed entry 'X'");
        fake.PouBodiesByPath[Struct].Declaration.Should().Contain("    {attribute 'TcInitSymbol'}\n    X : INT;");
    }

    [Fact]
    public void Edit_NoSolution_IsAnError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        Coord(fake).EditEntries(Struct, "DUT", "remove", "Y").Should().StartWith("[Error]");
    }
}
