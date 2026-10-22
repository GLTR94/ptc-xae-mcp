using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    public CoeDictionary ReadCoeDictionary(string treePath, string? indexFilter) =>
        ComBusyRetry.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(treePath))
                throw new InvalidOperationException("treePath is required.");

            dynamic dte = GetActiveTcDte();
            object? sysMan = RequireSystemManager(dte);
            dynamic? node = LookupTreeItemOrNull(sysMan, treePath)
                ?? throw new InvalidOperationException($"Tree path not found: {treePath}");

            string xml;
            try { xml = (string)node.ProduceXml(false); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"ProduceXml failed on '{treePath}': {ex.Message}", ex);
            }

            var (vendorId, productCode, revisionNo, slaveType) = ParseCoeSlaveIdentity(xml);

            string? esiPath = EsiLocator.LocateFromXml(xml);
            if (esiPath is null)
            {
                // Fallback (issue #157 item 9 / D-11): the slave's own
                // <MdpPlugIn><EsiFile> path is absent or stale — scan the
                // TwinCAT ESI directory (env/registry-resolved) for a file
                // describing this vendor/product code instead of failing.
                string? esiDir = EsiLocator.FindEsiDirectory();
                if (esiDir is not null)
                    esiPath = EsiLocator.FindEsiFile(esiDir, vendorId, productCode);
            }
            if (esiPath is null)
                throw new InvalidOperationException(
                    $"ESI file not found for '{slaveType}' (ProductCode={productCode:X8}). " +
                    "The slave's <MdpPlugIn><EsiFile> path is absent or the file does not exist, " +
                    "and no matching file was found in the TwinCAT ESI directory either.");

            var esiDoc = LoadEsiDocument(esiPath);
            var (dict, _) = EsiDictionaryParser.Parse(
                treePath, slaveType, esiDoc, productCode, revisionNo, indexFilter);
            return dict;
        }, OpenTimeout);

    public CoeStartupList ReadCoeStartup(string treePath) =>
        ComBusyRetry.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(treePath))
                throw new InvalidOperationException("treePath is required.");

            dynamic dte = GetActiveTcDte();
            object? sysMan = RequireSystemManager(dte);
            dynamic? node = LookupTreeItemOrNull(sysMan, treePath)
                ?? throw new InvalidOperationException($"Tree path not found: {treePath}");

            string xml;
            try { xml = (string)node.ProduceXml(false); }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"ProduceXml failed on '{treePath}': {ex.Message}", ex);
            }

            var (_, _, _, slaveType) = ParseCoeSlaveIdentity(xml);

            var produceDoc = new XmlDocument();
            try { produceDoc.LoadXml(xml); }
            catch (XmlException ex)
            {
                throw new InvalidOperationException(
                    $"ProduceXml for '{treePath}' is not well-formed: {ex.Message}", ex);
            }

            return CoeStartupParser.Parse(treePath, slaveType, produceDoc);
        }, OpenTimeout);

    // Extracts (VendorId, ProductCode, RevisionNo, SlaveType) from a slave ProduceXml string.
    // Throws if the XML is not a CoE EtherCAT slave (no <EtherCAT><Slave><Info> section).
    private static (long VendorId, long ProductCode, long RevisionNo, string SlaveType)
        ParseCoeSlaveIdentity(string xml)
    {
        if (string.IsNullOrEmpty(xml))
            throw new InvalidOperationException("Node has no ProduceXml — not an EtherCAT slave.");

        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(xml);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"ProduceXml is not well-formed: {ex.Message}", ex);
        }

        var infoNode = doc.SelectSingleNode("//EtherCAT/Slave/Info");
        if (infoNode is null)
            throw new InvalidOperationException(
                "Node has no <EtherCAT><Slave><Info> section — not an EtherCAT CoE slave.");

        long vendorId = ParseLong(infoNode["VendorId"]?.InnerText);
        long productCode = ParseLong(infoNode["ProductCode"]?.InnerText);
        long revisionNo = ParseLong(infoNode["RevisionNo"]?.InnerText);

        // Friendly name from <ProductRevision> (e.g. "EL3174-0000-0017") or <ItemSubTypeName>
        string slaveType =
            doc.SelectSingleNode("//TreeItem/ItemSubTypeName")?.InnerText?.Trim()
            ?? infoNode["ProductRevision"]?.InnerText?.Trim()
            ?? "Unknown";

        // Strip trailing detail like "-0000-0017" from ProductRevision to get "EL3174"
        if (slaveType.Contains('-'))
            slaveType = slaveType.Split('-')[0].Trim();

        if (productCode == 0)
            throw new InvalidOperationException(
                "Slave ProductCode is 0 — cannot locate ESI file.");

        return (vendorId, productCode, revisionNo, slaveType);
    }

    private static XmlDocument LoadEsiDocument(string path)
    {
        using var reader = XmlReader.Create(path);
        var doc = new XmlDocument();
        doc.Load(reader);
        return doc;
    }

    private static long ParseLong(string? s) =>
        long.TryParse(s?.Trim(), out long v) ? v : 0;
}
