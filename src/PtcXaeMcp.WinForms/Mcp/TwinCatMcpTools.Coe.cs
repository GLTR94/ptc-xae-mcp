using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_read_coe_dictionary", ReadOnly = true)]
    [Description("Read the CoE offline dictionary for an EtherCAT slave from its ESI file. " +
                 "Returns each object (index, name, access, objectType) and its sub-indexes " +
                 "(subindex, name, dataType, bitSize, defaultValue, defaultText, access, isSetting). " +
                 "Enum-typed sub-indexes include a human-readable defaultText, e.g. 'IIR level 1 (2)'. " +
                 "Read-only; no activated configuration or live controller required. " +
                 "Result: one-line summary + indented JSON (objectCount, objects[]).")]
    public string ReadCoeDictionary(
        [Description("Caret-separated tree path of the EtherCAT slave under TIID, " +
                     "e.g. 'TIID^Device 1 (EtherCAT)^Box 1 (EK1100)^-1AI5 (EL3174)'. " +
                     "Get a valid path via twincat_get_fieldbus_topology first.")]
        string treePath,
        [Description("Optional index filter: single index (e.g. '800D') or hex range (e.g. '8000-80FF'). " +
                     "Omit to return all objects.")]
        string? indexFilter = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_coe_dictionary", new { treePath, indexFilter }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, json) = new CoeCoordinator(bridge.Dispatcher.Session)
                    .ReadDictionary(treePath, indexFilter);
                if (McpErrors.IsError(summary))
                    return summary;
                return summary + Environment.NewLine + json;
            }), cancellationToken);

    [McpServerTool(Name = "twincat_read_coe_startup", ReadOnly = true)]
    [Description("Read the configured Startup SDO-download overrides for an EtherCAT slave " +
                 "as stored in the TwinCAT project. These are the CoE register values that will " +
                 "be written to the device on the PS / SafeOp→Op transition — the answer to " +
                 "'which registers has the project changed away from the ESI default?'. " +
                 "Returns an empty list (not an error) when no overrides are configured. " +
                 "Read-only. Result: one-line summary + indented JSON (entryCount, entries[]).")]
    public string ReadCoeStartup(
        [Description("Caret-separated tree path of the EtherCAT slave under TIID, " +
                     "e.g. 'TIID^Device 1 (EtherCAT)^Box 1 (EK1100)^-1AI5 (EL3174)'. " +
                     "Get a valid path via twincat_get_fieldbus_topology first.")]
        string treePath,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_coe_startup", new { treePath }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, json) = new CoeCoordinator(bridge.Dispatcher.Session)
                    .ReadStartup(treePath);
                if (McpErrors.IsError(summary))
                    return summary;
                return summary + Environment.NewLine + json;
            }), cancellationToken);
}
