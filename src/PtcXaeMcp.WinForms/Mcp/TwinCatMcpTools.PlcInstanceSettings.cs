using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

/// <summary>plc-instance-settings — MCP-only, no GUI tab.</summary>
internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_read_init_symbols", ReadOnly = true)]
    [Description("Read the init values of all {attribute 'TcInitSymbol'} variables from the PLC instance (ProduceXml, " +
                 "TcModuleInstance/Module/InitSymbolValues; TwinCAT >= 4024.17). These values live in the TwinCAT configuration, " +
                 "not in the PLC source. The PLC project must be built. Read-only.")]
    public string ReadInitSymbols(
        [Description("PLC project under TIPC, e.g. 'FixturePlc'. Optional when the solution has exactly one.")] string? plcProjectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_init_symbols", new { plcProjectName }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcInstanceSettingsCoordinator(bridge.Dispatcher.Session).ReadInitSymbols(plcProjectName)), cancellationToken);

    [McpServerTool(Name = "twincat_write_init_symbol", ReadOnly = false, Destructive = true)]
    [Description("Set the init value of one {attribute 'TcInitSymbol'} variable on the PLC instance (ConsumeXml). The symbol must already be " +
                 "listed by twincat_read_init_symbols (the XAE silently ignores others). Proven by a fresh read-back; a value the XAE does not " +
                 "store as given (invalid for the type, out of range — it would wrap) is refused and the old value restored. Logs the XAE out first, " +
                 "saves afterwards. Takes effect with the next Activate Configuration — this tool does not activate.")]
    public string WriteInitSymbol(
        [Description("Full symbol name as listed by twincat_read_init_symbols, e.g. 'GVL_Params.nSpeed'.")] string symbol,
        [Description("New init value in IEC literal form as the XAE shows it, e.g. '42', '1.5', 'TRUE'.")] string value,
        [Description("PLC project under TIPC. Optional when the solution has exactly one.")] string? plcProjectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_write_init_symbol", new { symbol, value, plcProjectName }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcInstanceSettingsCoordinator(bridge.Dispatcher.Session).WriteInitSymbol(symbol, value, plcProjectName)), cancellationToken);

    [McpServerTool(Name = "twincat_read_retain_config", ReadOnly = true)]
    [Description("Read the retain configuration of the PLC instance (ProduceXml): every data area with AreaType RetainSrc with its " +
                 "symbols (type, BitSize, BitOffs) and RetainDataMappings if present. Read-only — the configuration cannot be written " +
                 "(Infosys); retain variables are declared in the source (VAR RETAIN). The PLC project must be built.")]
    public string ReadRetainConfig(
        [Description("PLC project under TIPC. Optional when the solution has exactly one.")] string? plcProjectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_retain_config", new { plcProjectName }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcInstanceSettingsCoordinator(bridge.Dispatcher.Session).ReadRetainConfig(plcProjectName)), cancellationToken);

    [McpServerTool(Name = "twincat_get_type_file_generation", ReadOnly = true)]
    [Description("Read whether the PLC project generates/copies its .tmc and .tpy type files at build (PlcProjectDef TmcFileCopy / " +
                 "TpyFileCopy on the PLC project node under TIPC). Shows the actual state. Read-only.")]
    public string GetTypeFileGeneration(
        [Description("PLC project under TIPC. Optional when the solution has exactly one.")] string? plcProjectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_get_type_file_generation", new { plcProjectName }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcInstanceSettingsCoordinator(bridge.Dispatcher.Session).GetTypeFileGeneration(plcProjectName)), cancellationToken);

    [McpServerTool(Name = "twincat_set_type_file_generation", ReadOnly = false, Destructive = true)]
    [Description("Switch .tmc/.tpy type-file generation of the PLC project on or off (ConsumeXml PlcProjectDef TmcFileCopy / TpyFileCopy). " +
                 "The XAE couples them: tpy=true also turns tmc on, tmc=false also turns tpy off (tmc=false with tpy=true is refused). " +
                 "Returns the flags read back. Logs the XAE out first, saves afterwards; effective at the next build.")]
    public string SetTypeFileGeneration(
        [Description("Generate the .tmc file (omit to leave unchanged).")] bool? tmc = null,
        [Description("Generate the .tpy file (omit to leave unchanged).")] bool? tpy = null,
        [Description("PLC project under TIPC. Optional when the solution has exactly one.")] string? plcProjectName = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_set_type_file_generation", new { tmc, tpy, plcProjectName }, () =>
            bridge.Dispatcher.Run(() =>
                new PlcInstanceSettingsCoordinator(bridge.Dispatcher.Session).SetTypeFileGeneration(tmc, tpy, plcProjectName)), cancellationToken);
}
