using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed class FieldbusCoordinator(ITwinCatSession session)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string EmptyJson = "{\"deviceCount\":0,\"devices\":[]}";

    public (string Summary, string Json) GetTopology() =>
        GetTopologyFull().Item1;

    public ((string Summary, string Json), FieldbusTopology Topology) GetTopologyFull()
    {
        string? error = SessionGuards.RequireOpenSolution(session);
        var empty = new FieldbusTopology(0, Array.Empty<FieldbusNode>());
        if (error is not null) return ((error, EmptyJson), empty);

        FieldbusTopology topology;
        try { topology = session.GetFieldbusTopology(); }
        catch (InvalidOperationException ex) { return (($"[Error] {ex.Message}", EmptyJson), empty); }

        if (topology.DeviceCount == 0)
            return (("No fieldbus devices found under TIID.", EmptyJson), topology);

        int total = CountNodes(topology.Devices);
        string json = JsonSerializer.Serialize(topology, JsonOpts);
        string devWord = topology.DeviceCount == 1 ? "device" : "devices";
        return (($"Found {topology.DeviceCount} fieldbus {devWord}, {total} nodes total.", json), topology);
    }

    public string GetDeviceXml(string treePath)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            return "[Error] treePath is required.";
        if (treePath.Equals("TIID", StringComparison.OrdinalIgnoreCase))
            return "[Error] treePath must be a node under TIID (not the root).";

        string? error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try { return session.GetDeviceXml(treePath); }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }
    }

    public string ConfigureEtherCatSlave(string treePath, string xml)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            return "[Error] treePath is required.";
        if (treePath.Equals("TIID", StringComparison.OrdinalIgnoreCase))
            return "[Error] treePath must be a node under TIID (not the root).";
        if (string.IsNullOrWhiteSpace(xml))
            return "[Error] xml is required.";

        string? error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try { session.ConfigureEtherCatSlave(treePath, xml); }
        catch (InvalidOperationException ex) { return $"[Error] {ex.Message}"; }

        // Issue #152 D-23: ConsumeXml says nothing about what it applied — read the node back.
        IReadOnlyList<string> diffs;
        try { diffs = ConsumeXmlReadBack.Differences(xml, session.GetDeviceXml(treePath)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException)
        {
            return $"[Error] ConsumeXml ran on '{treePath}' and the project was saved, but the read-back failed: {ex.Message}";
        }
        if (diffs.Count > 0)
            return $"[Error] ConsumeXml ran on '{treePath}' and the project was saved, but {diffs.Count} value(s) did not " +
                   "read back as written (XAE ignored or changed them):\n  " + string.Join("\n  ", diffs);
        return $"Slave '{treePath}' configured, every written value read back, project saved (Save All).";
    }

    private static int CountNodes(IReadOnlyList<FieldbusNode> nodes)
    {
        int total = 0;
        foreach (var n in nodes)
        {
            total += 1 + CountNodes(n.Children);
        }
        return total;
    }
}
