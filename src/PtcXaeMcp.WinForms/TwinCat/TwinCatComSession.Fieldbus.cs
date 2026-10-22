using System.Globalization;
namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    public FieldbusTopology GetFieldbusTopology() => ComBusyRetry.Run(() =>
    {
        dynamic? dte = GetActiveTcDteOrNull();
        if (dte is null) return new FieldbusTopology(0, Array.Empty<FieldbusNode>());

        object? sysMan = TryGetSystemManager(dte);
        if (sysMan is null) return new FieldbusTopology(0, Array.Empty<FieldbusNode>());

        dynamic? tiid = LookupTreeItemOrNull(sysMan, "TIID");
        if (tiid is null) return new FieldbusTopology(0, Array.Empty<FieldbusNode>());

        var devices = new List<FieldbusNode>();
        int devCount = 0;
        try { devCount = (int)tiid.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        for (int i = 1; i <= devCount; i++)
        {
            dynamic? dev;
            try { dev = tiid.Child(i); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
            if (dev is null) continue;
            var node = BuildFieldbusNode(dev, depth: 1, maxDepth: 10);
            if (node is not null) devices.Add(node);
        }
        var raw = new FieldbusTopology(devices.Count, devices);
        return FieldbusTopologyDedup.Deduplicate(raw);
    }, OpenTimeout);

    public string GetDeviceXml(string treePath) => ComBusyRetry.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(treePath))
            throw new InvalidOperationException("treePath is required.");

        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);

        dynamic? node = LookupTreeItemOrNull(sysMan, treePath)
            ?? throw new InvalidOperationException($"Tree path not found: {treePath}");

        try { return (string)node.ProduceXml(false); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ProduceXml failed on '{treePath}': {ex.Message}", ex);
        }
    }, OpenTimeout);

    public void ConfigureEtherCatSlave(string treePath, string xml) => ComBusyRetry.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(treePath))
            throw new InvalidOperationException("treePath is required.");
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("xml is required.");

        dynamic dte = GetActiveTcDte();
        object? sysMan = RequireSystemManager(dte);

        dynamic? node = LookupTreeItemOrNull(sysMan, treePath)
            ?? throw new InvalidOperationException($"Tree path not found: {treePath}");

        try { node.ConsumeXml(xml); }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"ConsumeXml failed on '{treePath}': {ex.Message}", ex);
        }

        // #171 audit (issue #152): busy during the save must not replay the ConsumeXml.
        SaveAllAfterMutation(dte);
    }, OpenTimeout);

    private static FieldbusNode? BuildFieldbusNode(dynamic node, int depth, int maxDepth,
        string? parentModelName = null, int childIndex = 1)
    {
        string name = "<?>";
        try { name = (string)node.Name; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        string path = name;
        try { path = (string)node.PathName; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        int itemType = -1;
        try { itemType = (int)node.ItemType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        int subType = -1;
        try { subType = (int)node.ItemSubType; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { try { subType = (int)node.SubType; } catch (Exception comEx2) when (ComCall.NotBusy(comEx2)) { } }

        int childCount = 0;
        try { childCount = (int)node.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        bool hasProduceXml = false;
        string? selectedPort = null;
        try
        {
            string xml = (string)node.ProduceXml(false);
            hasProduceXml = true;
            selectedPort = ParseSelectedPreviousPort(xml);
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        // TC3 4026.22 quirk: PreviousPort/Selected is absent in ProduceXml(false).
        // Infer port letter from the segment-master index (0 = not a segment master → no port).
        if (selectedPort is null && parentModelName is not null && childIndex > 0)
            selectedPort = IndexToPortLetter(childIndex);

        string? physicalPort = null;
        if (parentModelName is not null && selectedPort is not null)
            physicalPort = PortLabelResolver.Instance.Resolve(parentModelName, selectedPort);

        string thisModelName = ExtractModelName(name);

        var children = new List<FieldbusNode>();
        if (depth < maxDepth)
        {
            // Port letters only count downstream segment masters (box/device children).
            // Channels and terminals all enter through the same port as their coupler —
            // they don't consume a separate EtherCAT port letter.
            int portIndex = 0;
            for (int i = 1; i <= childCount; i++)
            {
                dynamic? child;
                try { child = node.Child(i); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (child is null) continue;

                string childName = "<?>";
                try { childName = (string)child.Name; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                bool isSegmentMaster = FieldbusKind.LooksLikeSegmentMaster(childName);
                bool isCoupler = FieldbusKind.LooksLikeEtherCatCoupler(childName);
                if (isCoupler) portIndex++;

                var built = BuildFieldbusNode(child, depth + 1, maxDepth, thisModelName,
                    isCoupler ? portIndex : 0);
                if (built is not null) children.Add(built);
            }
        }

        string kind = FieldbusKind.Classify(name, itemType, subType, depth, childCount, hasProduceXml);
        return new FieldbusNode(path, name, itemType, subType, kind, selectedPort, children, physicalPort);
    }

    // Maps 1-based child index to EtherCAT port letter: 1→A, 2→B, 3→C, 4→D.
    private static string IndexToPortLetter(int index) =>
        index is >= 1 and <= 26 ? ((char)('A' + index - 1)).ToString() : index.ToString(CultureInfo.InvariantCulture);

    // Extracts model name from a node name like "Box 1 (EK1122 A1.45)" → "EK1122".
    private static string ExtractModelName(string name)
    {
        int open = name.LastIndexOf('(');
        int close = name.LastIndexOf(')');
        if (open < 0 || close <= open) return name;
        var inner = name[(open + 1)..close].Trim();
        // inner may be "EK1122 A1.45" — take the part before the first space
        int space = inner.IndexOf(' ');
        return space > 0 ? inner[..space] : inner;
    }

    private static string? ParseSelectedPreviousPort(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            var nodes = doc.SelectNodes("/TreeItem/EtherCAT/Slave/PreviousPort");
            if (nodes is null) return null;
            foreach (System.Xml.XmlNode n in nodes)
            {
                // TwinCAT stores the selection as Selected="true" (ProduceXml),
                // but ConsumeXml patches write Selected="1"; accept both.
                if (n is System.Xml.XmlElement el && IsSelectedAttr(el.GetAttribute("Selected")))
                    return n["Port"]?.InnerText?.Trim();
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        return null;
    }

    private static bool IsSelectedAttr(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
