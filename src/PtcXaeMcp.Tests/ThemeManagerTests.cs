using System.Drawing;
using PtcXaeMcp.WinForms.Theme;

namespace PtcXaeMcp.Tests;

/// <summary>
/// PR #279 review: a theme switch must carry status colours over to the new palette
/// instead of resetting them (status dots went grey, IBN/online state lost its colour).
/// </summary>
public sealed class ThemeManagerTests
{
    public static TheoryData<string> Roles => new() { "Ok", "Warn", "Error", "Inactive" };

    private static Color Role(ThemePalette p, string role) => role switch
    {
        "Ok" => p.Ok,
        "Warn" => p.Warn,
        "Error" => p.Error,
        _ => p.Inactive,
    };

    [Theory]
    [MemberData(nameof(Roles))]
    public void RemapSemantic_MapsEachRoleAcrossPalettes(string role)
    {
        Assert.Equal(Role(ThemeManager.Dark, role), ThemeManager.RemapSemantic(Role(ThemeManager.Light, role), ThemeManager.Dark));
        Assert.Equal(Role(ThemeManager.Light, role), ThemeManager.RemapSemantic(Role(ThemeManager.Dark, role), ThemeManager.Light));
        Assert.Equal(Role(ThemeManager.Light, role), ThemeManager.RemapSemantic(Role(ThemeManager.Light, role), ThemeManager.Light));
    }

    [Fact]
    public void RemapSemantic_ReturnsNullForNonSemanticColours()
    {
        Assert.Null(ThemeManager.RemapSemantic(ThemeManager.Light.Text, ThemeManager.Dark));
        Assert.Null(ThemeManager.RemapSemantic(Color.Transparent, ThemeManager.Dark));
    }
}
