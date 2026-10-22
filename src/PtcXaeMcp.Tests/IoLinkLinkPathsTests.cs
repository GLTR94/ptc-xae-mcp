using System.Text.Json;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class IoLinkLinkPathsTests
{
    private const string Term = "TIID^Device 1 (EtherCAT)^+CAB1-A1 (EK1200)^Term 28 (EL6224)";

    [Fact]
    public void Candidates_BuildsRealLeafPathFirst()
    {
        var cands = IoLinkLinkPaths.Candidates(Term, 1, "Valve 1 solenoid 00").ToList();

        // The real EL6224/100001 shape must be the first candidate probed.
        Assert.Equal($"{Term}^IO-Link Port1^Valve 1 solenoid 00", cands[0]);
        Assert.Contains($"{Term}^Valve 1 solenoid 00", cands);   // flat fallback
    }

    [Fact]
    public void Candidates_DefaultsToPort1WhenNullOrZero()
    {
        Assert.Equal($"{Term}^IO-Link Port1^X", IoLinkLinkPaths.Candidates(Term, null, "X").First());
        Assert.Equal($"{Term}^IO-Link Port1^X", IoLinkLinkPaths.Candidates(Term, 0, "X").First());
    }

    [Fact]
    public void Candidates_UsesGivenPortNumber()
    {
        Assert.Equal($"{Term}^IO-Link Port3^X", IoLinkLinkPaths.Candidates(Term, 3, "X").First());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Candidates_EmptyChannel_YieldsNothing(string ch)
        => Assert.Empty(IoLinkLinkPaths.Candidates(Term, 1, ch));

    [Fact]
    public void Candidates_EmptyTerminal_YieldsNothing()
        => Assert.Empty(IoLinkLinkPaths.Candidates("", 1, "X"));

    [Fact]
    public void Candidates_AreDistinct()
    {
        var cands = IoLinkLinkPaths.Candidates(Term, 1, "Valve 1 solenoid 00").ToList();
        Assert.Equal(cands.Count, cands.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void IoPoint_ParsesIoLinkFields_AndFlagsIsIoLink()
    {
        const string json = """
            { "terminalName": "A1.41 (EL6224)", "ioLinkPort": 1,
              "ioLinkChannel": "Valve 1 solenoid 00",
              "direction": "Q", "type": "BOOL", "varName": "Q_Ziehen_GST" }
            """;

        var point = JsonSerializer.Deserialize<IoPoint>(json)!;

        Assert.Equal("A1.41 (EL6224)", point.TerminalName);
        Assert.Equal(1, point.IoLinkPort);
        Assert.Equal("Valve 1 solenoid 00", point.IoLinkChannel);
        Assert.Equal("Q_Ziehen_GST", point.VarName);
        Assert.True(point.IsIoLink);
    }

    [Fact]
    public void IoPoint_PlainChannel_IsNotIoLink()
    {
        const string json = """
            { "terminalName": "A1.05 (EL1809)", "channel": 3, "direction": "I", "varName": "I_Start" }
            """;

        var point = JsonSerializer.Deserialize<IoPoint>(json)!;

        Assert.False(point.IsIoLink);
        Assert.Null(point.IoLinkChannel);
        Assert.Equal(3, point.Channel);
    }
}
