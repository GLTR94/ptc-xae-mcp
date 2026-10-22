using OpenQA.Selenium.Appium;

namespace PtcXaeMcp.AppiumTests;

/// <summary>
/// Verifies the three views of the main TabControl (issue #270): Activity, Approvals and
/// Console, and that each one shows its primary control when selected.
/// </summary>
[Collection("Appium")]
public sealed class TabNavigationTests
{
    private readonly AppiumFixture _fx;

    public TabNavigationTests(AppiumFixture fx) => _fx = fx;

    [Fact]
    public void TabControl_HasExactlyTheThreeViews()
    {
        string[] tabs = ["_tabActivity", "_tabApprovals", "_tabConsole"];
        foreach (var tabId in tabs)
        {
            _fx.Driver.FindElement(MobileBy.AccessibilityId(tabId))
               .Displayed.Should().BeTrue(because: $"tab '{tabId}' must be in the tab strip");
        }

        var tabControl = _fx.Driver.FindElement(MobileBy.AccessibilityId("_tabControl"));
        tabControl.FindElements(OpenQA.Selenium.By.XPath("./TabItem")).Should().HaveCount(3);
    }

    [Fact]
    public void Activity_View_ShowsGridAndClearButton()
    {
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_tabActivity")).Click();

        _fx.Driver.FindElement(MobileBy.AccessibilityId("_gridActivity")).Displayed.Should().BeTrue();
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnActivityClear")).Text.Should().Be("Clear (new run)");
    }

    [Fact]
    public void Approvals_View_ShowsArmAndAbort()
    {
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_tabApprovals")).Click();

        _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnIbnArm")).Text.Should().Be("Arm");
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnIbnAbort")).Text.Should().Contain("ABORT / RELEASE-ALL");
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_lblIbnArmStatus")).Displayed.Should().BeTrue();
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_gridHmiApprovals")).Displayed.Should().BeTrue();
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_txtIbnAudit")).Displayed.Should().BeTrue();

        // #272: the manual step buttons and the watch-symbol snapshot are gone.
        foreach (var removed in new[] { "_btnIbnStartChain", "_btnIbnNextStep", "_btnIbnHold", "_btnIbnSnapshot" })
            _fx.Driver.FindElements(MobileBy.AccessibilityId(removed)).Should().BeEmpty(because: $"{removed} was removed");

        _fx.Driver.FindElement(MobileBy.AccessibilityId("_tabActivity")).Click();
    }

    [Fact]
    public void Console_View_ShowsToolListAndRunButton()
    {
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_tabConsole")).Click();

        _fx.Driver.FindElement(MobileBy.AccessibilityId("_txtConsoleSearch")).Should().NotBeNull();
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_treeConsoleTools")).Should().NotBeNull();
        _fx.Driver.FindElement(MobileBy.AccessibilityId("_btnConsoleRun")).Should().NotBeNull();

        _fx.Driver.FindElement(MobileBy.AccessibilityId("_tabActivity")).Click();
    }
}
