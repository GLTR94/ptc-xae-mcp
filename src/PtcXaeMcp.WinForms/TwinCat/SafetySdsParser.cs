using System.Globalization;
using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Parses a TwinCAT Safety alias-device description file (<c>*.sds</c>). The format is not
/// publicly documented; it is taken from the files Beckhoff ships with the TwinSAFE editor
/// (<c>Components\Base\SafetyTemplate\Templates\1.3.0.0\…\1 digital input (Standard)_1.sds</c>, captured
/// verbatim as <c>src/PtcXaeMcp.Tests/Fixtures/Safety</c>):
/// <code>
/// &lt;SafetyDeviceDesc Crc="…" FileFormatVersion="1.5"&gt;
///   &lt;AliasDeviceType&gt;&lt;Type&gt;1&lt;/Type&gt;&lt;SubType&gt;1&lt;/SubType&gt;&lt;VendorId&gt;0&lt;/VendorId&gt;&lt;/AliasDeviceType&gt;
///   &lt;SDSID&gt;1&lt;/SDSID&gt;
///   &lt;StandardAliasDevice&gt;&lt;LinkingMode&gt;Manual&lt;/LinkingMode&gt;&lt;IO&gt;&lt;Name&gt;In&lt;/Name&gt;…&lt;/IO&gt;&lt;/StandardAliasDevice&gt;
/// &lt;/SafetyDeviceDesc&gt;
/// </code>
/// Every value is read from an element that exists in the file; nothing is inferred. The device
/// name is the file name (the file carries none).
/// </summary>
internal static class SafetySdsParser
{
    /// <summary>
    /// Parses a single <c>.sds</c> file. Returns <c>null</c> when the XML cannot be parsed or the
    /// root is not a <c>SafetyDeviceDesc</c>.
    /// </summary>
    internal static SafetyDeviceInfo? Parse(
        string xml, AliasDeviceKind kind, string fileNameWithoutExt = "")
    {
        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        XmlElement? root = doc.DocumentElement;
        if (root is null || root.LocalName != "SafetyDeviceDesc") return null;

        int? sdsId = ChildInt(root, "SDSID");
        XmlElement? type = Child(root, "AliasDeviceType");
        int? typeValue = type is null ? null : ChildInt(type, "Type");
        int subType = (type is null ? null : ChildInt(type, "SubType")) ?? 0;

        XmlElement? standard = Child(root, "StandardAliasDevice");
        string? linkingMode = standard is null ? null : ChildText(standard, "LinkingMode");
        XmlElement? io = standard is null ? null : Child(standard, "IO");
        string? ioName = io is null ? null : ChildText(io, "Name");

        string? version = root.GetAttribute("FileFormatVersion") is { Length: > 0 } v ? v : null;
        string name = fileNameWithoutExt;

        return new SafetyDeviceInfo(
            Name: name,
            SdsId: sdsId,
            SubType: subType,
            LinkingMode: linkingMode,
            SignalName: IsSignalName(name) ? name : null,
            Kind: kind,
            Type: typeValue,
            IoName: ioName,
            FileFormatVersion: version);
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private static XmlElement? Child(XmlElement parent, string localName)
    {
        foreach (XmlNode n in parent.ChildNodes)
            if (n is XmlElement e && e.LocalName == localName) return e;
        return null;
    }

    private static string? ChildText(XmlElement parent, string localName) =>
        Child(parent, localName)?.InnerText.Trim() is { Length: > 0 } t ? t : null;

    private static int? ChildInt(XmlElement parent, string localName) =>
        int.TryParse(ChildText(parent, localName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : null;

    /// <summary>
    /// Default safety-signal prefixes (#156 item 5) — one customer's naming
    /// convention (bSI_ = Safety→PLC, bSQ_ = PLC→Safety),
    /// not a TwinCAT/IEC standard. Kept as the
    /// default so existing behavior is unchanged; override with
    /// <c>PTC_XAE_MCP_SAFETY_PREFIXES</c> (comma-separated) for a project
    /// that uses a different convention.
    /// </summary>
    public const string DefaultPrefixes = "bSI_,bSQ_,SI,SQ";

    /// <summary>Env var overriding <see cref="DefaultPrefixes"/>, ','-separated.</summary>
    public const string PrefixesEnvVar = "PTC_XAE_MCP_SAFETY_PREFIXES";

    /// <summary>The configured signal-name prefixes (env var, else <see cref="DefaultPrefixes"/>).</summary>
    internal static string[] SignalPrefixes()
    {
        // Read fresh each call (not cached) — cheap, and keeps this correct
        // under a test that toggles the env var within one process.
        string configured = Environment.GetEnvironmentVariable(PrefixesEnvVar) is { Length: > 0 } v
            ? v
            : DefaultPrefixes;

        return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static bool IsSignalName(string s) =>
        s.Length > 0 && SignalPrefixes().Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
