namespace PtcXaeMcp.WinForms.TwinCat;

// ---- plc-run-control --------------------------------------------------------
// Result of a PLC start/stop. StateBefore/StateAfter are the ADS states actually
// read back from the runtime — the tool reports what the PLC IS, not what it was
// told to do, so a command that is accepted but does not take effect cannot
// masquerade as success.

internal sealed record PlcRunControlResult(
    bool Success,
    string? StateBefore,
    string? StateAfter,
    string AmsNetId,
    int AdsPort,
    string Message);

// ---- boot-project -----------------------------------------------------------
// Result of activating (writing) the boot project. Because the Automation
// Interface member that performs this is not something we can verify from the
// published docs alone, the tool probes a candidate list and reports exactly
// which member it resolved and whether it invoked it, rather than claiming a
// generic success.

internal sealed record BootProjectResult(
    bool Success,
    bool DryRun,
    string? MemberUsed,          // the COM member that dispatched, if any
    IReadOnlyList<string> Probed, // "<name>: available|absent|failed: <why>"
    string Message);
