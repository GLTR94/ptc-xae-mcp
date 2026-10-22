using System.Runtime.InteropServices;

namespace PtcXaeMcp.WinForms.Theme;

// Applies OS-level dark rendering to a WinForms window:
//   - DWMWA_USE_IMMERSIVE_DARK_MODE  → dark title bar + DWM chrome
//   - SetWindowTheme("DarkMode_Explorer") → dark OS scrollbars on TextBox,
//     TreeView, ListBox, RichTextBox, DataGridView (and its scroll bars), etc.,
//     dark check boxes (FlatStyle.Standard) and dark NumericUpDown spin buttons
//   - SetWindowTheme("DarkMode_CFD") → dark drop-down-list ComboBox (FlatStyle.Standard)
//
// Controls that get their handle later (a tab never shown yet) or are added later (the
// console's generated form) are covered too: ApplyToForm hooks HandleCreated and ControlAdded
// once per control and themes them with the current mode when they appear.
// Safe to call on every theme toggle — Windows ignores redundant calls.
internal static class NativeDarkMode
{
    private const int DWMWA_IMMERSIVE_DARK_MODE     = 20;  // Windows 10 20H1+
    private const int DWMWA_IMMERSIVE_DARK_MODE_OLD = 19;  // pre-20H1

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(
        IntPtr hwnd, string pszSubAppName, string? pszSubIdList);

    // Controls already hooked for HandleCreated / ControlAdded (UI thread only).
    private static readonly HashSet<Control> Hooked = [];

    private static bool IsDark => ThemeManager.Current == ThemeMode.Dark;

    public static void ApplyToForm(Form form, bool dark)
    {
        int value = dark ? 1 : 0;
        // Both HRESULTs are deliberately ignored (CA1806): the "old" attribute id is a
        // pre-20H1 fallback, so on current Windows it always fails with E_INVALIDARG —
        // failure here was always expected, not an error condition to react to.
        _ = DwmSetWindowAttribute(form.Handle, DWMWA_IMMERSIVE_DARK_MODE,     ref value, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, DWMWA_IMMERSIVE_DARK_MODE_OLD, ref value, sizeof(int));
        ApplyNative(form, dark);
    }

    /// <summary>For a dialog not shown yet: applies the current mode as soon as its handle exists
    /// (the title bar must be dark before the window first appears).</summary>
    public static void ApplyWhenCreated(Form form)
    {
        if (form.IsHandleCreated)
            ApplyToForm(form, IsDark);
        else
            form.HandleCreated += (_, _) => ApplyToForm(form, IsDark);
    }

    private static void ApplyNative(Control root, bool dark)
    {
        Hook(root);
        // HRESULT ignored (CA1806): best-effort visual theming, never worth surfacing a
        // failure for (uxtheme.dll is not guaranteed present/enabled on every SKU).
        if (ThemeName(root, dark) is { } theme && root.IsHandleCreated)
            _ = SetWindowTheme(root.Handle, theme, null);

        foreach (Control child in root.Controls)
            ApplyNative(child, dark);
    }

    private static void Hook(Control c)
    {
        if (!Hooked.Add(c)) return;
        c.Disposed += (_, _) => Hooked.Remove(c);
        // A control created later (tab not shown yet, recreated handle) gets the current theme.
        if (ThemeName(c, IsDark) is not null)
            c.HandleCreated += (_, _) =>
            {
                if (ThemeName(c, IsDark) is { } theme) _ = SetWindowTheme(c.Handle, theme, null);
            };
        // A control added later (generated console form) is themed with its subtree.
        c.ControlAdded += (_, e) => { if (e.Control is { } added) ApplyNative(added, IsDark); };
    }

    /// <summary>The window theme for a control with OS-drawn parts; <c>null</c> for the rest.</summary>
    private static string? ThemeName(Control c, bool dark) => c switch
    {
        // DropDownList (FlatStyle.Standard): box, arrow button and list scroll bar.
        ComboBox => dark ? "DarkMode_CFD" : "Explorer",
        TextBoxBase or TreeView or ListBox or DataGridView or ListView or ScrollBar or CheckBox
            => dark ? "DarkMode_Explorer" : "Explorer",
        _ when c.Parent is UpDownBase => dark ? "DarkMode_Explorer" : "Explorer", // spin buttons
        // AutoScroll scroll bars of panels (the console's generated form) and user controls.
        ScrollableControl => dark ? "DarkMode_Explorer" : "Explorer",
        _ => null,
    };
}
