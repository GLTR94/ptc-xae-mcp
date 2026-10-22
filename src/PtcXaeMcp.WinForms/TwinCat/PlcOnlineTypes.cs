namespace PtcXaeMcp.WinForms.TwinCat;

// ---- plc-online-status -------------------------------------------------------
// Result shape for GetPlcOnlineState().
// IsOnline=true  → XAE is logged in (Online); code edits are blocked.
// IsOnline=false → XAE is Offline; code edits are allowed.
// IsOnline=null  → state could not be determined via COM (no matching API found).

internal sealed record PlcOnlineStateResult(bool? IsOnline, string Message);
