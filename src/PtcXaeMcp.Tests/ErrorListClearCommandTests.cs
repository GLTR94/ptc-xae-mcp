using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #150 C-16: the Error List clear command is resolved at runtime, DE/EN fallbacks after it.</summary>
public class ErrorListClearCommandTests
{
    [Fact]
    public void ResolvedName_ComesFirst_ThenGermanAndEnglish() =>
        Assert.Equal(
            new[] { "OtherContextMenus.ErrorList.Effacer", ErrorListClearCommand.GermanName, ErrorListClearCommand.EnglishName },
            ErrorListClearCommand.Candidates("OtherContextMenus.ErrorList.Effacer"));

    [Fact]
    public void ResolvedGermanName_IsNotRepeated() =>
        Assert.Equal(
            new[] { ErrorListClearCommand.GermanName, ErrorListClearCommand.EnglishName },
            ErrorListClearCommand.Candidates("OtherContextMenus.ErrorList.Löschen"));

    [Fact]
    public void Unresolved_FallsBackToTheKnownNames() =>
        Assert.Equal(
            new[] { ErrorListClearCommand.GermanName, ErrorListClearCommand.EnglishName },
            ErrorListClearCommand.Candidates(null));
}
