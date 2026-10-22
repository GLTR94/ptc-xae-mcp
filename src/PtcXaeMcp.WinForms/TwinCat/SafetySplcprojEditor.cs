using System.Text;
using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Modifies a TwinCAT Safety PLC project file (<c>*.splcproj</c>) to register
/// a new alias device and advance <c>NextSdsId</c>.
/// Preserves BOM and CRLF line endings already present in the file.
/// </summary>
internal static class SafetySplcprojEditor
{
    /// <summary>
    /// Registers <paramref name="relativeIncludePath"/> as a new <c>&lt;None Include="..."&gt;</c> item
    /// and, when the project already has a <c>&lt;NextSdsId&gt;</c> element, sets it to
    /// <paramref name="newNextSdsId"/>. Returns the modified XML and whether anything changed.
    /// <para>
    /// Idempotent (issue #153, D-22): the existing-include check runs FIRST and compares the attribute
    /// in C# (case-insensitive, slash-normalised) — no XPath string literal, so a path containing a quote
    /// cannot break or inject into the query — and an already-registered path returns the input
    /// unchanged, with <c>NextSdsId</c> untouched.
    /// </para>
    /// <para>
    /// A missing <c>&lt;NextSdsId&gt;</c> is NOT invented: the Beckhoff project templates carry none,
    /// and an element the TwinSAFE editor never wrote is not ours to add.
    /// </para>
    /// </summary>
    internal static (string Xml, bool Changed) AddAliasDevice(
        string xml, string relativeIncludePath, int? newNextSdsId)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        XmlElement root = doc.DocumentElement
            ?? throw new InvalidOperationException("The .splcproj has no root element.");

        string wanted = NormalizeInclude(relativeIncludePath);
        foreach (XmlElement none in NoneItems(doc))
            if (string.Equals(NormalizeInclude(none.GetAttribute("Include")), wanted, StringComparison.OrdinalIgnoreCase))
                return (xml, false);

        if (newNextSdsId is not null)
        {
            foreach (XmlNode n in doc.GetElementsByTagName("NextSdsId", root.NamespaceURI))
            {
                n.InnerText = newNextSdsId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            }
        }

        // Same namespace as the MSBuild root — CreateElement("None") without it would serialise as
        // <None xmlns=""> and MSBuild rejects that element.
        XmlElement noneEl = doc.CreateElement("None", root.NamespaceURI);
        noneEl.SetAttribute("Include", relativeIncludePath);

        // Append after the last registered .sds when there is one (keeps alias devices together and
        // reuses that line's indentation); otherwise into an ItemGroup that already holds <None>.
        XmlElement? lastSds = NoneItems(doc).LastOrDefault(e =>
            e.GetAttribute("Include").EndsWith(".sds", StringComparison.OrdinalIgnoreCase));
        XmlNode? anchor = lastSds ?? (XmlNode?)NoneItems(doc).LastOrDefault();
        if (anchor?.ParentNode is XmlNode parent)
        {
            string indent = anchor.PreviousSibling is XmlWhitespace ws ? ws.Value ?? "\n    " : "\n    ";
            parent.InsertAfter(noneEl, anchor);
            parent.InsertAfter(doc.CreateWhitespace(indent), anchor);
        }
        else
        {
            XmlElement group = doc.CreateElement("ItemGroup", root.NamespaceURI);
            group.AppendChild(doc.CreateWhitespace("\n    "));
            group.AppendChild(noneEl);
            group.AppendChild(doc.CreateWhitespace("\n  "));
            root.AppendChild(doc.CreateWhitespace("  "));
            root.AppendChild(group);
            root.AppendChild(doc.CreateWhitespace("\n"));
        }

        return (doc.OuterXml, true);
    }

    private static IEnumerable<XmlElement> NoneItems(XmlDocument doc)
    {
        foreach (XmlNode n in doc.GetElementsByTagName("None", doc.DocumentElement?.NamespaceURI ?? string.Empty))
            if (n is XmlElement e && e.HasAttribute("Include")) yield return e;
    }

    private static string NormalizeInclude(string path) =>
        path.Trim().Replace('/', '\\');

    // ── file I/O helpers ──────────────────────────────────────────────────────

    /// <summary>Reads a file preserving BOM detection.</summary>
    internal static (string Xml, bool HasBom, bool HasCrlf) ReadFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool hasBom = bytes.Length >= 3 &&
                      bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string content = hasBom
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.UTF8.GetString(bytes);
        bool hasCrlf = content.Contains("\r\n");
        return (content, hasBom, hasCrlf);
    }

    /// <summary>Writes <paramref name="xml"/> to <paramref name="path"/>, preserving BOM and CRLF.</summary>
    internal static void WriteFile(string path, string xml, bool withBom, bool withCrlf)
    {
        string content = withCrlf
            ? xml.Replace("\r\n", "\n").Replace("\n", "\r\n")
            : xml;
        byte[] body = Encoding.UTF8.GetBytes(content);
        if (withBom)
        {
            byte[] bom = { 0xEF, 0xBB, 0xBF };
            using var fs = File.Open(path, FileMode.Create);
            fs.Write(bom, 0, bom.Length);
            fs.Write(body, 0, body.Length);
        }
        else
        {
            File.WriteAllBytes(path, body);
        }
    }
}
