using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class GoToDialogMatcherTests
{
    private const string WinFormsWindow = "WindowsForms10.Window.8.app.0.3bf8826_r13_ad1";
    private const string WinFormsEdit = "WindowsForms10.EDIT.app.0.3bf8826_r13_ad1";
    private const string WinFormsButton = "WindowsForms10.BUTTON.app.0.3bf8826_r13_ad1";
    private const string WinFormsStatic = "WindowsForms10.STATIC.app.0.3bf8826_r13_ad1";

    // Exactly the shape observed live on TcXaeShell 4026.22 (German UI).
    private static WindowShape GermanDialog() => new(true, WinFormsWindow, new List<(string, string)>
    {
        (WinFormsButton, "Abbrechen"),
        (WinFormsButton, "OK"),
        (WinFormsEdit, ""),
        (WinFormsStatic, "&Zeilennummer"),
    });

    [Fact]
    public void GermanShape_Matches() => Assert.True(GoToDialogMatcher.IsGoToDialog(GermanDialog()));

    [Fact]
    public void EnglishShape_Matches()
    {
        var shape = new WindowShape(true, WinFormsWindow, new List<(string, string)>
        {
            (WinFormsButton, "Cancel"), (WinFormsButton, "&OK"), (WinFormsEdit, ""), (WinFormsStatic, "&Line number"),
        });
        Assert.True(GoToDialogMatcher.IsGoToDialog(shape));
    }

    [Fact]
    public void NotOwnedByXae_DoesNotMatch()
    {
        var shape = GermanDialog() with { OwnedByXae = false };
        Assert.False(GoToDialogMatcher.IsGoToDialog(shape));
    }

    [Fact]
    public void StandardDialogClass_DoesNotMatch()
    {
        // #32770 = the security-code / restart dialogs handled by Win32DialogAutomation.
        var shape = GermanDialog() with { ClassName = "#32770" };
        Assert.False(GoToDialogMatcher.IsGoToDialog(shape));
    }

    [Fact]
    public void TwoEdits_DoesNotMatch()
    {
        var children = new List<(string, string)>(GermanDialog().Children) { (WinFormsEdit, "") };
        Assert.False(GoToDialogMatcher.IsGoToDialog(GermanDialog() with { Children = children }));
    }

    [Fact]
    public void NoOkButton_DoesNotMatch()
    {
        var shape = new WindowShape(true, WinFormsWindow, new List<(string, string)>
        {
            (WinFormsButton, "Abbrechen"), (WinFormsEdit, ""),
        });
        Assert.False(GoToDialogMatcher.IsGoToDialog(shape));
    }

    [Fact]
    public void LoginStyleModal_WithRadiosAndManyButtons_DoesNotMatch()
    {
        var shape = new WindowShape(true, WinFormsWindow, new List<(string, string)>
        {
            (WinFormsButton, "Online-Change"), (WinFormsButton, "Download"), (WinFormsButton, "Ohne Änderung"),
            (WinFormsButton, "OK"), (WinFormsButton, "Abbrechen"), (WinFormsEdit, ""),
        });
        Assert.False(GoToDialogMatcher.IsGoToDialog(shape));
    }

    [Theory]
    [InlineData("OK", true)]
    [InlineData("&OK", true)]
    [InlineData("Ok", true)]
    [InlineData(" OK ", true)]
    [InlineData("Abbrechen", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOkText_RecognisesWinFormsVariants(string? text, bool expected) =>
        Assert.Equal(expected, GoToDialogMatcher.IsOkText(text));
}
