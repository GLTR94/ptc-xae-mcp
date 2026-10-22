using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Parses the TwinCAT Safety PLC project file (<c>*.splcproj</c>), a standard
/// MSBuild-format XML. Extracts alias-device paths, group (.sal) and user-FB
/// (.ufb) paths, and the <c>NextSdsId</c> counter.
/// Alias device kind (hardware / project-variable / system-variable) is inferred
/// from the folder segment of the Include path:
///   "Projekt-Variablen" → ProjectVariable
///   "Systemvariablen"   → SystemVariable
///   "Hardware"          → Hardware (default for .sds files in other folders)
/// </summary>
internal static class SafetySplcprojParser
{
    internal static SplcprojStructure Parse(string xml)
    {
        var aliases = new List<SplcprojAliasEntry>();
        var groups = new List<string>();
        var userFbs = new List<string>();
        int? nextSdsId = null;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            // NextSdsId is a PropertyGroup element
            XmlNode? nextId = doc.SelectSingleNode(
                "//*[local-name()='NextSdsId']");
            // Null when the element is absent or unparsable — the Beckhoff project
            // templates carry no <NextSdsId>, so absence is normal, not 0.
            if (nextId is not null &&
                int.TryParse(nextId.InnerText.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int parsed))
                nextSdsId = parsed;

            // Collect all <None Include="..."> entries
            XmlNodeList? noneItems = doc.SelectNodes(
                "//*[local-name()='None'][@Include]");
            if (noneItems is not null)
            {
                foreach (XmlNode node in noneItems)
                {
                    string include = node.Attributes!["Include"]!.Value.Trim();
                    string ext = Path.GetExtension(include).ToLowerInvariant();

                    if (ext == ".sds")
                        aliases.Add(new SplcprojAliasEntry(include, ClassifyFromPath(include)));
                    else if (ext == ".sal")
                        groups.Add(include);
                    else if (ext == ".ufb")
                        userFbs.Add(include);
                }
            }
        }
        catch { /* malformed — return what we collected */ }

        return new SplcprojStructure(aliases, groups, userFbs, nextSdsId);
    }

    internal static AliasDeviceKind ClassifyFromPath(string includePath)
    {
        // Normalise slashes for comparison
        string norm = includePath.Replace('\\', '/');

        if (norm.IndexOf("Projekt-Variablen", StringComparison.OrdinalIgnoreCase) >= 0)
            return AliasDeviceKind.ProjectVariable;
        if (norm.IndexOf("Systemvariablen", StringComparison.OrdinalIgnoreCase) >= 0)
            return AliasDeviceKind.SystemVariable;
        if (norm.IndexOf("Hardware", StringComparison.OrdinalIgnoreCase) >= 0)
            return AliasDeviceKind.Hardware;

        return AliasDeviceKind.Unknown;
    }
}
