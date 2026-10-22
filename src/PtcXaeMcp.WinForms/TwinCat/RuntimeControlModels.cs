namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed record BroadcastTarget(
    string Name,
    string NetId,
    string IpAddr,
    string Version,
    string Os);
