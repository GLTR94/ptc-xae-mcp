using System.Globalization;
using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Parses a slave's CoE offline dictionary from an EtherCAT Slave Information (ESI) XML
/// document. The matching Device is selected by ProductCode and RevisionNo; if no exact
/// revision match exists the highest available revision is used and the fallback is reported.
/// </summary>
internal static class EsiDictionaryParser
{
    public static (CoeDictionary Dictionary, string? FallbackNote) Parse(
        string treePath,
        string slaveType,
        XmlDocument esiDoc,
        long productCode,
        long revisionNo,
        string? indexFilter)
    {
        var (deviceNode, fallbackNote) = FindDevice(esiDoc, productCode, revisionNo);
        if (deviceNode is null)
            throw new InvalidOperationException(
                $"Device with ProductCode={productCode:X8} not found in ESI.");

        var dataTypes = ParseDataTypes(deviceNode);
        var objects = ParseObjects(deviceNode, dataTypes, indexFilter);

        return (new CoeDictionary(treePath, slaveType, objects.Count, objects, fallbackNote), fallbackNote);
    }

    private static (XmlNode? Device, string? FallbackNote) FindDevice(
        XmlDocument doc, long productCode, long revisionNo)
    {
        string pcHex = productCode.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        if (pcHex.Length == 0) pcHex = "0";

        XmlNode? exactMatch = null;
        XmlNode? fallbackMatch = null;
        long bestFallbackRevision = -1;

        var devices = doc.SelectNodes("//EtherCATInfo/Descriptions/Devices/Device");
        if (devices is null) return (null, null);

        foreach (XmlNode device in devices)
        {
            var typeNode = device["Type"];
            if (typeNode is null) continue;

            string pcAttr = (typeNode.Attributes?["ProductCode"]?.Value ?? "")
                .TrimStart('#').TrimStart('x', 'X').TrimStart('0');
            if (pcAttr.Length == 0) pcAttr = "0";

            if (!string.Equals(pcAttr, pcHex, StringComparison.OrdinalIgnoreCase))
                continue;

            string revAttr = typeNode.Attributes?["RevisionNo"]?.Value ?? "";
            long devRev = ParseHexOrDecimal(revAttr);

            if (devRev == revisionNo)
            {
                exactMatch = device;
                break;
            }

            if (devRev > bestFallbackRevision)
            {
                bestFallbackRevision = devRev;
                fallbackMatch = device;
            }
        }

        if (exactMatch is not null) return (exactMatch, null);
        if (fallbackMatch is not null)
        {
            string note = $"ESI revision {bestFallbackRevision:X} used as fallback for requested {revisionNo:X}.";
            return (fallbackMatch, note);
        }
        return (null, null);
    }

    private static Dictionary<string, DataTypeInfo> ParseDataTypes(XmlNode deviceNode)
    {
        var map = new Dictionary<string, DataTypeInfo>(StringComparer.OrdinalIgnoreCase);
        var dtNodes = deviceNode.SelectNodes("Profile/Dictionary/DataTypes/DataType");
        if (dtNodes is null) return map;

        foreach (XmlNode dt in dtNodes)
        {
            string name = dt["Name"]?.InnerText?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) continue;

            int bitSize = ParseIntOrZero(dt["BitSize"]?.InnerText);
            string? baseType = dt["BaseType"]?.InnerText?.Trim();

            var enumInfos = new List<EnumEntry>();
            var enumNodes = dt.SelectNodes("EnumInfo");
            if (enumNodes is not null)
            {
                foreach (XmlNode ei in enumNodes)
                {
                    string text = ei["Text"]?.InnerText?.Trim() ?? "";
                    string enumValStr = ei["Enum"]?.InnerText?.Trim() ?? "";
                    if (int.TryParse(enumValStr, out int intV))
                        enumInfos.Add(new EnumEntry(intV, text));
                    else if (TryParseHex(enumValStr, out int hexV))
                        enumInfos.Add(new EnumEntry(hexV, text));
                }
            }

            var subItems = new List<DataTypeSubItem>();
            var subNodes = dt.SelectNodes("SubItem");
            if (subNodes is not null)
            {
                foreach (XmlNode si in subNodes)
                {
                    int subIdx = ParseIntOrZero(si["SubIdx"]?.InnerText);
                    string siName = si["Name"]?.InnerText?.Trim() ?? "";
                    string siType = si["Type"]?.InnerText?.Trim() ?? "";
                    int siBitSize = ParseIntOrZero(si["BitSize"]?.InnerText);
                    int bitOffs = ParseIntOrZero(si["BitOffs"]?.InnerText);
                    var flags = si["Flags"];
                    string access = flags?["Access"]?.InnerText?.Trim() ?? "rw";
                    bool isSetting = flags?["Setting"]?.InnerText?.Trim() == "1";
                    subItems.Add(new DataTypeSubItem(subIdx, siName, siType, siBitSize, bitOffs, access, isSetting));
                }
            }

            map[name] = new DataTypeInfo(name, bitSize, baseType, subItems, enumInfos);
        }

