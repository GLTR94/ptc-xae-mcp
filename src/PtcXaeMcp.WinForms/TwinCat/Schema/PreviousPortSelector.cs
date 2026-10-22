namespace PtcXaeMcp.WinForms.TwinCat.Schema;

/// <summary>
/// Pure candidate-selection logic extracted from NativeTwinCatRunner.SetPreviousPort.
/// On a junction (EK1122 etc.) several upstream devices can expose the same
/// &lt;PreviousPort&gt; letter, distinguished only by their PhysAddr. This picks the
/// candidate that connects to the intended upstream box, falling back to the first
/// letter match (legacy behaviour) when no PhysAddr match exists. COM-free: the caller
/// is responsible for reading the XML and applying the chosen index back to it.
/// </summary>
internal static class PreviousPortSelector
{
    internal readonly record struct PortCandidate(string Port, string PhysAddr);

    /// <param name="ChosenIndex">Index into the candidate list to mark Selected, or null when
    /// the desired port letter has no candidate at all.</param>
    /// <param name="Warning">Non-fatal note to log (disambiguation fallback), or null.</param>
    internal readonly record struct Selection(int? ChosenIndex, string? Warning);

    internal static Selection Select(IReadOnlyList<PortCandidate> candidates, string desiredPort, string upstreamPhysAddr)
    {
        int? chosenIndex = null;
        int? firstLetterMatchIndex = null;
        var availablePorts = new List<string>();

        for (int i = 0; i < candidates.Count; i++)
        {
            string port = candidates[i].Port;
            if (!string.IsNullOrWhiteSpace(port))
            {
                availablePorts.Add(port);
            }

            if (!string.Equals(port, desiredPort, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            firstLetterMatchIndex ??= i;

            string physAddr = candidates[i].PhysAddr;
            if (chosenIndex is null &&
                !string.IsNullOrWhiteSpace(upstreamPhysAddr) &&
                string.Equals(physAddr, upstreamPhysAddr, StringComparison.OrdinalIgnoreCase))
            {
                chosenIndex = i;
            }
        }

        chosenIndex ??= firstLetterMatchIndex;
        if (chosenIndex is null)
        {
            string available = availablePorts.Count == 0 ? "<keine>" : string.Join(",", availablePorts);
            return new Selection(null, $"Gewuenschter Port '{desiredPort}' ist nicht verfuegbar. Verfuegbar: {available}");
        }

        if (chosenIndex == firstLetterMatchIndex && !string.IsNullOrWhiteSpace(upstreamPhysAddr))
        {
            string chosenAddr = candidates[chosenIndex.Value].PhysAddr;
            if (!string.Equals(chosenAddr, upstreamPhysAddr, StringComparison.OrdinalIgnoreCase))
            {
                return new Selection(chosenIndex,
                    $"kein Kandidat mit Upstream-PhysAddr '{upstreamPhysAddr}' gefunden; verwende ersten Treffer (PhysAddr '{chosenAddr}').");
            }
        }

        return new Selection(chosenIndex, null);
    }
}
