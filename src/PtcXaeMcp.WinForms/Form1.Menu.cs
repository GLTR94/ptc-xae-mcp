using PtcXaeMcp.WinForms.Dialogs;
using PtcXaeMcp.WinForms.Mcp;
using PtcXaeMcp.WinForms.Theme;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.WinForms;

// Header menu additions of issue #273: Open sample project, Connect a client…, Settings… and the
// window position that Settings can remember. Built in code (not in the Designer) so the menu
// order lives in one place: WireMenu rebuilds the item list the Designer created.
public partial class Form1
{
    private readonly ToolStripMenuItem _mnuOpenSample = new() { Name = "_mnuOpenSample", Text = "Open sample project" };
    private readonly ToolStripMenuItem _mnuConnectClient = new() { Name = "_mnuConnectClient", Text = "Connect a client…" };
    private readonly ToolStripMenuItem _mnuSettings = new() { Name = "_mnuSettings", Text = "Settings…" };
    private readonly ToolStripMenuItem _mnuWebsite = new() { Name = "_mnuWebsite", Text = "pneutronic.ch" };

    /// <summary>The one GUI-side runner on the shared dispatcher lock (also handed to ApprovalsView).</summary>
    private GuiComDispatchRunner _guiDispatch = null!;

    /// <summary>The server's real diagnostics state, set by AttachMcp (shown in Settings).</summary>
    private bool _diagnosticsEnabled;

    /// <summary>Last non-minimized state, so "maximized, then minimized, then closed" restores maximized.</summary>
    private FormWindowState _lastShownState = FormWindowState.Normal;

    /// <summary>Called from the constructor after <see cref="WireHeader"/>, before the theme is applied.</summary>
    private void WireMenu()
    {
        _menuHeader.Items.Clear();
        _menuHeader.Items.AddRange(new ToolStripItem[]
        {
            _mnuRefreshStatus,
            _mnuOpenSample,
            _mnuConnectClient,
            _mnuSettings,
            new ToolStripSeparator(),
            _mnuForceCloseXae,
            _mnuDismissSecurityDialog,
            new ToolStripSeparator(),
            _mnuOpenLogFolder,
            _mnuToggleTheme,
            new ToolStripSeparator(),
            _mnuWebsite,
        });

        _mnuOpenSample.Click += async (_, _) => await OpenSampleProjectFromGuiAsync();
        _mnuConnectClient.Click += (_, _) =>
        {
            using var dialog = new ConnectClientDialog(McpBridge.Port);
            dialog.ShowDialog(this);
        };
        _mnuSettings.Click += (_, _) => ShowSettingsDialog();
        _mnuWebsite.Click += (_, _) => OpenWebsite();

        // After WinForms' auto-scaling (which runs on load): bounds set in the constructor would
        // be scaled again, and the designer size shrank to ~750 px at 96 DPI.
        Load += (_, _) => RestoreWindowPlacement();
        Resize += (_, _) => { if (WindowState != FormWindowState.Minimized) _lastShownState = WindowState; };
        FormClosing += (_, _) => SaveWindowPlacement();
    }

    private void ShowSettingsDialog()
    {
        var settings = ThemeManager.Settings;
        using var dialog = new SettingsDialog(McpBridge.Port, ThemeManager.Current == ThemeMode.Dark,
            settings.RememberWindowPosition, _diagnosticsEnabled);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        settings.RememberWindowPosition = dialog.RememberWindowPosition;
        var mode = dialog.DarkTheme ? ThemeMode.Dark : ThemeMode.Light;
        if (mode != ThemeManager.Current)
            ThemeManager.Set(mode); // saves ui.json, including RememberWindowPosition
        else
            ThemeManager.Save();
    }

