using OpenQA.Selenium.Appium;

namespace PtcXaeMcp.AppiumTests;

/// <summary>
/// Verifies the main window shell (issue #270): title, the header with product name, the
/// XAE / Project / MCP status chips, Stop, Menu and the theme toggle.
/// </summary>
[Collection("Appium")]
public sealed class MainWindowTests
{
    private readonly AppiumFixture _fx;

    public MainWindowTests(AppiumFixture fx) => _fx = fx;

    [Fact]
    public void Window_HasCorrectTitle()
    {
        _fx.Driver.Title.Should().StartWith("Pneutronic XAE MCP");
    }

    [Fact]
    public void Header_ProductNameLabel_ShowsAppName()
    {
        var label = _fx.Driver.FindElement(MobileBy.AccessibilityId("_lblProductName"));
        label.Displayed.Should().BeTrue();
        label.Text.Should().StartWith("Pneutronic XAE MCP");
    }

    [Theory]
    [InlineData("_lblXaeState", "XAE")]
    [InlineData("_lblProjectState", "Project")]
    [InlineData("_lblMcpCaption", "MCP")]
    public void Header_StatusChip_IsPresent(string automationId, string prefix)
    {
        var label = _fx.Driver.FindElement(MobileBy.AccessibilityId(automationId));
        label.Displayed.Should().BeTrue();
        label.Text.Should().StartWith(prefix);
    }

    [Fact]
    public void Header_StopButton_IsEnabled()
    {
        var btn = _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnStop"));
        btn.Enabled.Should().BeTrue();
        btn.Text.Should().Be("Stop");
    }

    [Fact]
    public void Header_MenuButton_IsEnabled()
    {
        var btn = _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnMenu"));
        btn.Enabled.Should().BeTrue();
        btn.Text.Should().StartWith("Menu");
    }

    [Fact]
    public void Header_ThemeToggleButton_IsEnabled()
    {
        var btn = _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnThemeToggle"));
        btn.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Header_ThemeToggle_CyclesWithoutError()
    {
        var btn = _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnThemeToggle"));
        btn.Click();
        btn.Click(); // toggle back so theme is unaffected for other tests
    }
}
