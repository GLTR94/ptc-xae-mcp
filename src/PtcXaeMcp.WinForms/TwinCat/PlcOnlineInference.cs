namespace PtcXaeMcp.WinForms.TwinCat;

// Issue #148 (LIVE-4): the online state is read from the PLC Login/Logout DTE commands'
// IsAvailable flags. The only sound inference is from the LOGOUT command:
//   Logout available  ⇒ logged in (Online)
//   Logout disabled   ⇒ not logged in (Offline) — you cannot log out of a session you do not have
//   Logout unreadable ⇒ unknown
// "Login disabled ⇒ already online" is WRONG: with no reachable target both commands are
// disabled (reproduced 4× live: login returned success/isOnlineNow=true while offline).
internal enum LoginPrecheck
{
    /// <summary>Login is available and Logout is not: execute the Login command.</summary>
    Proceed,
    /// <summary>Logout is available: a session already exists — idempotent no-op.</summary>
    AlreadyOnline,
    /// <summary>Both commands disabled: offline and no login possible (no reachable target / not built).</summary>
    NoTarget,
    /// <summary>A flag could not be read: the state is unknown — never report success.</summary>
    Unknown,
}

internal static class PlcOnlineInference
{
    public static bool? IsOnline(bool? logoutAvailable) => logoutAvailable;

    public static LoginPrecheck BeforeLogin(bool? loginAvailable, bool? logoutAvailable)
    {
        if (logoutAvailable == true) return LoginPrecheck.AlreadyOnline;
        if (logoutAvailable is null || loginAvailable is null) return LoginPrecheck.Unknown;
        return loginAvailable.Value ? LoginPrecheck.Proceed : LoginPrecheck.NoTarget;
    }
}
