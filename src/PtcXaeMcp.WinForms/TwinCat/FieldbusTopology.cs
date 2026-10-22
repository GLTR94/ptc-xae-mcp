namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed record FieldbusNode(
    string Path,
    string Name,
    int ItemType,
    int ItemSubType,
    string Kind,
    string? Port,
    IReadOnlyList<FieldbusNode> Children,
    string? PhysicalPort = null);

internal sealed record FieldbusTopology(
    int DeviceCount,
    IReadOnlyList<FieldbusNode> Devices);

internal static class FieldbusKind
{
    public const string Device  = "device";
    public const string Box     = "box";
    public const string Slave   = "slave";
    public const string Channel = "channel";
    public const string Other   = "other";

    public static string Classify(string name, int itemType, int itemSubType,
                                  int depth, int childCount, bool hasProduceXml)
    {
        if (depth <= 1) return Device;

        if (childCount == 0) return Channel;

        if (LooksLikeSegmentMaster(name)) return Box;

        return hasProduceXml ? Slave : Other;
    }

    // Used by BuildFieldbusNode to count downstream ports before full classification.
    internal static bool LooksLikeSegmentMaster(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.StartsWith("Box ", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("(EK", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("(EL", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("(ED", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("(EP", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Only EK couplers/junctions occupy a downstream port on the upstream device.
    // EL/ED terminals and EP box modules ride the ring and do not consume a port letter.
    internal static bool LooksLikeEtherCatCoupler(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.Contains("(EK", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Removes duplicate nodes (by PathName) that arise from EK1122 junction processing
/// where a sub-coupler's children are appended to both the junction and its parent.
/// </summary>
internal static class FieldbusTopologyDedup
{
    internal static FieldbusTopology Deduplicate(FieldbusTopology topology)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var devices = topology.Devices
            .Select(d => DeduplicateNode(d, seen))
            .OfType<FieldbusNode>()
            .ToList();
        return new FieldbusTopology(devices.Count, devices);
    }

    private static FieldbusNode? DeduplicateNode(FieldbusNode node, HashSet<string> seen)
    {
        if (!seen.Add(node.Path)) return null;

        var children = node.Children
            .Select(c => DeduplicateNode(c, seen))
            .OfType<FieldbusNode>()
            .ToList();

        return node with { Children = children };
    }
}
