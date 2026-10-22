namespace PtcXaeMcp.WinForms.TwinCat;

// twincat_open_license_manager_and_request_trial. The 'Aktiviere 7 Tage Testlizenz...' button is a
// Win32 child of the License editor page, and that page exists only while SYSTEM > License is open in
// the shared system-manager editor. Live (TcXaeShell 4026, 2026-10-08, right after open_project):
// LookupTreeItem("TIRC^License") does not open it, and selecting the Solution-Explorer row alone does
// not either while no system-manager editor is open — the row has to be activated (Enter). So: click
// when the button is already there; otherwise open the editor once and retry the click while the page
// renders. Pure, so the sequence is unit-tested without an XAE.
internal static class TrialLicenseButtonFlow
{
    /// <summary>The result: whether the button was clicked, and what opening the editor reported (null when it was not needed).</summary>
    public sealed record Outcome(bool Clicked, string? OpenEditorNote);

    public static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// <paramref name="tryClick"/> first; when it finds no button, <paramref name="openEditor"/> once (it never
    /// throws and returns a note), then <paramref name="tryClick"/> every <see cref="PollStep"/> until
    /// <paramref name="retryWindow"/> has elapsed.
    /// </summary>
    public static Outcome Run(Func<bool> tryClick, Func<string> openEditor, TimeSpan retryWindow, Action<TimeSpan> sleep)
    {
        if (tryClick()) return new Outcome(true, null);

        string note = openEditor();
        for (var waited = TimeSpan.Zero; waited < retryWindow; waited += PollStep)
        {
            sleep(PollStep);
            if (tryClick()) return new Outcome(true, note);
        }
        return new Outcome(false, note);
    }
}
