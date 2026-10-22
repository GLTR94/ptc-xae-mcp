using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Parses the <c>Mappings.xml</c> file in a TwinCAT Safety project.
/// Maps safe signal names (SIxxx/SQxxx) to physical terminal/channel/SafeAddress.
/// Schema is not publicly documented; parser is defensive.
/// </summary>
internal static class SafetyMappingsParser
{
    internal static IReadOnlyList<MappingsEntry> Parse(string xml)
    {
        var results = new List<MappingsEntry>();
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            // Look for "Mapping" or "Channel" elements with SignalName/Signal attrs.
            XmlNodeList? nodes = doc.SelectNodes("//*[@SignalName or @Signal or @signalName]");
            if (nodes is null || nodes.Count == 0)
                nodes = doc.SelectNodes("//*[local-name()='Mapping' or local-name()='Channel']");

            if (nodes is not null)
            {
                foreach (XmlNode node in nodes)
                {
                    if (node is not XmlElement el) continue;
                    var entry = ParseEntry(el);
                    if (entry is not null) results.Add(entry);
                }
            }
        }
        catch { /* malformed */ }

        return results;
    }

    private static MappingsEntry? ParseEntry(XmlElement el)
    {
        string? signal = el.GetAttribute("SignalName") is { Length: > 0 } s ? s
            : el.GetAttribute("signalName") is { Length: > 0 } s2 ? s2
            : el.GetAttribute("Signal") is { Length: > 0 } s3 ? s3 : null;

        if (string.IsNullOrWhiteSpace(signal)) return null;

        string? terminal = el.GetAttribute("Terminal") is { Length: > 0 } t ? t
            : el.GetAttribute("terminal") is { Length: > 0 } t2 ? t2 : null;
        string? channel = el.GetAttribute("Channel") is { Length: > 0 } c ? c
            : el.GetAttribute("channel") is { Length: > 0 } c2 ? c2 : null;
        string? safeAddr = el.GetAttribute("SafeAddress") is { Length: > 0 } a ? a
            : el.GetAttribute("safeAddress") is { Length: > 0 } a2 ? a2 : null;
        string? function = el.GetAttribute("Function") is { Length: > 0 } f ? f
            : el.GetAttribute("function") is { Length: > 0 } f2 ? f2 : null;

        // A row is joined to an alias device ONLY through an SDSID the row itself carries
        // (issue #153, D-7) — never by position or by a guess from another file.
        string rawId = el.GetAttribute("SDSID") is { Length: > 0 } i1 ? i1
            : el.GetAttribute("SdsId") is { Length: > 0 } i2 ? i2
            : el.GetAttribute("sdsId");
        int? sdsId = int.TryParse(rawId, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : null;

        return new MappingsEntry(signal!, terminal, channel, safeAddr, function, sdsId);
    }
}
