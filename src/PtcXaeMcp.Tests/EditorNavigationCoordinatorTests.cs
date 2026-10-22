using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class EditorNavigationCoordinatorTests
{
    private const string Pou = "TIPC^PLC^PLC Projekt^POUs^02_Machine^FB_Machine";
    private const string Body = "IF NOT InitDone THEN\n\tM_Init();\nEND_IF\n\nCASE Step OF\n\n\t0:\n\t\t;\n\n\t1010:\n\n\t\tStepInfo := 'Zyklusstopp anfordern';\n\n\tREADY_STEP:\n\t\t;\nEND_CASE";

    private static FakeTwinCatSession OpenFake()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.PouBodiesByPath[Pou] = new PouBody("FUNCTION_BLOCK FB_Machine", Body, "ST");
        return fake;
    }

    private static FakeTwinCatSession ClosedFake() => new() { IsConnected = true, SolutionOpen = false };

    private static GotoLineResult Confirmed(int line) => new(
        Pou, EditorSection.Implementation, line, Opened: true, AlreadyOpen: false, PaneFocused: true,
        DialogSeen: true, CursorConfirmed: true, ForegroundRequested: true, ForegroundConfirmed: true,
        Trigger: "Edit.GoTo command", Note: null);

    // ---- LocateStep ------------------------------------------------------------

    [Fact]
    public void LocateStep_BlankPath_ErrorBeforeSession()
    {
        var fake = OpenFake();
        var (summary, loc) = new EditorNavigationCoordinator(fake).LocateStep(" ", 1010, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("treePath", summary);
        Assert.Null(loc);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void LocateStep_BadSelector_ErrorBeforeSession()
    {
        var fake = OpenFake();
        var (summary, _) = new EditorNavigationCoordinator(fake).LocateStep(Pou, 1010, "1 bad");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("stepVariable", summary);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void LocateStep_NoSolution_Error()
    {
        var (summary, loc) = new EditorNavigationCoordinator(ClosedFake()).LocateStep(Pou, 1010, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Null(loc);
    }

    [Fact]
    public void LocateStep_Hit_ReturnsLineLabelAndSnippet()
    {
        var fake = OpenFake();
        var (summary, loc) = new EditorNavigationCoordinator(fake).LocateStep(Pou, 1010, null);

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("Located step 1010", summary);
        Assert.Contains("implementation line 10", summary);
        Assert.NotNull(loc);
        Assert.Equal(10, loc!.Line);
        Assert.Equal("1010", loc.Label);
        Assert.Equal(12, loc.EndLine);
        Assert.Equal("Step", loc.StepVariable);
        Assert.Contains("Zyklusstopp anfordern", loc.Snippet);
        Assert.Contains($"ReadPou({Pou})", fake.CallLog);
    }

    [Fact]
    public void LocateStep_DefaultsSelectorToStep_AndAcceptsExplicit()
    {
        var fake = OpenFake();
        var coord = new EditorNavigationCoordinator(fake);
        Assert.Equal("Step", coord.LocateStep(Pou, 0, "  ").Location!.StepVariable);
        Assert.Equal("Step", coord.LocateStep(Pou, 0, "Step").Location!.StepVariable);
    }

    [Fact]
    public void LocateStep_Miss_ErrorPointsToReadPouAndGotoLine()
    {
        var fake = OpenFake();
        var (summary, loc) = new EditorNavigationCoordinator(fake).LocateStep(Pou, 424242, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("424242", summary);
        Assert.Contains("READY_STEP", summary);          // labels seen help the agent
        Assert.Contains("twincat_goto_line", summary);
        Assert.Null(loc);
    }

    [Fact]
    public void LocateStep_NonStPou_Error()
    {
        var fake = OpenFake();
        fake.PouBodiesByPath[Pou] = new PouBody("FUNCTION_BLOCK FB_Machine", Body, "FBD");
        var (summary, _) = new EditorNavigationCoordinator(fake).LocateStep(Pou, 1010, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("FBD", summary);
    }

    [Fact]
    public void LocateStep_SessionThrows_Error()
    {
        var fake = OpenFake();
        fake.ReadPouThrows = new InvalidOperationException("PLC tree path not found: X");
        var (summary, _) = new EditorNavigationCoordinator(fake).LocateStep(Pou, 1010, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("not found", summary);
    }

    // ---- GotoLine --------------------------------------------------------------

    [Fact]
    public void GotoLine_BlankPath_LineZero_BadSection_ErrorBeforeSession()
    {
        var fake = OpenFake();
        var coord = new EditorNavigationCoordinator(fake);

        Assert.StartsWith("[Error]", coord.GotoLine("", 1, null).Summary);
        Assert.Contains("line must be >= 1", coord.GotoLine(Pou, 0, null).Summary);
        Assert.Contains("section", coord.GotoLine(Pou, 1, "body").Summary);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void GotoLine_NoSolution_Error()
    {
        var (summary, r) = new EditorNavigationCoordinator(ClosedFake()).GotoLine(Pou, 5, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Null(r);
    }

    [Fact]
    public void GotoLine_Confirmed_ReportsCursorSet()
    {
        var fake = OpenFake();
        fake.FakeGotoLineResult = Confirmed(59);
        var (summary, r) = new EditorNavigationCoordinator(fake).GotoLine(Pou, 59, "implementation");

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("Cursor set to implementation line 59", summary);
        Assert.Contains("Go-To dialog seen and closed via Edit.GoTo command", summary);
        Assert.Contains("foreground", summary);
        Assert.NotNull(r);
        Assert.Contains($"GotoLine({Pou}, 59, Implementation)", fake.CallLog);
    }

    [Fact]
    public void GotoLine_DefaultSectionIsImplementation_DeclarationParsed()
    {
        var fake = OpenFake();
        var coord = new EditorNavigationCoordinator(fake);
        coord.GotoLine(Pou, 3, null);
        coord.GotoLine(Pou, 3, "Declaration");
        Assert.Contains($"GotoLine({Pou}, 3, Implementation)", fake.CallLog);
        Assert.Contains($"GotoLine({Pou}, 3, Declaration)", fake.CallLog);
    }

    [Fact]
    public void GotoLine_ForegroundNotConfirmed_IsNotError()
    {
        var fake = OpenFake();
        fake.FakeGotoLineResult = Confirmed(59) with { ForegroundConfirmed = false };
        var (summary, _) = new EditorNavigationCoordinator(fake).GotoLine(Pou, 59, null);

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("not confirmed", summary);
    }

    [Fact]
    public void GotoLine_DialogNeverAppeared_IsError()
    {
        var fake = OpenFake();
        fake.FakeGotoLineResult = Confirmed(59) with { DialogSeen = false, CursorConfirmed = false, Trigger = null, Note = "Edit.GoTo command failed: nicht verfügbar" };
        var (summary, r) = new EditorNavigationCoordinator(fake).GotoLine(Pou, 59, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("never appeared", summary);
        Assert.Contains("nicht verfügbar", summary);
        Assert.NotNull(r);
    }

    [Fact]
    public void GotoLine_DialogStuck_IsError()
    {
        var fake = OpenFake();
        fake.FakeGotoLineResult = Confirmed(59) with { CursorConfirmed = false };
        var (summary, _) = new EditorNavigationCoordinator(fake).GotoLine(Pou, 59, null);
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("did not close", summary);
    }

    [Fact]
    public void GotoLine_SessionThrows_Error()
    {
        var fake = OpenFake();
        fake.GotoLineThrows = new InvalidOperationException("declaration pane not found");
        var (summary, r) = new EditorNavigationCoordinator(fake).GotoLine(Pou, 3, "declaration");
        Assert.StartsWith("[Error]", summary);
        Assert.Contains("declaration pane", summary);
        Assert.Null(r);
    }

    // ---- ShowStep --------------------------------------------------------------

    [Fact]
    public void ShowStep_Miss_DoesNotTouchTheEditor()
    {
        var fake = OpenFake();
        var (summary, loc, r) = new EditorNavigationCoordinator(fake).ShowStep(Pou, 424242, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Null(loc);
        Assert.Null(r);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("GotoLine(", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowStep_Hit_LocatesThenNavigatesToImplementationLine()
    {
        var fake = OpenFake();
        fake.FakeGotoLineResult = Confirmed(10);
        var (summary, loc, r) = new EditorNavigationCoordinator(fake).ShowStep(Pou, 1010, null);

        Assert.DoesNotContain("[Error]", summary);
        Assert.Contains("Located step 1010", summary);
        Assert.Contains("Cursor set to implementation line 10", summary);
        Assert.Equal(10, loc!.Line);
        Assert.NotNull(r);
        Assert.Equal(new[] { $"ReadPou({Pou})", $"GotoLine({Pou}, 10, Implementation)" }, fake.CallLog);
    }

    [Fact]
    public void ShowStep_GotoFails_ErrorStillCarriesLocation()
    {
        var fake = OpenFake();
        fake.GotoLineThrows = new InvalidOperationException("Could not obtain the XAE main-window handle");
        var (summary, loc, r) = new EditorNavigationCoordinator(fake).ShowStep(Pou, 1010, null);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("located at implementation line 10", summary);
        Assert.NotNull(loc);
        Assert.Null(r);
    }
}
