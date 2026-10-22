namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // Read the master's ProduceXml and parse the IO-Link section. The config
    // persists inside the EtherCAT slave XML, so this is a pure read + parse — no mutation.
    public IoLinkConfig GetIoLinkConfig(string treePath) => ComBusyRetry.Run(() =>
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

        return IoLinkXmlParser.Parse(treePath, xml);
    }, OpenTimeout);
}
