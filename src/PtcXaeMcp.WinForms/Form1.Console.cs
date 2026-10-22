using PtcXaeMcp.WinForms.Diagnostics;

namespace PtcXaeMcp.WinForms;

public partial class Form1
{
    // Issue #271: wiring between the Activity view ("Run again in Console") and the Console view.

    /// <summary>Points the Console view at the in-process server and connects the Run-again hand-off.</summary>
    private void WireConsole()
    {
        _consoleView.Attach(McpBridge.Port);
        _activityView.RunAgainRequested += OnRunAgainRequested;
    }

    private async void OnRunAgainRequested(string tool, string argumentsJson)
    {
        _tabControl.SelectedTab = _tabConsole;
        try
        {
            await _consoleView.LoadCallAsync(tool, argumentsJson);
        }
        catch (Exception ex)
        {
            // async void: nothing may escape to the message loop.
            HostLog.Shared.Warn($"Console: could not load '{tool}' from the Activity view: {ex.Message}");
        }
    }
}