    /// <summary>
    /// Menu > Open sample project: copies the shipped neutral fixture to a fresh folder under
    /// Documents and opens that copy (never the shipped one) through the same lifecycle path as
    /// <c>twincat_open_project</c>, under the dispatcher lock. An XAE is launched if none runs.
    /// </summary>
    private async Task OpenSampleProjectFromGuiAsync()
    {
        string source = SampleProject.ShippedDirectory(AppContext.BaseDirectory);
        if (!Directory.Exists(source))
        {
            MessageBox.Show(this, $"The sample project is not part of this installation:\n{source}",
                "Open sample project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _mnuOpenSample.Enabled = false;
        try
        {
            // 1. An open solution would make the open fail: ask first, before anything is copied.
            var openCheck = await _guiDispatch.RunAsync(() =>
            {
                try { return _session.HasOpenSolution() ? "open" : "none"; }
                catch (Exception) { return "none"; } // no XAE yet: the open below launches one
            });
            if (!openCheck.Ok)
            {
                MessageBox.Show(this, openCheck.ErrorMessage, "Open sample project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool closeCurrent = openCheck.Value == "open";
            if (closeCurrent && MessageBox.Show(this,
                    "A solution is open in the TwinCAT XAE. Close it without saving and open the sample project?",
                    "Open sample project", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            // 2. Copy outside the dispatcher lock (no COM involved); a failed copy leaves nothing behind.
            ShowTransientRunState("Copying sample project...");
            string target = SampleProject.UniqueTargetDirectory(documents, DateTime.Now);
            string sln;
            try
            {
                sln = await Task.Run(() => SampleProject.CopyTo(source, target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
                MessageBox.Show(this, $"The sample project could not be copied to\n{target}\n\n{ex.Message}",
                    "Open sample project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AppendLog($"Sample project copied to {target}");

            // 3. Open the copy under the lock, same path as twincat_open_project.
            ShowTransientRunState("Opening sample project...");
            var outcome = await _guiDispatch.RunAsync(() =>
                new LifecycleCoordinator(_session)
                    .OpenProjectAsync(sln, closeCurrentFirst: closeCurrent, CancellationToken.None)
                    .GetAwaiter().GetResult());

            if (!outcome.Ok)
            {
                ShowTransientRunState("Sample project not opened.");
                MessageBox.Show(this, outcome.ErrorMessage, "Open sample project",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string result = outcome.Value ?? string.Empty;
            AppendLog(result);
            bool ok = McpErrors.IsOk(result);
            _host.RequestEnvironmentRefresh("After sample project");
            ShowTransientRunState(ok ? "Sample project opened." : "Sample project not opened.");
            if (!ok)
                MessageBox.Show(this, result, "Open sample project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _mnuOpenSample.Enabled = true;
        }
    }

    /// <summary>Applies the position saved in ui.json, clamped to the screens connected now.</summary>
    private void RestoreWindowPlacement()
    {
        var settings = ThemeManager.Settings;
        if (!settings.RememberWindowPosition || settings.Window is not { } w)
        {
            ApplyDefaultWindowSize();
            return;
        }

        var areas = Screen.AllScreens
            .OrderByDescending(s => s.Primary)
            .Select(s => s.WorkingArea)
            .ToList();
        if (UiSettings.ClampToScreens(new Rectangle(w.X, w.Y, w.Width, w.Height), areas, MinimumSize) is not { } bounds)
            return;

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (w.Maximized) WindowState = FormWindowState.Maximized;
    }

    /// <summary>First start (or nothing remembered): a comfortable size, centred on the primary screen.</summary>
    private void ApplyDefaultWindowSize()
    {
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        var size = new Size(Math.Min(1200, area.Width * 9 / 10), Math.Min(860, area.Height * 9 / 10));
        Bounds = new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2,
            size.Width, size.Height);
    }

    private void SaveWindowPlacement()
    {
        var settings = ThemeManager.Settings;
        if (!settings.RememberWindowPosition) return;

        // The restore bounds are the normal bounds while minimized or maximized.
        Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        bool maximized = WindowState == FormWindowState.Maximized
            || (WindowState == FormWindowState.Minimized && _lastShownState == FormWindowState.Maximized);
        settings.Window = new WindowPlacement(b.X, b.Y, b.Width, b.Height, maximized);
        ThemeManager.Save();
    }
}
