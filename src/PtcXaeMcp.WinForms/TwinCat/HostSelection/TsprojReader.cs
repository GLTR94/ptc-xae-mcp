using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat.HostSelection;

/// <summary>
/// Extracts the root <c>TcVersion</c> attribute from a TwinCAT
/// <c>.tsproj</c> (e.g. <c>&lt;TcSmProject TcVersion="3.1.4026.22" ...&gt;</c>).
/// Every failure mode — missing file, unreadable, not XML, no attribute,
/// unparsable attribute — returns <c>false</c> with a human-readable reason;
/// nothing throws into the caller (host resolution must degrade gracefully).
/// </summary>
public static class TsprojReader
{
    public static bool TryReadTcVersionFromFile(
        string path, out TcVersion? version, out string? error)
    {
        version = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "tsproj path is empty.";
            return false;
        }
        if (!File.Exists(path))
        {
            error = $".tsproj not found: {path}";
            return false;
        }

        string xml;
        try
        {
            xml = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = $"Could not read .tsproj: {ex.Message}";
            return false;
        }

        return TryReadTcVersion(xml, out version, out error);
    }

    public static bool TryReadTcVersion(
        string xmlContent, out TcVersion? version, out string? error)
    {
        version = null;
        error = null;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xmlContent);
        }
        catch (Exception ex)
        {
            error = $".tsproj is not valid XML: {ex.Message}";
            return false;
        }

        var root = doc.Root;
        if (root is null)
        {
            error = ".tsproj has no root element.";
            return false;
        }

        // TcVersion is an unqualified attribute on the project root.
        var attr = root.Attribute("TcVersion");
        if (attr is null || string.IsNullOrWhiteSpace(attr.Value))
        {
            error = "No TcVersion attribute on the .tsproj root element.";
            return false;
        }

        if (!TcVersion.TryParse(attr.Value, out version))
        {
            version = null;
            error = $"TcVersion attribute is not a valid version: '{attr.Value}'.";
            return false;
        }

        return true;
    }
}
