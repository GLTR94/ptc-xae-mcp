using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// twincat_open_license_manager_and_request_trial: the trial button exists only once SYSTEM &gt; License is
/// open, so the tool opens the editor when the first click finds no button and retries while it renders.
/// </summary>
public class TrialLicenseButtonFlowTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    [Fact]
    public void ButtonAlreadyThere_ClicksWithoutOpeningTheEditor()
    {
        int opens = 0, sleeps = 0;
        var outcome = TrialLicenseButtonFlow.Run(() => true, () => { opens++; return "opened"; }, Window, _ => sleeps++);

        Assert.True(outcome.Clicked);
        Assert.Null(outcome.OpenEditorNote);
        Assert.Equal(0, opens);
        Assert.Equal(0, sleeps);
    }

    [Fact]
    public void ButtonAppearsAfterTheEditorOpened_OpensOnceAndClicks()
    {
        int clicks = 0, opens = 0;
        bool editorOpen = false;
        var outcome = TrialLicenseButtonFlow.Run(
            () => { clicks++; return editorOpen && clicks >= 3; },
            () => { opens++; editorOpen = true; return "selected 'Lizenz'; Enter sent"; },
            Window, _ => { });

        Assert.True(outcome.Clicked);
        Assert.Equal("selected 'Lizenz'; Enter sent", outcome.OpenEditorNote);
        Assert.Equal(1, opens);
        Assert.Equal(3, clicks);
    }

    [Fact]
    public void ButtonNeverAppears_GivesUpAfterTheWindowAndReportsTheNote()
    {
        int clicks = 0, opens = 0;
        var slept = TimeSpan.Zero;
        var outcome = TrialLicenseButtonFlow.Run(
            () => { clicks++; return false; },
            () => { opens++; return "the XAE is not the foreground window"; },
            Window, d => slept += d);

        Assert.False(outcome.Clicked);
        Assert.Equal("the XAE is not the foreground window", outcome.OpenEditorNote);
        Assert.Equal(1, opens);
        Assert.Equal(Window, slept);
        Assert.Equal(1 + (int)(Window / TrialLicenseButtonFlow.PollStep), clicks);
    }
}
