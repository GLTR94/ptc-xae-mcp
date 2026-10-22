namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Classifies "no DTE attached" (issue #149, scope 4) from the two COM-free probes: the
/// TcXaeShell process count (null = probe failed) and the blocking XAE window, if any.
/// Pure; unit-tested. The texts keep "No TwinCAT XAE instance is running" for the
/// no-process case — clients and tests key on it.
/// </summary>
internal static class XaeReachability
{
    public static string Describe(int? processCount, string? blockingDialog)
    {
        if (blockingDialog is not null)
            return ComCall.BlockedByDialogMessage(blockingDialog, null, null);
        return processCount switch
        {
            0 => "No TwinCAT XAE instance is running.",
            null => "No TwinCAT XAE DTE is reachable via COM, and whether a TcXaeShell process " +
                    "is running could not be determined (process enumeration failed).",
            _ => $"TcXaeShell is running ({processCount} process(es)) but its DTE is not reachable " +
                 "via COM — still starting, not yet registered in the Running Object Table, or hung. " +
                 "Retry in a few seconds; if it persists, check the XAE window.",
        };
    }
}
