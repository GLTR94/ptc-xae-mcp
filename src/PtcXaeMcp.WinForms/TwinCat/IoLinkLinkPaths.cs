namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Builds candidate TIID leaf paths for a <b>named IO-Link process-data point</b>
/// under a master terminal, so the schema-apply linker can link a GVL variable to
/// e.g. a valve solenoid bit. Real shape (EL6224, project 100001):
/// <c>{terminalPath}^IO-Link Port1^Valve 1 solenoid 00</c> — the port group node
/// carries the PDO name ("IO-Link Port1") and each entry carries its IODD label.
/// Pure and order-stable; the caller probes candidates against the live tree.
/// </summary>
internal static class IoLinkLinkPaths
{
    public static IEnumerable<string> Candidates(string terminalPath, int? port, string? channelName)
    {
        if (string.IsNullOrWhiteSpace(terminalPath) || string.IsNullOrWhiteSpace(channelName))
            yield break;

        int p = port is > 0 ? port.Value : 1;
        string ch = channelName.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Port group node name as TwinCAT writes it, plus tolerant spacing variants.
        foreach (string portNode in new[] { $"IO-Link Port{p}", $"IO-Link Port {p}", $"IO Link Port{p}" })
        {
            string cand = $"{terminalPath}^{portNode}^{ch}";
            if (seen.Add(cand)) yield return cand;
        }

        // Some layouts surface the named entry directly under the terminal.
        string flat = $"{terminalPath}^{ch}";
        if (seen.Add(flat)) yield return flat;
    }
}
