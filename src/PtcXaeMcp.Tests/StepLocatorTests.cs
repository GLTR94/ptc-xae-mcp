using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class StepLocatorTests
{
    // Mirrors the PTC template's FB_Machine shape: numeric, symbolic and list labels,
    // a commented-out step, a nested CASE, a string containing a colon.
    private const string Body = """
        IF NOT InitDone THEN
        	M_Init();
        	RETURN;
        END_IF

        CASE Step OF

        	0:

        		StepInfo := 'Einrichten: Warte auf Auto';
        		IF Auto THEN
        			M_NextStep(AUTO_START);
        		END_IF

        	AUTO_START:

        		StepInfo := 'Auto Start';

        // Manual

        // 	10:
        // 		;

        	100, 1110:

        		StepInfo := 'Module starten';
        		CASE MachineReference.OperatingMode OF
        			E_OperatingMode.Manual:		M_NextStep(200);
        			200: M_NextStep(300);
        		END_CASE

        	1010:

        		StepInfo := 'Zyklusstopp anfordern';
        		IF M_Stop() THEN
        			M_NextStep(CYCLE_STOP_STEP);
        		END_IF


        	2000:
        		StepInfo := 'Running';
        	(* 3000:
        		old *)
        	ELSE
        		;
        END_CASE

        M_Cyclic();
        """;

    [Fact]
    public void Locate_NumericLiteral_ReturnsLabelLineAndBranch()
    {
        var r = StepLocator.Locate(Body, 1010, "Step");

        Assert.True(r.Found, r.Reason);
        Assert.Equal(32, r.Line);
        Assert.Equal("1010", r.Label);
        Assert.Equal(37, r.EndLine);
        Assert.StartsWith("\t1010:", r.Snippet);
        Assert.Contains("M_NextStep(CYCLE_STOP_STEP)", r.Snippet);
        Assert.DoesNotContain("2000:", r.Snippet);
    }

    [Fact]
    public void Locate_CommaList_MatchesEachMember()
    {
        var a = StepLocator.Locate(Body, 100, "Step");
        var b = StepLocator.Locate(Body, 1110, "Step");

        Assert.True(a.Found);
        Assert.True(b.Found);
        Assert.Equal(a.Line, b.Line);
        Assert.Equal("100, 1110", a.Label);
    }

    [Fact]
    public void Locate_CommentedOutLabel_IsIgnored()
    {
        var r = StepLocator.Locate(Body, 10, "Step");
        Assert.False(r.Found);
        Assert.DoesNotContain("10", r.LabelsSeen);
    }

    [Fact]
    public void Locate_BlockCommentedLabel_IsIgnored()
    {
        var r = StepLocator.Locate(Body, 3000, "Step");
        Assert.False(r.Found);
    }

    [Fact]
    public void Locate_NestedCaseLabel_IsNotMatchedAtTopLevel()
    {
        // 200 exists only inside the nested CASE MachineReference.OperatingMode OF.
        var r = StepLocator.Locate(Body, 200, "Step");
        Assert.False(r.Found);
        Assert.DoesNotContain("200", r.LabelsSeen);
    }

    [Fact]
    public void Locate_LastBranchBeforeElse_EndsBeforeElse()
    {
        var r = StepLocator.Locate(Body, 2000, "Step");
        Assert.True(r.Found);
        Assert.Equal(40, r.Line);
        Assert.Equal(41, r.EndLine);          // trailing block comment is not part of the branch
        Assert.DoesNotContain("ELSE", r.Snippet);
        Assert.DoesNotContain("3000", r.Snippet);
    }

    [Fact]
    public void Locate_TrailingCommentHeaderOfNextStep_IsTrimmed()
    {
        const string body = "CASE Step OF\n100:\n\tx := 1;\n\n// Manual\n\n200:\n\tx := 2;\nEND_CASE";
        var r = StepLocator.Locate(body, 100, "Step");
        Assert.True(r.Found);
        Assert.Equal(3, r.EndLine);
        Assert.DoesNotContain("Manual", r.Snippet);
    }

    [Fact]
    public void Locate_SymbolicLabel_IsMissButListedInLabelsSeen()
    {
        var r = StepLocator.Locate(Body, 424242, "Step");

        Assert.False(r.Found);
        Assert.Contains("AUTO_START", r.LabelsSeen);
        Assert.Contains("0", r.LabelsSeen);
        Assert.Contains("100, 1110", r.LabelsSeen);
        Assert.Contains("labels seen", r.Reason);
        Assert.Contains("424242", r.Reason);
    }

    [Fact]
    public void Locate_WrongSelectorVariable_ReportsTheActualSelector()
    {
        var r = StepLocator.Locate(Body, 0, "State");

        Assert.False(r.Found);
        Assert.Contains("CASE State OF", r.Reason);
        Assert.Contains("'Step'", r.Reason);
        Assert.Empty(r.LabelsSeen);
    }

    [Fact]
    public void Locate_SelectorIsCaseInsensitive()
    {
        var r = StepLocator.Locate(Body, 0, "step");
        Assert.True(r.Found);
        Assert.Equal(8, r.Line);
    }

    [Fact]
    public void Locate_NegativeLiteral_Matches()
    {
        const string body = "CASE Step OF\n\t-1:\n\t\t;\n\t5:\n\t\t;\nEND_CASE";
        var r = StepLocator.Locate(body, -1, "Step");
        Assert.True(r.Found);
        Assert.Equal(2, r.Line);
        Assert.Equal(3, r.EndLine);
    }

    [Fact]
    public void Locate_CrLfBody_UsesSameLineNumbersAsLf()
    {
        string crlf = Body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        Assert.Equal(StepLocator.Locate(Body, 1010, "Step").Line, StepLocator.Locate(crlf, 1010, "Step").Line);
    }

    [Fact]
    public void Locate_LabelWithCodeOnSameLine_Matches()
    {
        const string body = "CASE Step OF\n10: x := 1;\n20: x := 2;\nEND_CASE";
        var r = StepLocator.Locate(body, 20, "Step");
        Assert.True(r.Found);
        Assert.Equal(3, r.Line);
    }

    [Fact]
    public void Locate_RangeLabel_IsSeenButNotMatched()
    {
        const string body = "CASE Step OF\n10..20:\n\t;\nEND_CASE";
        var r = StepLocator.Locate(body, 15, "Step");
        Assert.False(r.Found);
        Assert.Contains("10..20", r.LabelsSeen);
    }

    [Fact]
    public void Locate_NoCaseAtAll_ReportsMissingCase()
    {
        var r = StepLocator.Locate("x := 1;\ny := 2;", 1, "Step");
        Assert.False(r.Found);
        Assert.Contains("no top-level 'CASE Step OF'", r.Reason);
    }

    [Fact]
    public void Locate_BlankSelector_Throws()
    {
        Assert.Throws<ArgumentException>(() => StepLocator.Locate(Body, 1, " "));
    }

    [Fact]
    public void StripCommentsAndStrings_KeepsLineAlignmentAndBlanksStringColons()
    {
        var stripped = StepLocator.StripCommentsAndStrings(new[] { "a := 'x: y'; // 10:", "(* 20:", "still *) 30:" });

        Assert.Equal(3, stripped.Length);
        Assert.DoesNotContain(":", stripped[0].Replace(":=", ""));
        Assert.DoesNotContain("20", stripped[1]);
        Assert.Contains("30:", stripped[2]);
    }
}
