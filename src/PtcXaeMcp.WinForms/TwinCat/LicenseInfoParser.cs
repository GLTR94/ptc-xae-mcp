using System.Globalization;
using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Parses the XML returned by TIRC^License ProduceXml() into a
/// list of <see cref="LicenseEntry"/> records.
///
/// The actual XML schema is not documented by Beckhoff Infosys and must be
/// captured empirically (see runtime-activation spec open-questions). This
/// parser handles the most likely element-based structure observed in TC3
/// 4026.x and falls back gracefully when fields are absent.
/// </summary>
internal static class LicenseInfoParser
{
    /// <summary>
    /// Parses raw ProduceXml() output from the TIRC^License tree node.
    /// Tolerates unknown XML shapes — missing elements produce Unknown status.
    /// </summary>
    internal static LicenseInfoResult Parse(string xml)
    {
        var entries = new List<LicenseEntry>();
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            // Try element-centric schema:
            //   <LicenseInfo> / <Licenses> containing <LicenseEntry> children
            //   with <LicenseId>, <LicenseName>, <LicenseStatus>, <ExpiryDate>
            XmlNodeList? nodes = doc.SelectNodes(
                "//*[local-name()='LicenseEntry' or local-name()='License' or local-name()='LicInfo']");

            if (nodes is not null)
            {
                foreach (XmlNode node in nodes)
                {
                    string id     = Child(node, "LicenseId", "Id",     "id")  ?? string.Empty;
                    string name   = Child(node, "LicenseName","Name",  "name") ?? string.Empty;
                    string status = Child(node, "LicenseStatus","Status","state") ?? string.Empty;
                    string expiry = Child(node, "ExpiryDate","Expiry","ExpirationDate") ?? string.Empty;

                    // Also try attributes when elements are missing
                    if (string.IsNullOrEmpty(id))     id     = Attr(node, "id","licenseId")     ?? string.Empty;
                    if (string.IsNullOrEmpty(name))   name   = Attr(node, "name","licenseName") ?? string.Empty;
                    if (string.IsNullOrEmpty(status)) status = Attr(node, "status","licenseStatus") ?? string.Empty;
                    if (string.IsNullOrEmpty(expiry)) expiry = Attr(node, "expiryDate","expiry") ?? string.Empty;

                    entries.Add(new LicenseEntry(
                        Id:         id,
                        Name:       name,
                        Status:     ParseStatus(status),
                        ExpiryDate: ParseDate(expiry)));
                }
            }
        }
        catch { /* malformed XML: return whatever we collected */ }

        return new LicenseInfoResult(entries, xml);
    }

    internal static LicenseStatus ParseStatus(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return LicenseStatus.Unknown;
        return raw.Trim().ToLowerInvariant() switch
        {
            "present" or "valid" or "ok" or "active" or "1"  => LicenseStatus.Present,
            "trial"   or "testlicense" or "test"              => LicenseStatus.Trial,
            "expired" or "expiredtrial"                       => LicenseStatus.Expired,
            "missing" or "notfound" or "invalid" or "0"       => LicenseStatus.Missing,
            _ => LicenseStatus.Unknown,
        };
    }

    internal static DateTime? ParseDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Invariant, not the machine's current culture: this XML comes from the
        // XAE/runtime, not from a locale-aware user-facing source, so parsing it
        // against whatever culture happens to be active would make the result
        // depend on the host OS's regional settings (issue #157 B-19).
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return dt;
        // TwinCAT sometimes uses "dd.MM.yyyy" or "yyyy-MM-dd"
        string[] formats = ["dd.MM.yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "yyyyMMdd"];
        foreach (var fmt in formats)
            if (DateTime.TryParseExact(raw.Trim(), fmt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None, out dt))
                return dt;
        return null;
    }

    private static string? Child(XmlNode node, params string[] names)
    {
        foreach (string n in names)
        {
            XmlNode? child = node.SelectSingleNode($"*[local-name()='{n}']");
            if (child is not null && !string.IsNullOrWhiteSpace(child.InnerText))
                return child.InnerText.Trim();
        }
        return null;
    }

    private static string? Attr(XmlNode node, params string[] names)
    {
        if (node.Attributes is null) return null;
        foreach (string n in names)
        {
            XmlAttribute? attr = node.Attributes[n];
            if (attr is not null && !string.IsNullOrWhiteSpace(attr.Value))
                return attr.Value.Trim();
        }
        return null;
    }
}
