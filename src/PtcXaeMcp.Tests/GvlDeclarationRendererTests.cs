using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.TwinCat.Schema;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Golden-style coverage for the pure GVL declaration rendering extracted from
/// NativeTwinCatRunner.RenderGvlText / GetTabPad (Hardening 9, step 1).
/// </summary>
public class GvlDeclarationRendererTests
{
    private static IoPoint Point(string terminal, int channel, string dir, string varName, string? type = null, string? group = null, string? description = null)
        => new()
        {
            TerminalName = terminal,
            Channel = channel,
            Direction = dir,
            VarName = varName,
            Type = type,
            Group = group,
            Description = description
        };

    [Fact]
    public void Render_EmptyList_ProducesEmptyVarBlock()
    {
        string text = GvlDeclarationRenderer.Render([]);

        text.Should().StartWith("{attribute 'qualified_only'}");
        text.Should().Contain("VAR_GLOBAL");
        text.Should().Contain("END_VAR");
    }

    [Fact]
    public void Render_SinglePoint_DefaultsToBoolAndCorrectAtModifier()
    {
        string text = GvlDeclarationRenderer.Render([Point("T1", 0, "I", "xIn1")]);

        text.Should().Contain("xIn1");
        text.Should().Contain("AT %I*");
        text.Should().Contain(": BOOL;");
    }

    [Fact]
    public void Render_OutputDirection_UsesQAtModifier()
    {
        string text = GvlDeclarationRenderer.Render([Point("T1", 0, "Q", "xOut1")]);

        text.Should().Contain("AT %Q*");
    }

    [Fact]
    public void Render_ExplicitType_IsUsedInsteadOfBoolDefault()
    {
        string text = GvlDeclarationRenderer.Render([Point("T1", 0, "I", "xIn1", type: "INT")]);

        text.Should().Contain(": INT;");
    }

    [Fact]
    public void Render_GroupsByTerminalName_SortedAlphabeticallyThenByChannel()
    {
        string text = GvlDeclarationRenderer.Render(
        [
            Point("T2", 1, "I", "b"),
            Point("T1", 1, "I", "a2"),
            Point("T1", 0, "I", "a1")
        ]);

        int t1Index = text.IndexOf("// T1", StringComparison.Ordinal);
        int t2Index = text.IndexOf("// T2", StringComparison.Ordinal);
        int a1Index = text.IndexOf("a1", StringComparison.Ordinal);
        int a2Index = text.IndexOf("a2", StringComparison.Ordinal);

        t1Index.Should().BeLessThan(t2Index, "T1 sorts before T2");
        a1Index.Should().BeLessThan(a2Index, "channel 0 sorts before channel 1 within the same terminal");
    }

    [Fact]
    public void Render_GroupCommentAndDescription_AreConcatenatedAndTrimmed()
    {
        string text = GvlDeclarationRenderer.Render([Point("T1", 0, "I", "x", group: "G1", description: "desc")]);

        text.Should().Contain("// G1 desc");
    }

    [Theory]
    [InlineData(1, 5, "\t")]
    [InlineData(5, 5, "\t")]
    [InlineData(1, 9, "\t\t")]
    public void GetTabPad_ComputesTabsFromDeltaAndTabSize(int currentLength, int targetLength, string expected)
    {
        GvlDeclarationRenderer.GetTabPad(currentLength, targetLength, tabSize: 4).Should().Be(expected);
    }
}