        return map;
    }

    private static IReadOnlyList<CoeObject> ParseObjects(
        XmlNode deviceNode,
        Dictionary<string, DataTypeInfo> dataTypes,
        string? indexFilter)
    {
        var (filterLow, filterHigh) = ParseFilter(indexFilter);
        var result = new List<CoeObject>();

        var objectNodes = deviceNode.SelectNodes("Profile/Dictionary/Objects/Object");
        if (objectNodes is null) return result;

        foreach (XmlNode obj in objectNodes)
        {
            string indexStr = obj["Index"]?.InnerText?.Trim() ?? "";
            int indexVal = ParseHexIndex(indexStr);
            if (indexVal < 0) continue;

            if (filterLow >= 0 && (indexVal < filterLow || indexVal > filterHigh))
                continue;

            string indexHex = indexVal.ToString("X4", CultureInfo.InvariantCulture);
            string name = obj["Name"]?.InnerText?.Trim() ?? "";
            string typeName = obj["Type"]?.InnerText?.Trim() ?? "";
            string objAccess = obj["Flags"]?["Access"]?.InnerText?.Trim() ?? "rw";
            var infoNode = obj["Info"];

            // Collect per-sub-item DefaultData from <Info><SubItem> (keyed by sub-item Name)
            var defaultDataByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (infoNode is not null)
            {
                var infoSubItems = infoNode.SelectNodes("SubItem");
                if (infoSubItems is not null)
                {
                    foreach (XmlNode isi in infoSubItems)
                    {
                        string isiName = isi["Name"]?.InnerText?.Trim() ?? "";
                        string dd = isi["Info"]?["DefaultData"]?.InnerText?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(isiName) && !string.IsNullOrEmpty(dd))
                            defaultDataByName[isiName] = dd;
                    }
                }
            }

            // If the DataType has sub-items, it's a RECORD; expand them.
            if (dataTypes.TryGetValue(typeName, out var dt) && dt.SubItems.Count > 0)
            {
                var subIndexes = new List<CoeSubIndex>();
                foreach (var si in dt.SubItems)
                {
                    string? defaultVal = defaultDataByName.TryGetValue(si.Name, out var dd) ? dd : null;
                    string? defaultText = ResolveEnumText(defaultVal, si.Type, si.BitSize, dataTypes);
                    string siAccess = string.IsNullOrEmpty(si.Access) ? objAccess : si.Access;
                    subIndexes.Add(new CoeSubIndex(
                        SubIndex: si.SubIdx.ToString("X2", CultureInfo.InvariantCulture),
                        Name: si.Name,
                        DataType: si.Type,
                        BitSize: si.BitSize,
                        DefaultValue: defaultVal,
                        DefaultText: defaultText,
                        Access: siAccess,
                        IsSetting: si.IsSetting));
                }
                result.Add(new CoeObject(indexHex, name, objAccess, "RECORD", subIndexes));
            }
            else
            {
                // VAR: simple type or unknown type with no sub-items
                int bitSize = dataTypes.TryGetValue(typeName, out var simpledt)
                    ? simpledt.BitSize
                    : ParseIntOrZero(obj["BitSize"]?.InnerText);
                string? defaultVal = infoNode?["DefaultData"]?.InnerText?.Trim();
                result.Add(new CoeObject(indexHex, name, objAccess, "VAR",
                [
                    new CoeSubIndex("00", name, typeName, bitSize, defaultVal, null, objAccess, false)
                ]));
            }
        }

        return result;
    }

    private static string? ResolveEnumText(
        string? defaultHex,
        string typeName,
        int bitSize,
        Dictionary<string, DataTypeInfo> dataTypes)
    {
        if (defaultHex is null) return null;
        if (!dataTypes.TryGetValue(typeName, out var enumDt) || enumDt.EnumInfos.Count == 0) return null;

        int numVal = ParseLittleEndianHex(defaultHex, bitSize);
        var entry = enumDt.EnumInfos.Find(e => e.Value == numVal);
        return entry is not null ? $"{entry.Text} ({numVal})" : null;
    }

    private static (int Low, int High) ParseFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return (-1, -1);
        filter = filter.Trim();
        int dash = filter.IndexOf('-');
        if (dash > 0)
        {
            int lo = ParseHexIndex(filter[..dash].Trim());
            int hi = ParseHexIndex(filter[(dash + 1)..].Trim());
            return lo >= 0 && hi >= 0 ? (lo, hi) : (-1, -1);
        }
        int single = ParseHexIndex(filter);
        return single >= 0 ? (single, single) : (-1, -1);
    }

    private static int ParseHexIndex(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return -1;
        s = s.Trim().TrimStart('#').TrimStart('x', 'X').TrimStart('0');
        if (s.Length == 0) s = "0";
        return int.TryParse(s, NumberStyles.HexNumber, null, out int v) ? v : -1;
    }

    internal static long ParseHexOrDecimal(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim();
        if (s.StartsWith("#x", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
            return long.TryParse(s, NumberStyles.HexNumber, null, out long v) ? v : 0;
        }
        return long.TryParse(s, out long d) ? d : 0;
    }

    private static bool TryParseHex(string s, out int value)
    {
        if (string.IsNullOrWhiteSpace(s)) { value = 0; return false; }
        s = s.Trim().TrimStart('#').TrimStart('x', 'X');
        return int.TryParse(s, NumberStyles.HexNumber, null, out value);
    }

    private static int ParseIntOrZero(string? s) =>
        int.TryParse(s?.Trim(), out int v) ? v : 0;

    private static int ParseLittleEndianHex(string hex, int bitSize)
    {
        if (string.IsNullOrEmpty(hex)) return 0;
        if (hex.Length % 2 != 0) hex = "0" + hex;
        int byteCount = Math.Min(hex.Length / 2, 4);
        int result = 0;
        for (int i = 0; i < byteCount; i++)
        {
            if (!int.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, null, out int b)) break;
            result |= b << (i * 8);
        }
        return result;
    }

    private sealed record DataTypeInfo(
        string Name,
        int BitSize,
        string? BaseType,
        List<DataTypeSubItem> SubItems,
        List<EnumEntry> EnumInfos);

    private sealed record DataTypeSubItem(
        int SubIdx,
        string Name,
        string Type,
        int BitSize,
        int BitOffs,
        string Access,
        bool IsSetting);

    private sealed record EnumEntry(int Value, string Text);
}
