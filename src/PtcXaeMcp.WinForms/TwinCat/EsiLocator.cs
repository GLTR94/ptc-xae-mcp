using Microsoft.Win32;
using System.Xml;
using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Resolves the ESI file path for an EtherCAT slave. Primary source: the
/// &lt;MdpPlugIn&gt;&lt;EsiFile&gt; element embedded in the slave's ProduceXml, which
/// TwinCAT writes when loading the device. Fallback: registry + directory scan.
/// </summary>
internal static class EsiLocator
{
    /// <summary>
    /// Returns the ESI file path from the slave's raw ProduceXml string,
    /// or null when the path cannot be determined or the file does not exist.
    /// </summary>
    public static string? LocateFromXml(string produceXml)
    {
        string? path = ExtractMdpPlugInPath(produceXml);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;
        return null;
    }

    /// <summary>
    /// Returns the TwinCAT ESI directory (…/Config/Io/EtherCAT) via env-var or
    /// registry. Used as a fallback when &lt;MdpPlugIn&gt;&lt;EsiFile&gt; is absent.
    /// </summary>
    public static string? FindEsiDirectory()
    {
        string? tc3Dir = Environment.GetEnvironmentVariable("TwinCAT3Dir");
        if (!string.IsNullOrEmpty(tc3Dir))
        {
            var candidate = Path.Combine(tc3Dir, "Config", "Io", "EtherCAT");
            if (Directory.Exists(candidate)) return candidate;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Beckhoff\TwinCAT3\CurrentVersion");
            var dir = key?.GetValue("TwinCATDir") as string;
            if (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, "3.1", "Config", "Io", "EtherCAT");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        catch { /* registry unavailable in some test environments */ }

        return null;
    }

    /// <summary>
    /// Scans <paramref name="esiDirectory"/> for an ESI file describing a
    /// device with the given vendor/product code (issue #157 item 9 / D-11).
    /// Used as the fallback when the slave's own <c>&lt;MdpPlugIn&gt;&lt;EsiFile&gt;</c>
    /// path is absent or stale — e.g. a terminal whose ESI file exists on disk
    /// but wasn't recorded on the tree node. Matches by hex ProductCode the
    /// same way <see cref="EsiDictionaryParser"/> does (leading zeros/case
    /// insignificant); returns the first matching file, or null.
    /// </summary>
    public static string? FindEsiFile(string esiDirectory, long vendorId, long productCode)
    {
        string vidHex = ToBareHex(vendorId);
        string pcHex = ToBareHex(productCode);

        foreach (string file in SafeEnumerateXmlFiles(esiDirectory))
        {
            XmlDocument doc;
            try
            {
                doc = new XmlDocument();
                doc.Load(file);
            }
            catch (XmlException) { continue; }
            catch (IOException) { continue; }

            XmlNode? vendorIdNode = doc.SelectSingleNode("//EtherCATInfo/Vendor/Id");
            if (vendorIdNode is null) continue;
            if (!string.Equals(ToBareHex(vendorIdNode.InnerText), vidHex, StringComparison.OrdinalIgnoreCase))
                continue;

            XmlNodeList? typeNodes = doc.SelectNodes("//EtherCATInfo/Descriptions/Devices/Device/Type[@ProductCode]");
            if (typeNodes is null) continue;

            foreach (XmlNode typeNode in typeNodes)
            {
                string pcAttr = typeNode.Attributes?["ProductCode"]?.Value ?? "";
                if (string.Equals(ToBareHex(pcAttr), pcHex, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }

        return null;
    }

    private static IEnumerable<string> SafeEnumerateXmlFiles(string directory)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
        return files;
    }

    // Normalizes a hex string (with or without a leading "#x"/"0x") or plain
    // decimal-looking numeric string to a bare, leading-zero-stripped lowercase
    // hex string, mirroring EsiDictionaryParser.FindDevice's ProductCode match.
    private static string ToBareHex(long value)
    {
        string hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private static string ToBareHex(string raw)
    {
        string hex = (raw ?? "").Trim().TrimStart('#').TrimStart('x', 'X').TrimStart('0');
        return hex.Length == 0 ? "0" : hex;
    }

    private static string? ExtractMdpPlugInPath(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc.SelectSingleNode("//MdpPlugIn/EsiFile")?.InnerText?.Trim();
        }
        catch { return null; }
    }
}
