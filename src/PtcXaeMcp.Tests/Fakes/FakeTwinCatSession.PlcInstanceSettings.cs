using System.Xml.Linq;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- plc-instance-settings ----
    /// <summary>Children per tree path (name, ItemType).</summary>
    public Dictionary<string, List<TreeChildInfo>> TreeChildren { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ProduceXml(false) result per tree path.</summary>
    public Dictionary<string, string> TreeItemXml { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every ConsumeXml call (path, xml), in order.</summary>
    public List<(string Path, string Xml)> ConsumedXml { get; } = [];

    /// <summary>Applies a ConsumeXml payload to <see cref="TreeItemXml"/> (null = record only, like a silent XAE no-op).</summary>
    public Func<string, string, string, string>? ConsumeXmlApplier { get; set; } = DefaultApplier;

    public Exception? ConsumeTreeItemXmlThrows { get; set; }

    public IReadOnlyList<TreeChildInfo> ListTreeChildren(string treePath)
    {
        CallLog.Add($"ListTreeChildren({treePath})");
        return TreeChildren.TryGetValue(treePath, out var list)
            ? list.ToList()
            : throw new InvalidOperationException($"Tree path not found: {treePath}");
    }

    public string ProduceTreeItemXml(string treePath)
    {
        CallLog.Add($"ProduceTreeItemXml({treePath})");
        return TreeItemXml.TryGetValue(treePath, out var xml)
            ? xml
            : throw new InvalidOperationException($"Tree path not found: {treePath}");
    }

    public void ConsumeTreeItemXml(string treePath, string xml)
    {
        CallLog.Add($"ConsumeTreeItemXml({treePath})");
        if (ConsumeTreeItemXmlThrows is not null) throw ConsumeTreeItemXmlThrows;
        ConsumedXml.Add((treePath, xml));
        if (ConsumeXmlApplier is not null && TreeItemXml.TryGetValue(treePath, out var current))
            TreeItemXml[treePath] = ConsumeXmlApplier(treePath, current, xml);
    }

    /// <summary>
    /// Mimics the live XAE: an init value is applied only for a symbol already in InitSymbolValues;
    /// TmcFileCopy=false also clears TpyFileCopy, TpyFileCopy=true also sets TmcFileCopy.
    /// </summary>
    public static string DefaultApplier(string path, string current, string payload)
    {
        var doc = XElement.Parse(current);
        var input = XElement.Parse(payload);

        foreach (var v in input.Descendants("InitSymbolValues").Elements("Value"))
        {
            string name = (string?)v.Element("Name") ?? string.Empty;
            var target = doc.Descendants("InitSymbolValues").Elements("Value")
                .FirstOrDefault(x => string.Equals((string?)x.Element("Name"), name, StringComparison.OrdinalIgnoreCase));
            target?.SetElementValue("Value", (string?)v.Element("Value"));
        }

        var def = doc.Descendants("PlcProjectDef").FirstOrDefault();
        foreach (var flag in input.Descendants("PlcProjectDef").Elements())
        {
            if (def is null) break;
            bool on = string.Equals(flag.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (flag.Name == "TmcFileCopy")
            {
                def.SetElementValue("TmcFileCopy", on ? "true" : "false");
                if (!on) def.SetElementValue("TpyFileCopy", "false");
            }
            else if (flag.Name == "TpyFileCopy")
            {
                def.SetElementValue("TpyFileCopy", on ? "true" : "false");
                if (on) def.SetElementValue("TmcFileCopy", "true");
            }
        }
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}
