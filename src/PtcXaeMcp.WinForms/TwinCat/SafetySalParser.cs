using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Parses a TwinCAT Safety group file (<c>*.sal</c>), a DSL-Tools XML schema.
/// Extracts two data sets:
/// <list type="bullet">
///   <item>IO-usage assignments (<c>aliasDeviceIoUsage</c> — sdsId + channelId)</item>
///   <item>FBD network structure (FB instances, connections, parameters)</item>
/// </list>
/// Because the schema is not publicly documented, the parser is defensive:
/// all inferred bindings are flagged with <c>Inferred = true</c>.
/// </summary>
internal static class SafetySalParser
{
    // ── IO usage ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the <c>aliasDeviceIoUsage</c> entries (sdsId + channelId [+ functionId]) together with
    /// the enclosing <c>&lt;variable name=…&gt;</c> and whether it sits under <c>sourceUsages</c> or
    /// <c>targetUsages</c> — the nesting of the Beckhoff-shipped <c>TwinSAFE.sal</c> template. Only that
    /// element is read: an arbitrary element that happens to carry an <c>sdsId</c> attribute (e.g. a
    /// <c>twinSAFEGroupAliasPort</c> with <c>sdsId="0"</c>) is NOT an I/O usage (issue #153, D-7).
    /// Returns an empty list when nothing is found or the XML is malformed.
    /// </summary>
    internal static IReadOnlyList<SalIoUsageEntry> ParseIoUsage(string xml)
    {
        var results = new List<SalIoUsageEntry>();
        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(xml);
        }
        catch (XmlException) { return results; }

        XmlNodeList? nodes = doc.SelectNodes("//*[local-name()='aliasDeviceIoUsage']");
        if (nodes is null) return results;

