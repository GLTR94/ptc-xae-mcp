using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>One child of a tree node: its name and ItemType (matched by type, never by label).</summary>
internal sealed record TreeChildInfo(string Name, int ItemType);

/// <summary>Initialisation value of one <c>{attribute 'TcInitSymbol'}</c> variable on the PLC instance.</summary>
internal sealed record InitSymbolValue(string Name, string Value, string? Type);

/// <summary>One symbol of a retain data area.</summary>
internal sealed record RetainSymbol(string Name, string? Type, int? BitSize, long? BitOffs);

/// <summary>A data area with <c>AreaType="RetainSrc"</c> on the PLC instance.</summary>
internal sealed record RetainArea(string Name, int? AreaNo, long? ByteSize, IReadOnlyList<RetainSymbol> Symbols);

/// <summary>.tmc/.tpy generation flags of a PLC project (<c>&lt;PlcProjectDef&gt;</c>).</summary>
internal sealed record TypeFileFlags(bool Tmc, bool Tpy);

/// <summary>
/// plc-instance-settings: pure XML parsing/building for the
/// ProduceXml/ConsumeXml contracts of the PLC instance (ItemType 57) and the PLC project node
/// (TIPC child, ItemType 56). Shapes verified live on TC3 4026 (2026-10-08):
/// <list type="bullet">
/// <item>read: <c>TreeItem/TcModuleInstance/Module/InitSymbolValues/Value/{Name,Value}</c>; the
///   type comes from the matching <c>DataAreas/DataArea/Symbol</c> (Property <c>TcInitSymbol</c>).</item>
/// <item>write: the Infosys form <c>&lt;Instance&gt;&lt;SymbolInit&gt;</c> is accepted but IGNORED (silent
///   no-op); the read form <c>&lt;TcModuleInstance&gt;&lt;Module&gt;&lt;InitSymbolValues&gt;</c> works.</item>
/// <item>retain: <c>DataArea</c> with <c>AreaNo/@AreaType="RetainSrc"</c>; <c>RetainDataMappings</c> optional.</item>
/// <item>flags: <c>TreeItem/PlcProjectDef/{TmcFileCopy,TpyFileCopy}</c> on <c>TIPC^&lt;Projekt&gt;</c> (not on a
///   "… Project" node — that path does not resolve).</item>
/// </list>
/// </summary>
internal static class PlcInstanceSettingsXml
{
    /// <summary>ItemType of a PLC project node under TIPC.</summary>
    public const int PlcProjectItemType = 56;

    /// <summary>ItemType of the PLC instance under a PLC project node.</summary>
    public const int PlcInstanceItemType = 57;

    // ---- init symbols ------------------------------------------------------------------

