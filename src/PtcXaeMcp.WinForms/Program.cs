using PtcXaeMcp.WinForms.Diagnostics;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.WinForms;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Register global exception handlers before anything else can throw
        // (issue #147 item 2) — a crash during startup must still land in the
        // host log instead of vanishing silently.
        RegisterGlobalExceptionHandlers();
        HostLog.Shared.Info("=== ptc-xae-mcp starting ===");

        // Runtime fallback for the Beckhoff DLLs this project does not
        // redistribute by default (#156 item 4) — no-op unless the process
        // actually tries to load Beckhoff.TwinCAT.TcEventLoggerAdsProxy.*.
        BeckhoffAssemblyResolver.Register();

        // A second instance started headlessly (--http-server, or stdin piped by
        // Claude Code) must never block on a MessageBox nobody will see — log and
        // exit fast instead (issue #147 item 8). An interactive double-click start
        // keeps the informational dialog.
        bool launchedHeadless = Console.IsInputRedirected
            || Environment.GetCommandLineArgs().Contains("--http-server");

        // Diagnostics/research/test-support MCP tools (ptc_test_marker, twincat_show_step,
        // twincat_safety_recon, twincat_safety_probe_create_alias, twincat_dump_nc_tree, the
        // deprecated twincat_open_plc_object/twincat_close_plc_object) are opt-in only — a
        // released MCP client never sees them (issue #157 item 1). The live integration suite
        // host sets one of these to reach twincat_show_step etc.
        bool diagnosticsEnabled = Environment.GetCommandLineArgs().Contains("--diagnostics")
            || Environment.GetEnvironmentVariable(McpBridge.DiagnosticsEnvVar) == "1";

        // Prevent a second instance from starting. A named Mutex scoped to the
        // current Windows session is sufficient for a WinForms desktop app.
        using var mutex = new Mutex(initiallyOwned: true, "Local\\ptc-xae-mcp", out bool first);
        if (!first)
        {
            if (launchedHeadless)
            {
                HostLog.Shared.Warn(
                    "Second instance detected (--http-server or redirected stdin) while another " +
                    "ptc-xae-mcp instance already holds the mutex. Exiting without starting (exit code 2).");
                Environment.Exit(2);
                return;
            }

            MessageBox.Show(
                "ptc-xae-mcp is already running.\nOnly one instance is allowed.",
                "ptc-xae-mcp", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Register the Windows system SQLite provider (winsqlite3.dll) before any
        // SqliteConnection is opened. Required when using Microsoft.Data.Sqlite.Core
        // without an implicit bundle that auto-registers a provider.
        SQLitePCL.Batteries_V2.Init();

        ApplicationConfiguration.Initialize();

        // The host owns the TwinCAT session and everything the MCP tools share with the GUI
        // (issue #268). Disposed after Application.Run: IBN abort, UMR, session, dispatcher.
        using var host = new ServerHost();
        var form = new Form1(host);

        // The HTTP MCP server always starts — also on a plain double-click (issue #268):
        // clients connect by URL (http://localhost:5010/), so a window without a server
        // was a dead end. --http-server is still accepted for existing launchers.
        var bridge = new McpBridge(host, diagnosticsEnabled);
        form.AttachMcp(bridge);
        form.FormClosed += (_, _) =>
        {
            HostLog.Shared.Info("Form closed (GUI) — stopping MCP server.");
            bridge.Stop();
        };
        bridge.Start();

        // COM calls issued directly on this STA thread (Form1 button handlers) hit
        // RPC_E_CALL_REJECTED/RETRYLATER the same way the MCP/MTA path does, but
        // CoRegisterMessageFilter — not ComBusyRetry — is the mechanism that covers
        // an STA thread. ComMessageFilter existed but was never registered anywhere,
        // so the GUI path had no busy-retry at all. Register it for the process
        // lifetime; Dispose after Application.Run restores the previous filter.
        using var comFilterScope = ComMessageFilter.Register();

        Application.Run(form);
    }

    /// <summary>
    /// Wires the three global exception handlers (issue #147 item 2). Each one
    /// logs the full exception + originating thread name to <see cref="HostLog.Shared"/>
    /// and keeps the host running — none of these re-throw or terminate the process.
    /// </summary>
    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception
                ?? new Exception($"Non-Exception unhandled object: {e.ExceptionObject}");
            HostLog.Shared.WriteException("AppDomain.CurrentDomain.UnhandledException", ex, Thread.CurrentThread.Name);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            HostLog.Shared.WriteException("TaskScheduler.UnobservedTaskException", e.Exception, Thread.CurrentThread.Name);
            e.SetObserved();
        };

        // Application.ThreadException only fires when UnhandledExceptionMode is
        // CatchException — set it explicitly so a WinForms UI-thread exception is
        // logged and the message loop keeps running instead of the default
        // (mode-dependent) termination.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            HostLog.Shared.WriteException("Application.ThreadException", e.Exception, Thread.CurrentThread.Name);
    }
}