        foreach (XmlNode node in nodes)
        {
            if (node is not XmlElement el) continue;
            int? sdsId = ReadIntAttr(el, "sdsId");
            int? channelId = ReadIntAttr(el, "channelId");
            if (sdsId is null || channelId is null) continue;

            string? variable = null, usage = null;
            for (XmlNode? p = el.ParentNode; p is XmlElement pe; p = p.ParentNode)
            {
                if (usage is null && pe.LocalName == "sourceUsages") usage = "source";
                else if (usage is null && pe.LocalName == "targetUsages") usage = "target";
                if (pe.LocalName == "variable")
                {
                    variable = pe.GetAttribute("name") is { Length: > 0 } n ? n : null;
                    break;
                }
            }
            results.Add(new SalIoUsageEntry(sdsId.Value, channelId.Value, variable, usage,
                ReadIntAttr(el, "functionId")));
        }
        return results;
    }

    // ── FBD network dump ──────────────────────────────────────────────────────

    /// <summary>
    /// Parses the FBD logic from a <c>.sal</c> file into groups and networks.
    /// Wiring that cannot be definitively resolved is marked <c>Inferred = true</c>.
    /// </summary>
    internal static IReadOnlyList<SalGroup> ParseGroups(string xml)
    {
        var groups = new List<SalGroup>();
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            // The root typically is the "Group" element or contains "Network" children.
            // Try to find "Network" elements — each represents one FBD network.
            XmlNodeList? networkNodes = FindNetworkNodes(doc);
            if (networkNodes is null || networkNodes.Count == 0)
                return groups;

            // For simplicity, treat all networks as belonging to a single group
            // (the .sal file name is the group name — callers pass it separately).
            var networks = new List<SalNetwork>();
            foreach (XmlNode netNode in networkNodes)
            {
                if (netNode is not XmlElement netEl) continue;
                networks.Add(ParseNetwork(netEl));
            }

            // Represent as one unnamed group (name injected by caller).
            if (networks.Count > 0)
                groups.Add(new SalGroup(string.Empty, networks));
        }
        catch { /* malformed */ }

        return groups;
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private static XmlNodeList? FindNetworkNodes(XmlDocument doc)
    {
        // Try common element names for FBD networks in DSL-Tools schemas.
        string[] names = { "Network", "FBNetwork", "Rung", "SafetyNetwork" };
        foreach (string n in names)
        {
            XmlNodeList? nodes = doc.GetElementsByTagName(n);
            if (nodes is not null && nodes.Count > 0) return nodes;
        }
        return null;
    }

    private static SalNetwork ParseNetwork(XmlElement netEl)
    {
        string? id = netEl.GetAttribute("Id");
        if (string.IsNullOrWhiteSpace(id)) id = netEl.GetAttribute("id");

        var instances = new List<SalFbInstance>();
        var connections = new List<SalConnection>();

        // Parse FB instances: elements named "FBD", "Block", "FunctionBlock", "FB"
        string[] instanceNames = { "FunctionBlock", "FBD", "Block", "FB", "Instance" };
        foreach (string tag in instanceNames)
        {
            XmlNodeList? fbNodes = netEl.GetElementsByTagName(tag);
            if (fbNodes is null) continue;
            foreach (XmlNode fbNode in fbNodes)
            {
                if (fbNode is not XmlElement fbEl) continue;
                instances.Add(ParseFbInstance(fbEl));
            }
        }

        // Parse connections: elements named "Connection", "Link", "Wire"
        string[] connNames = { "Connection", "Link", "Wire", "Connector" };
        foreach (string tag in connNames)
        {
            XmlNodeList? connNodes = netEl.GetElementsByTagName(tag);
            if (connNodes is null) continue;
            foreach (XmlNode connNode in connNodes)
            {
                if (connNode is not XmlElement connEl) continue;
                var conn = ParseConnection(connEl);
                if (conn is not null) connections.Add(conn);
            }
        }

        return new SalNetwork(id, instances, connections);
    }

    private static SalFbInstance ParseFbInstance(XmlElement el)
    {
        // Instance name and type are typically attributes.
        string? instanceName = el.GetAttribute("instanceName");
        if (string.IsNullOrWhiteSpace(instanceName)) instanceName = el.GetAttribute("name");
        if (string.IsNullOrWhiteSpace(instanceName)) instanceName = el.GetAttribute("Name");
        instanceName ??= string.Empty;

        string? typeName = el.GetAttribute("typeName");
        if (string.IsNullOrWhiteSpace(typeName)) typeName = el.GetAttribute("type");
        if (string.IsNullOrWhiteSpace(typeName)) typeName = el.GetAttribute("Type");

        // Parameters: child elements named "Param", "Parameter", "Property"
        var parms = new List<SalFbParam>();
        string[] paramTags = { "Param", "Parameter", "Property", "Attribute" };
        foreach (string tag in paramTags)
        {
            XmlNodeList? pNodes = el.GetElementsByTagName(tag);
            if (pNodes is null) continue;
            foreach (XmlNode pNode in pNodes)
            {
                if (pNode is not XmlElement pEl) continue;
                string? pName = pEl.GetAttribute("name") is { Length: > 0 } n ? n
                    : pEl.GetAttribute("Name") is { Length: > 0 } n2 ? n2 : null;
                string pVal = pEl.GetAttribute("value") is { Length: > 0 } v ? v
                    : pEl.GetAttribute("Value") is { Length: > 0 } v2 ? v2
                    : pEl.InnerText.Trim();
                if (pName is not null)
                    parms.Add(new SalFbParam(pName, pVal));
            }
        }

        // If typeName is absent but instanceName is a known FB prefix, flag inferred.
        bool inferred = string.IsNullOrWhiteSpace(typeName);

        return new SalFbInstance(instanceName, typeName, parms, inferred);
    }

    private static SalConnection? ParseConnection(XmlElement el)
    {
        string? src = el.GetAttribute("source") is { Length: > 0 } s ? s
            : el.GetAttribute("Source") is { Length: > 0 } s2 ? s2 : null;
        string? tgt = el.GetAttribute("target") is { Length: > 0 } t ? t
            : el.GetAttribute("Target") is { Length: > 0 } t2 ? t2 : null;

        // Also accept child moniker elements.
        if (src is null)
        {
            XmlNode? srcNode = el.SelectSingleNode("*[local-name()='Source' or local-name()='source']");
            src = srcNode?.InnerText.Trim();
        }
        if (tgt is null)
        {
            XmlNode? tgtNode = el.SelectSingleNode("*[local-name()='Target' or local-name()='target']");
            tgt = tgtNode?.InnerText.Trim();
        }

        if (string.IsNullOrWhiteSpace(src) && string.IsNullOrWhiteSpace(tgt)) return null;

        bool inferred = string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(tgt);
        return new SalConnection(src ?? "[?]", tgt ?? "[?]", inferred);
    }

    private static int? ReadIntAttr(XmlElement el, string name)
    {
        string v = el.GetAttribute(name);
        return int.TryParse(v, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int i) ? i : null;
    }
}
