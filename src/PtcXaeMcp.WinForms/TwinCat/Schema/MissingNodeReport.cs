namespace PtcXaeMcp.WinForms.TwinCat.Schema;

/// <summary>
/// One EtherCAT schema node that failed to apply, or was skipped as a consequence of an
/// ancestor failing. Moved out of NativeTwinCatRunner (was a private nested class) so the
/// grouping logic below, and the future ApplyReport, can reference it without depending on
/// the runner's COM-bound internals.
/// </summary>
internal sealed class MissingEthercatNodeInfo
{
    public string Name { get; init; } = string.Empty;
    public string VInfo { get; init; } = string.Empty;
    public string ParentName { get; init; } = string.Empty;
    public string ParentPath { get; init; } = string.Empty;
    public string SchemaPath { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool IsDirectFailure { get; init; }
}

/// <summary>
/// Pure grouping of "skipped as a consequence" nodes under the direct failure that caused
/// them, extracted from NativeTwinCatRunner.LogMissingEthercatSummary (BuildSkippedNodeGroups /
/// FindOwningDirectFailure). No COM, no logging — just node lists in, grouped report out.
/// </summary>
internal static class MissingNodeReport
{
    internal static IEnumerable<(MissingEthercatNodeInfo? Cause, List<MissingEthercatNodeInfo> Nodes)> GroupSkipped(
        IReadOnlyList<MissingEthercatNodeInfo> directFailures,
        IReadOnlyList<MissingEthercatNodeInfo> skippedNodes)
    {
        var groups = new List<(MissingEthercatNodeInfo? Cause, List<MissingEthercatNodeInfo> Nodes)>();

        foreach (MissingEthercatNodeInfo skipped in skippedNodes)
        {
            MissingEthercatNodeInfo? cause = FindOwningDirectFailure(directFailures, skipped);
            int existingIndex = groups.FindIndex(group =>
                string.Equals(group.Cause?.SchemaPath, cause?.SchemaPath, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                groups[existingIndex].Nodes.Add(skipped);
            }
            else
            {
                groups.Add((cause, [skipped]));
            }
        }

        return groups
            .OrderByDescending(group => group.Nodes.Count)
            .ThenBy(group => group.Cause?.SchemaPath, StringComparer.OrdinalIgnoreCase);
    }

    private static MissingEthercatNodeInfo? FindOwningDirectFailure(
        IReadOnlyList<MissingEthercatNodeInfo> directFailures,
        MissingEthercatNodeInfo skippedNode)
    {
        MissingEthercatNodeInfo? best = null;
        int bestLength = -1;

        foreach (MissingEthercatNodeInfo directFailure in directFailures)
        {
            string schemaPath = directFailure.SchemaPath;
            if (string.IsNullOrWhiteSpace(schemaPath))
            {
                continue;
            }

            bool isMatch =
                string.Equals(skippedNode.SchemaPath, schemaPath, StringComparison.OrdinalIgnoreCase) ||
                skippedNode.SchemaPath.StartsWith(schemaPath + "^", StringComparison.OrdinalIgnoreCase);
            if (!isMatch)
            {
                continue;
            }

            if (schemaPath.Length > bestLength)
            {
                best = directFailure;
                bestLength = schemaPath.Length;
            }
        }

        return best;
    }
}
