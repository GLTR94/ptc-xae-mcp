using System.Text.RegularExpressions;
using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Parses an IO-Link master's <c>ProduceXml(false)</c> into an <see cref="IoLinkConfig"/>.
/// Pure and tolerant — selects by descendant element name so it does not break
/// if TwinCAT re-nests the config. Shape verified against a live EL6224
/// (project 100001).
/// </summary>
internal static partial class IoLinkXmlParser
{
    [GeneratedRegex(@"IO-?Link\s*Port\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PortNameRegex();

    /// <summary>
    /// Parse the device XML of a master at <paramref name="masterPath"/>.
    /// Returns a config with <c>PortCount == 0</c> when the XML carries no
    /// IO-Link section (valid XML, just not an IO-Link master).
    /// </summary>
    /// <exception cref="InvalidOperationException">The XML cannot be loaded.</exception>
    public static IoLinkConfig Parse(string masterPath, string deviceXml)
    {
        if (string.IsNullOrWhiteSpace(deviceXml))
            return new IoLinkConfig(masterPath, MasterTypeFromPath(masterPath), 0, Array.Empty<IoLinkPort>());

        var doc = new XmlDocument();
        try { doc.LoadXml(deviceXml); }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(
                $"IO-Link device XML for '{masterPath}' is not well-formed: {ex.Message}", ex);
        }

        string masterType = GetText(doc.SelectSingleNode("//ItemSubTypeName")) is { Length: > 0 } sub
            ? FirstToken(sub)                       // "EL6224 (IO Link Master)" -> "EL6224"
            : MasterTypeFromPath(masterPath);

        // Named process data, keyed by port number. RxPdo = outputs (Q), TxPdo = inputs (I).
        var pdoByPort = new Dictionary<int, List<IoLinkPdoEntry>>();
        CollectPdo(doc, "RxPdo", "Q", pdoByPort);
        CollectPdo(doc, "TxPdo", "I", pdoByPort);

        // Device identity per port: <Port> elements under <Xml>, in document order.
        var portElems = SelectPortElements(doc);
        var ports = new List<IoLinkPort>();
        int portCount = Math.Max(portElems.Count, pdoByPort.Count == 0 ? 0 : MaxKey(pdoByPort));

        for (int p = 1; p <= portCount; p++)
        {
            XmlNode? pe = p <= portElems.Count ? portElems[p - 1] : null;
            string? deviceName = GetChildText(pe, "DeviceName");
            bool configured = !string.IsNullOrWhiteSpace(deviceName);
            pdoByPort.TryGetValue(p, out var pd);

            ports.Add(new IoLinkPort(
                Port: p,
                Configured: configured,
                DeviceName: NullIfEmpty(deviceName),
                VendorName: NullIfEmpty(GetChildText(pe, "VendorName")),
                ProductId: NullIfEmpty(GetChildText(pe, "ProductId")),
                DeviceFamily: NullIfEmpty(GetChildText(pe, "DeviceFamilyName")),
                IoddFileName: NullIfEmpty(GetChildText(pe, "FileName")),
                ProcessData: pd is null ? Array.Empty<IoLinkPdoEntry>() : pd));
        }

        return new IoLinkConfig(masterPath, masterType, ports.Count, ports);
    }

    private static void CollectPdo(XmlDocument doc, string pdoTag, string direction,
        Dictionary<int, List<IoLinkPdoEntry>> sink)
    {
        var pdos = doc.SelectNodes($"//{pdoTag}");
        if (pdos is null) return;
        foreach (XmlNode pdo in pdos)
        {
            string name = GetChildText(pdo, "Name") ?? "";
            var m = PortNameRegex().Match(name);
            if (!m.Success) continue;               // only port-bound PDOs (skip "DeviceState Inputs" etc.)
            if (!int.TryParse(m.Groups[1].Value, out int port)) continue;

            var entries = pdo.SelectNodes("Entry");
            if (entries is null) continue;
            foreach (XmlNode e in entries)
            {
                string entryName = GetChildText(e, "Name") ?? "";
                if (string.IsNullOrWhiteSpace(entryName)) continue;   // padding entries have no name
                var list = sink.TryGetValue(port, out var l) ? l : (sink[port] = new List<IoLinkPdoEntry>());
                list.Add(new IoLinkPdoEntry(
                    Name: entryName,
                    Direction: direction,
                    DataType: GetChildText(e, "DataType") ?? "",
                    BitLen: ParseIntOr(GetChildText(e, "BitLen"), 0),
                    Index: GetChildText(e, "Index") ?? "",
                    SubIndex: ParseIntOr(GetChildText(e, "SubIndex"), 0)));
            }
        }
    }

    // <Port> elements that are the IO-Link device slots: direct children of <Xml>.
    // Falls back to any <Port> carrying device identity if <Xml> is absent.
    private static List<XmlNode> SelectPortElements(XmlDocument doc)
    {
        var result = new List<XmlNode>();
        var underXml = doc.SelectNodes("//Xml/Port");
        if (underXml is { Count: > 0 })
        {
            foreach (XmlNode n in underXml) result.Add(n);
            return result;
        }
        var any = doc.SelectNodes("//Port");
        if (any is null) return result;
        foreach (XmlNode n in any)
            if (n["DeviceName"] is not null || n["FileName"] is not null) result.Add(n);
        return result;
    }

    private static int MaxKey(Dictionary<int, List<IoLinkPdoEntry>> d)
    {
        int max = 0;
        foreach (int k in d.Keys) if (k > max) max = k;
        return max;
    }

    private static string? GetChildText(XmlNode? parent, string childName) =>
        parent?[childName]?.InnerText?.Trim();

    private static string GetText(XmlNode? node) => node?.InnerText?.Trim() ?? "";

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string FirstToken(string s)
    {
        int sp = s.IndexOf(' ');
        return sp > 0 ? s[..sp] : s;
    }

    // "TIID^...^Term 28 (EL6224)" -> "EL6224"; falls back to the raw last segment.
    private static string MasterTypeFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        int caret = path.LastIndexOf('^');
        string leaf = caret >= 0 ? path[(caret + 1)..] : path;
        int open = leaf.LastIndexOf('(');
        int close = leaf.LastIndexOf(')');
        if (open >= 0 && close > open) return leaf[(open + 1)..close].Trim();
        return leaf.Trim();
    }

    private static int ParseIntOr(string? s, int fallback) =>
        int.TryParse(s, out int v) ? v : fallback;
}
