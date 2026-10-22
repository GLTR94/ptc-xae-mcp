namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Pure description of a top-level window, as far as the Go-To dialog matcher needs
/// it. Built from Win32 by <see cref="GoToDialogAutomation"/>; built by hand in unit
/// tests.
/// </summary>
/// <param name="OwnedByXae">GetWindow(GW_OWNER) equals the XAE main window.</param>
/// <param name="ClassName">Win32 class of the top-level window.</param>
/// <param name="Children">(class, text) of every child control.</param>
internal sealed record WindowShape(
    bool OwnedByXae,
    string ClassName,
    IReadOnlyList<(string ClassName, string Text)> Children);

/// <summary>
/// Identifies the TwinCAT "Gehe zu Zeile" / "Go To Line" dialog by STRUCTURE, never by
/// its localized title or label text (German on this box; see the "never hardcode
/// localized names" rule). Proven shape (live, TcXaeShell 4026.22): a WinForms
/// top-level window owned by the XAE main window with exactly one EDIT, a STATIC and
/// two BUTTONs (OK / Abbrechen). A #32770 security/login dialog does not match (wrong
/// class); the app-changed login modal has radios and no single EDIT.
/// </summary>
internal static class GoToDialogMatcher
{
    public static bool IsGoToDialog(WindowShape shape)
    {
        if (shape is null || !shape.OwnedByXae) return false;
        if (!shape.ClassName.Contains("WindowsForms", StringComparison.OrdinalIgnoreCase)) return false;

        int edits = 0, buttons = 0, radiosOrChecks = 0; bool ok = false;
        foreach (var (cls, text) in shape.Children)
        {
            if (cls.Contains("EDIT", StringComparison.OrdinalIgnoreCase)) edits++;
            else if (cls.Contains("BUTTON", StringComparison.OrdinalIgnoreCase))
            {
                buttons++;
                if (IsOkText(text)) ok = true;
            }
        }
        _ = radiosOrChecks;
        return edits == 1 && ok && buttons <= 3;
    }

    /// <summary>"OK" in any of the ways WinForms renders it ("OK", "&amp;OK", "Ok").</summary>
    public static bool IsOkText(string? text) =>
        string.Equals(text?.Trim().TrimStart('&'), "OK", StringComparison.OrdinalIgnoreCase);
}
