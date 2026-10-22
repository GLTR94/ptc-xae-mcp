using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat.Schema;

/// <summary>
/// Pure XML read/mutation logic extracted from NativeTwinCatRunner.SetGvlAlwaysBindTrue and
/// TryReadAlwaysBindState. Operates only on a TreeItem's ProduceXml() string; the caller (the
/// runner) is responsible for the COM ProduceXml/ConsumeXml round-trip and any property-based
/// fallback (TrySetAlwaysBindViaProperties), which stays COM-bound and out of scope here.
/// </summary>
internal static class AlwaysBindXml
{
    internal readonly record struct ApplyResult(string Xml, bool Changed);

    internal static bool IsAlwaysBindSemanticName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string normalized = name.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        return normalized is "alwaysbind" or "alwayslink" or "linkalways" or "immerbinden";
    }

    internal static bool IsBooleanTrueText(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.Ordinal);
    }

    /// <summary>Reads the current AlwaysBind/AlwaysLink state from a TreeItem's XML, or null when
    /// the field is not present at all.</summary>
    internal static bool? TryReadState(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        XmlNodeList? allNodes = doc.SelectNodes("//*");
        if (allNodes is not null)
        {
            foreach (XmlNode node in allNodes)
            {
                string nodeName = node.Name ?? string.Empty;
                if (IsAlwaysBindSemanticName(nodeName))
                {
                    return IsBooleanTrueText(node.InnerText);
                }

                XmlAttributeCollection? attrs = node.Attributes;
                if (attrs is null)
                {
                    continue;
                }

                XmlAttribute? nameAttr = attrs["Name"];
                if (nameAttr is null)
                {
                    continue;
                }

                string attrName = nameAttr.Value ?? string.Empty;
                if (!IsAlwaysBindSemanticName(attrName))
                {
                    continue;
                }

                XmlAttribute? valueAttr = attrs["Value"];
                if (valueAttr is null)
                {
                    return null;
                }

                return IsBooleanTrueText(valueAttr.Value);
            }
        }

        XmlNodeList? propNodes = doc.SelectNodes("//*[Name and Value]");
        if (propNodes is not null)
        {
            foreach (XmlNode prop in propNodes)
            {
                XmlNode? nameNode = prop.SelectSingleNode("Name");
                XmlNode? valueNode = prop.SelectSingleNode("Value");
                if (nameNode is null || valueNode is null)
                {
                    continue;
                }

                string propName = nameNode.InnerText ?? string.Empty;
                if (IsAlwaysBindSemanticName(propName))
                {
                    return IsBooleanTrueText(valueNode.InnerText);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Sets every AlwaysBind/AlwaysLink-semantic node/attribute/property found in the XML to
    /// true, returning the (possibly unchanged) resulting XML and whether any mutation happened.
    /// Mirrors the field's various XML representations across TwinCAT versions (element text,
    /// Name/Value attribute pair, Name/Value child-element pair).
    /// </summary>
    internal static ApplyResult Apply(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        bool changed = false;

        XmlNodeList? allNodes = doc.SelectNodes("//*");
        if (allNodes is not null)
        {
            foreach (XmlNode node in allNodes)
            {
                string nodeName = node.Name ?? string.Empty;
                string innerText = node.InnerText ?? string.Empty;
                if (IsAlwaysBindSemanticName(nodeName))
                {
                    if (!IsBooleanTrueText(innerText))
                    {
                        node.InnerText = "true";
                        changed = true;
                    }
                }

                XmlAttributeCollection? attrs = node.Attributes;
                if (attrs is null)
                {
                    continue;
                }

                XmlAttribute? nameAttr = attrs["Name"];
                if (nameAttr is null)
                {
                    continue;
                }

                string attrName = nameAttr.Value ?? string.Empty;
                if (!IsAlwaysBindSemanticName(attrName))
                {
                    continue;
                }

                XmlAttribute? valueAttr = attrs["Value"];
                if (valueAttr is not null)
                {
                    if (!IsBooleanTrueText(valueAttr.Value))
                    {
                        valueAttr.Value = "true";
                        changed = true;
                    }
                }
                else
                {
                    XmlAttribute newAttr = doc.CreateAttribute("Value");
                    newAttr.Value = "true";
                    attrs.Append(newAttr);
                    changed = true;
                }
            }
        }

        XmlNodeList? propNodes = doc.SelectNodes("//*[Name and Value]");
        if (propNodes is not null)
        {
            foreach (XmlNode prop in propNodes)
            {
                XmlNode? nameNode = prop.SelectSingleNode("Name");
                XmlNode? valueNode = prop.SelectSingleNode("Value");
                if (nameNode is null || valueNode is null)
                {
                    continue;
                }

                string propName = nameNode.InnerText ?? string.Empty;
                if (IsAlwaysBindSemanticName(propName) && !IsBooleanTrueText(valueNode.InnerText))
                {
                    valueNode.InnerText = "true";
                    changed = true;
                }
            }
        }

        return new ApplyResult(doc.OuterXml, changed);
    }
}
