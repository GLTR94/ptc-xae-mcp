using System.Globalization;
namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // ---- PLC Online Status (feature/plc-online-status) ----------------------
    //
    // "Online" in TwinCAT XAE means the IDE has an active login to the running
    // PLC runtime (green "Login" toolbar button). While Online, code edits are
    // blocked.
    //
    // The TwinCAT Automation Interface (published docs) does not expose a direct
    // "IsOnline" property on ITcSysManager, ITcSmTreeItem, or ITcPlcProject.
    // This implementation probes candidate sources in order, falling back to
    // IsOnline=null when none is found. The probe source is embedded in Message
    // so we can diagnose which strategy worked in practice.
    //
    // Strategy 0 — PLC Logout DTE command IsAvailable (PRIMARY, see #127)
    //   The PLC Logout toolbar action is a DTE command whose IsAvailable flag is
    //   true ⟺ Online. The only signal that stays correct after activate_config.
    //   See FindPlcLogoutCommand in TwinCatComSession.Logout.cs.
    //
    // Strategy 1 — DTE.Debugger.CurrentMode
    //   TwinCAT "Login" engages the VS Shell debugger host (dbgDebugModeDesign=1
    //   when offline, dbgDebugModeRun=3 or Break=2 when online).
    //   Risk: TcXaeShell may not set this mode for PLC login; verify live.
    //
    // Strategy 2 — ITcSysManager.IsOnline via IDispatch
    //   Tries a dynamic property call on the system manager COM object.
    //   Not in published docs but may exist as a late-bound COM property.
    //
    // Strategy 3 — PLC project node .IsOnline via IDispatch
    //   Tries the same property on the first PLC source-project tree node.
    //   Also undocumented; worth a shot.
    //
    // Fallback — IsOnline=null + descriptive message
    //   Tells callers that the state is indeterminate; they should warn rather
    //   than assume Offline.

    public PlcOnlineStateResult GetPlcOnlineState() => ComBusyRetry.Run(() =>
    {
        dynamic dte = GetActiveTcDte();

        // ── Strategy 0 (most reliable on Tc3 4026.x): PLC Logout command ───────
        // TwinCAT exposes the PLC Logout toolbar action as a DTE command (dynamic
        // name, e.g. "SPS.Logoutfromnone"). Its IsAvailable flag is true ⟺ the IDE
        // is logged into the PLC (Online). This is the ONLY signal that stays
        // correct after activate_config, where DTE.Debugger.CurrentMode wrongly
        // reports Design/offline (issue #127). Verified live both directions.
        PlcOnlineCommand? logoutCmd = FindPlcLogoutCommand(dte);
        if (logoutCmd is { Available: not null } lc)
        {
            // Login availability only refines the Offline message (no reachable target / not
            // built); it never decides the state (issue #148, LIVE-4).
            PlcOnlineCommand? loginCmd = FindPlcLoginCommand(dte);
            string offlineDetail = loginCmd is { Available: false }
                ? " Login is disabled too — no reachable target (or project not built)."
                : string.Empty;
            return new PlcOnlineStateResult(lc.Available,
                lc.Available == true
                    ? $"XAE is Online (PLC Logout command '{lc.Name}' is available)."
                    : $"XAE is Offline (PLC Logout command '{lc.Name}' is disabled).{offlineDetail}");
        }

        // ── Strategy 1: DTE.Debugger.CurrentMode ──────────────────────────────
        // mode != 1 (Run/Break) → definitively Online.
        // mode == 1 (Design) is NOT definitively Offline: after activate_config,
        // TwinCAT auto-logs into the PLC without engaging the VS Shell debugger,
        // so Design mode coexists with an active PLC login (issue #127). Fall
        // through to let Strategies 2 and 3 check TwinCAT's own COM state.
        try
        {
            int mode = (int)dte.Debugger.CurrentMode;
            if (mode != 1)
            {
                string modeLabel = mode switch { 2 => "Break", 3 => "Run", _ => mode.ToString(CultureInfo.InvariantCulture) };
                return new PlcOnlineStateResult(true,
                    $"XAE is Online (DTE debugger mode: {modeLabel}).");
            }
            // mode == 1 (Design): ambiguous — continue to TwinCAT-specific strategies
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* property unavailable on this DTE version — fall through */ }

        // ── Strategy 2: ITcSysManager.IsOnline ────────────────────────────────
        object sysObj = RequireSystemManager(dte);
        dynamic sysMan = sysObj;
        try
        {
            bool online = (bool)sysMan.IsOnline;
            return new PlcOnlineStateResult(online,
                online ? "XAE is Online (ITcSysManager.IsOnline=true)."
                       : "XAE is Offline (ITcSysManager.IsOnline=false).");
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* property not available — fall through */ }

        // ── Strategy 3: PLC project node IsOnline ─────────────────────────────
        dynamic? plcNode = FindPlcSourceProjectNode(dte);
        if (plcNode is not null)
        {
            try
            {
                bool online = (bool)plcNode.IsOnline;
                return new PlcOnlineStateResult(online,
                    online ? "XAE is Online (PLC project node IsOnline=true)."
                           : "XAE is Offline (PLC project node IsOnline=false).");
            }
            catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* property not available — fall through */ }
        }

        // ── Fallback ──────────────────────────────────────────────────────────
        // DTE.Debugger.CurrentMode was 1 (Design) — ambiguous after activate_config.
        // ITcSysManager.IsOnline and PLC node IsOnline both threw (undocumented).
        // State is indeterminate; the PLC may be online (post-activate) or offline.
        return new PlcOnlineStateResult(null,
            "XAE online state is indeterminate: DTE is in Design mode (ambiguous after " +
            "activate_config), and ITcSysManager.IsOnline / PLC node IsOnline are not " +
            "available on this TwinCAT version. Manually verify in XAE or call " +
            "twincat_logout to ensure offline before building.");
    }, OpenTimeout);
}
