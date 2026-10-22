using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_get_fieldbus_topology", ReadOnly = true)]
    [Description("Walk the TwinCAT TIID I/O tree and return a JSON topology (devices, boxes, slaves, channels). " +
                 "Read-only. Requires XAE running with a solution open. " +
                 "Result body is a one-line summary followed by indented JSON (deviceCount, devices[]).")]
    public string GetFieldbusTopology(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_get_fieldbus_topology", null, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, json) = new FieldbusCoordinator(bridge.Dispatcher.Session).GetTopology();
                if (McpErrors.IsError(summary) ||
                    summary.StartsWith("No fieldbus", StringComparison.OrdinalIgnoreCase))
                    return summary;
                return summary + Environment.NewLine + json;
            }), cancellationToken);

    [McpServerTool(Name = "twincat_get_device_xml", ReadOnly = true)]
    [Description("Return the raw TwinCAT ProduceXml(false) for a single TIID tree node. " +
                 "Read-only. Requires XAE running with a solution open. " +
                 "treePath must be a non-root caret-separated path under TIID; the root 'TIID' " +
                 "itself is rejected to prevent enormous dumps.")]
    public string GetDeviceXml(
        [Description("Caret-separated tree path under TIID, e.g. 'TIID^Device 1 (EtherCAT)^Box 1 (EK1100)'. " +
                     "Get a valid path via twincat_get_fieldbus_topology first.")]
        string treePath,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_get_device_xml", new { treePath }, () =>
            bridge.Dispatcher.Run(() => new FieldbusCoordinator(bridge.Dispatcher.Session).GetDeviceXml(treePath)), cancellationToken);

    [McpServerTool(Name = "twincat_configure_ethercat_slave", ReadOnly = false, Destructive = true)]
    [Description("Write device-configuration XML to an EtherCAT slave node via ConsumeXml, then read the node back " +
                 "(ProduceXml) and verify that every written leaf value is there; values XAE ignored or changed are " +
                 "returned as [Error]. Side effect: saves ALL dirty documents of the solution (File > Save All), " +
                 "not only this node. " +
                 "Requires XAE running with a solution open. " +
                 "Obtain current XML first via twincat_get_device_xml, modify it, then pass it back here. " +
                 "treePath must be a non-root caret-separated path under TIID.")]
    public string ConfigureEtherCatSlave(
        [Description("Caret-separated tree path of the slave to configure, " +
                     "e.g. 'TIID^Device 1 (EtherCAT)^Box 1 (EK1100)'. " +
                     "Get a valid path via twincat_get_fieldbus_topology first.")]
        string treePath,
        [Description("Full device-configuration XML string to write (TwinCAT ConsumeXml format). " +
                     "Obtain the current XML via twincat_get_device_xml and modify as needed.")]
        string xml,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_configure_ethercat_slave",
            new { treePath, xml }, () =>
            bridge.Dispatcher.Run(() =>
                new FieldbusCoordinator(bridge.Dispatcher.Session).ConfigureEtherCatSlave(treePath, xml)), cancellationToken);
}