    /// <summary>
    /// Init symbols of the instance. Returns null when the XML has no <c>TcModuleInstance/Module</c>
    /// (instance never built); an empty list when the module has no init symbols.
    /// </summary>
    public static IReadOnlyList<InitSymbolValue>? ParseInitSymbols(string instanceXml)
    {
        XElement? module = Module(Parse(instanceXml));
        if (module is null) return null;

        var types = module.Elements("DataAreas").Elements("DataArea").Elements("Symbol")
            .Where(s => s.Elements("Properties").Elements("Property")
                .Any(p => string.Equals((string?)p.Element("Name"), "TcInitSymbol", StringComparison.Ordinal)))
            .GroupBy(s => ((string?)s.Element("Name") ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => ((string?)g.First().Element("BaseType"))?.Trim(), StringComparer.OrdinalIgnoreCase);

        return module.Elements("InitSymbolValues").Elements("Value")
            .Select(v => (Name: ((string?)v.Element("Name"))?.Trim(), Value: (string?)v.Element("Value") ?? string.Empty))
            .Where(v => !string.IsNullOrEmpty(v.Name))
            .Select(v => new InitSymbolValue(v.Name!, v.Value, types.GetValueOrDefault(v.Name!)))
            .ToList();
    }

    /// <summary>ConsumeXml payload that sets one init value (the shape the XAE actually applies).</summary>
    public static string BuildInitSymbolXml(string symbol, string value) =>
        new XElement("TreeItem",
            new XElement("TcModuleInstance",
                new XElement("Module",
                    new XElement("InitSymbolValues",
                        new XElement("Value",
                            new XElement("Name", symbol),
                            new XElement("Value", value))))))
        .ToString(SaveOptions.DisableFormatting);

    /// <summary>
    /// Whether the value read back equals the requested one: exact (trimmed, case-insensitive for
    /// TRUE/FALSE and enum texts) or numerically equal (the XAE normalises e.g. "1.50" → "1.5").
    /// A numeric value the XAE silently wrapped (DINT overflow, live) is NOT equal.
    /// </summary>
    public static bool ValuesEqual(string requested, string actual)
    {
        string a = requested.Trim(), b = actual.Trim();
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (TryBool(a, out bool ba) && TryBool(b, out bool bb)) return ba == bb;
        return decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal da)
            && decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal db)
            && da == db;
    }

    private static bool TryBool(string s, out bool value)
    {
        switch (s.ToUpperInvariant())
        {
            case "TRUE": case "1": value = true; return true;
            case "FALSE": case "0": value = false; return true;
            default: value = false; return false;
        }
    }

    // ---- retain ------------------------------------------------------------------------

    /// <summary>Retain areas (<c>AreaType="RetainSrc"</c>); null when the instance has no module (never built).</summary>
    public static IReadOnlyList<RetainArea>? ParseRetainAreas(string instanceXml)
    {
        XElement? module = Module(Parse(instanceXml));
        if (module is null) return null;
        return module.Elements("DataAreas").Elements("DataArea")
            .Where(a => string.Equals((string?)a.Element("AreaNo")?.Attribute("AreaType"), "RetainSrc", StringComparison.OrdinalIgnoreCase))
            .Select(a => new RetainArea(
                ((string?)a.Element("Name"))?.Trim() ?? string.Empty,
                Int(a.Element("AreaNo")),
                Long(a.Element("ByteSize")),
                a.Elements("Symbol").Select(s => new RetainSymbol(
                    ((string?)s.Element("Name"))?.Trim() ?? string.Empty,
                    ((string?)s.Element("BaseType"))?.Trim(),
                    Int(s.Element("BitSize")),
                    Long(s.Element("BitOffs")))).ToList()))
            .ToList();
    }

    /// <summary>
    /// The <c>RetainDataMappings</c> element (anywhere in the instance XML) as compact one-line XML per
    /// child, or empty. Its schema is not documented in detail, so it is passed through, not interpreted.
    /// </summary>
    public static IReadOnlyList<string> ParseRetainMappings(string instanceXml) =>
        Parse(instanceXml).Descendants("RetainDataMappings").Elements()
            .Select(e => e.ToString(SaveOptions.DisableFormatting))
            .ToList();

    // ---- .tmc / .tpy -------------------------------------------------------------------

    /// <summary>Flags from <c>PlcProjectDef</c>; null when the node has no PlcProjectDef or no flag elements.</summary>
    public static TypeFileFlags? ParseTypeFileFlags(string projectXml)
    {
        XElement? def = Parse(projectXml).Descendants("PlcProjectDef").FirstOrDefault();
        if (def is null) return null;
        bool? tmc = Bool(def.Element("TmcFileCopy")), tpy = Bool(def.Element("TpyFileCopy"));
        return tmc is null || tpy is null ? null : new TypeFileFlags(tmc.Value, tpy.Value);
    }

    /// <summary>ConsumeXml payload for the given flags (TMC first: the XAE couples them, see the coordinator).</summary>
    public static string BuildTypeFileXml(bool? tmc, bool? tpy)
    {
        var def = new XElement("PlcProjectDef");
        if (tmc is not null) def.Add(new XElement("TmcFileCopy", tmc.Value ? "true" : "false"));
        if (tpy is not null) def.Add(new XElement("TpyFileCopy", tpy.Value ? "true" : "false"));
        return new XElement("TreeItem", def).ToString(SaveOptions.DisableFormatting);
    }

    // ---- helpers -----------------------------------------------------------------------

    private static XElement Parse(string xml)
    {
        try { return XElement.Parse(xml); }
        catch (XmlException ex) { throw new InvalidOperationException($"The XAE returned XML that cannot be parsed: {ex.Message}", ex); }
    }

    private static XElement? Module(XElement root) =>
        (root.Name.LocalName == "TcModuleInstance" ? root : root.Element("TcModuleInstance"))?.Element("Module");

    private static int? Int(XElement? e) =>
        int.TryParse(e?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    private static long? Long(XElement? e) =>
        long.TryParse(e?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;

    private static bool? Bool(XElement? e) =>
        e is null ? null : bool.TryParse(e.Value.Trim(), out bool v) ? v : null;
}
