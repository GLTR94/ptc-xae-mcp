namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // ---- Logout / Clean / Rebuild (feature/build-recovery, issue #127) --------
    //
    // At 100000 the XAE was still Online (logged in) when rebuild was attempted,
    // which caused the build to fail with no readable errors. The correct recovery
    // sequence is: logout → clean → rebuild → read errors[].
    //
    // Logout / online-state mechanism (verified live on Tc3 4026.x):
    //   TwinCAT exposes the PLC Login/Logout toolbar actions as DTE commands whose
    //   names carry a dynamic, locale-dependent suffix — e.g. "SPS.Logoutfromnone"
    //   on a German XAE. The Logout command's IsAvailable flag is the RELIABLE
    //   online signal: available ⟺ the IDE is logged into the PLC (Online).
    //
    //   This matters because DTE.Debugger.CurrentMode stays at Design (1) even when
    //   the PLC is online after activate_config — TwinCAT's PLC login does not engage
    //   the VS Shell debugger, so the debugger mode wrongly reads "offline" (#127).
    //   ITcSysManager.IsOnline / plcNode.IsOnline are not exposed on this version
    //   either. ConsumeXml("<LogoutCmd/>") on the PLC node is rejected. Only the
    //   PLC Logout DTE command both reflects (IsAvailable) and performs the logout.
    //
    //   Commands are matched by the "Logoutfrom" / "Loginto" substring so the
    //   locale prefix (SPS./PLC.) and target suffix are irrelevant.
    //
    // Infosys ref (logout → offline action):
    //   https://infosys.beckhoff.com/content/1033/tc3_userinterface/2531428235.html

    // Available=null: IsAvailable could not be read (non-busy COM failure) — "unknown", never
    // "disabled" (issue #148, B-1: an unreadable flag used to become "offline").
    internal readonly record struct PlcOnlineCommand(string Name, bool? Available);

    /// <summary>
    /// Locate the TwinCAT PLC Logout DTE command (dynamic name, e.g.
    /// "SPS.Logoutfromnone"). Its IsAvailable flag is true ⟺ the PLC is Online.
    /// Returns null when no such command exists (non-TwinCAT shell / no PLC project).
    /// </summary>
    private static PlcOnlineCommand? FindPlcLogoutCommand(dynamic dte) =>
        FindPlcCommandContaining(dte, "Logoutfrom");

    private static PlcOnlineCommand? FindPlcCommandContaining(dynamic dte, string needle)
    {
        try
        {
            foreach (dynamic cmd in dte.Commands)
            {
                string name;
                try { name = (string)cmd.Name ?? string.Empty; }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool? avail;
                try { avail = (bool)cmd.IsAvailable; }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) { avail = null; }
                return new PlcOnlineCommand(name, avail);
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* Commands enumeration unavailable — caller falls back */ }
        return null;
    }

    public LogoutPlcResult LogoutPlc() => ComBusyRetry.Run(() =>
    {
        dynamic dte = GetActiveTcDte();

        // ── Primary: the PLC Logout DTE command ───────────────────────────────
        // Its availability is the authoritative online state; executing it logs out.
        PlcOnlineCommand? logoutCmd = FindPlcLogoutCommand(dte);
        if (logoutCmd is { } lc)
        {
            if (lc.Available is null)
                return new LogoutPlcResult(false, null,
                    $"Online state unknown: IsAvailable of the PLC Logout command '{lc.Name}' could not be read.");
            if (lc.Available == false)
                return new LogoutPlcResult(false, false,
                    $"PLC already offline (Logout command '{lc.Name}' is disabled) — no-op.");

            try
            {
                dte.ExecuteCommand(lc.Name);
                Thread.Sleep(800);   // let XAE switch state before re-reading

                // Verify: the Logout command should now be disabled (offline).
                // after.Available IS the online state — available ⟺ still online.
                PlcOnlineCommand? after = FindPlcLogoutCommand(dte);
                bool? nowOnline = after?.Available;
                return new LogoutPlcResult(true, nowOnline,
                    nowOnline switch
                    {
                        false => $"PLC logged out via '{lc.Name}' — now offline.",
                        true  => $"Logout command '{lc.Name}' executed but PLC still appears online.",
                        null  => $"Logout command '{lc.Name}' executed but the online state could not be read back.",
                    });
            }
            catch (Exception ex) when (ComCall.NotBusy(ex)) {
                // Command exists but execution failed — fall through to legacy paths.
                _ = ex;
            }
        }

        // ── Legacy fallback (older TC versions where the command isn't found) ──
        // ConsumeXml("<LogoutCmd/>") on the PLC source-project node, then
        // DTE.Debugger.Stop(). Guarded by best-effort online detection.
        dynamic? plcNode = FindPlcSourceProjectNode(dte);

        bool? wasOnline = null;
        try
        {
            int mode = (int)dte.Debugger.CurrentMode;
            if (mode != 1) wasOnline = true;   // Run/Break ⟹ definitely online
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        if (wasOnline is null && plcNode is not null)
        {
            try { wasOnline = (bool)plcNode.IsOnline; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        }
        if (wasOnline == false)
            return new LogoutPlcResult(false, false, "PLC was already offline — no-op.");

        string logoutMethod = "none";
        if (plcNode is not null)
        {
            try { plcNode.ConsumeXml("<LogoutCmd/>"); logoutMethod = "ConsumeXml(<LogoutCmd/>)"; }
            catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        }
        if (logoutMethod == "none")
        {
            try { dte.Debugger.Stop(); logoutMethod = "Debugger.Stop()"; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        }

        Thread.Sleep(500);

        bool? isOnlineNow = null;
        try
        {
            int mode = (int)dte.Debugger.CurrentMode;
            if (mode != 1) isOnlineNow = true;
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        if (isOnlineNow is null && plcNode is not null)
        {
            try { isOnlineNow = (bool)plcNode.IsOnline; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        }

        string msg = logoutMethod != "none"
            ? (isOnlineNow != true
                ? $"PLC logged out via {logoutMethod} — now offline."
                : $"Logout attempted via {logoutMethod} but PLC is still online.")
            : (wasOnline is null
                ? "No active PLC login found — likely already offline."
                : "Could not log out PLC — all methods failed; PLC may still be online.");

        return new LogoutPlcResult(
            WasOnline: wasOnline ?? (logoutMethod != "none"),
            IsOnlineNow: isOnlineNow,
            Message: msg);
    }, OpenTimeout);

    // CleanSolution and Rebuild* are implemented in TwinCatComSession.Build.cs
    // (partial class — they live there to co-locate with SolutionBuild usage).
}
