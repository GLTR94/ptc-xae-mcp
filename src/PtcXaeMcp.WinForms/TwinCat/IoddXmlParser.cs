using System.Xml;
using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>Identity of an IO-Link device, read from its IODD's <c>&lt;DeviceIdentity&gt;</c>.</summary>
/// <param name="VendorId">IO-Link vendor id (integer).</param>
/// <param name="DeviceId">IO-Link device id (integer).</param>
/// <param name="VendorName">Vendor name attribute, e.g. <c>Festo</c>.</param>
/// <param name="ProductIds">Product ids from the device-variant collection, e.g. <c>VABX-A-P-EL-E12-IOS</c>.</param>
internal sealed record IoddIdentity(
    int VendorId,
    int DeviceId,
    string? VendorName,
    IReadOnlyList<string> ProductIds);

/// <summary>
/// Parses the identity of an IODD file. Namespace-agnostic (IODD uses
/// <c>xmlns="http://www.io-link.com/IODD/2010/10"</c>) — matches by local name.
/// Shape verified against the Beckhoff/Festo IODDs shipped in TwinCAT's
/// <c>Config\Io\IOLink</c> folder.
/// </summary>
internal static class IoddXmlParser
{
    /// <exception cref="InvalidOperationException">The XML is empty, malformed, or carries no DeviceIdentity.</exception>
    public static IoddIdentity ParseIdentity(string ioddXml)
    {
        if (string.IsNullOrWhiteSpace(ioddXml))
            throw new InvalidOperationException("IODD XML is empty.");

        XDocument doc;
        try { doc = XDocument.Parse(ioddXml); }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"IODD XML is not well-formed: {ex.Message}", ex);
        }

        var di = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "DeviceIdentity")
            ?? throw new InvalidOperationException(
                "IODD has no <DeviceIdentity> element — is this a valid IO-Link IODD file?");

        var productIds = doc.Descendants()
            .Where(e => e.Name.LocalName == "DeviceVariant")
            .Select(e => (string?)e.Attribute("productId"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new IoddIdentity(
            VendorId: ParseIntAttr(di, "vendorId"),
            DeviceId: ParseIntAttr(di, "deviceId"),
            VendorName: string.IsNullOrWhiteSpace((string?)di.Attribute("vendorName"))
                ? null : ((string?)di.Attribute("vendorName"))!.Trim(),
            ProductIds: productIds);
    }

    private static int ParseIntAttr(XElement el, string name) =>
        int.TryParse((string?)el.Attribute(name), out int v) ? v : 0;
}
