using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

// hmi-internal-symbols (#165) — HMI-owned variables with no
// PLC behind them: client (framework) internal symbols %i%…%/i% in Properties\tchmiconfig.json,
// and server symbols of the TcHmiSrv domain in Server\TcHmiSrv\TcHmiSrv.Config.default.json.
internal sealed partial class TwinCatMcpTools
{
    private const string HmiDataTypeParam =
        "Data type: a TwinCAT HMI type reference such as 'tchmi:general#/definitions/Boolean', " +
        "'tchmi:general#/definitions/INT16', 'tchmi:general#/definitions/String' (the form tchmiconfig.json uses), " +
        "or one of the JSON-schema types boolean, number, integer, string.";

    private const string HmiDefaultValueParam =
        "Optional default value as a JSON scalar: true, 42, 3.5, \"text\" or null. Objects and arrays are refused " +
        "(they cannot cross the Automation Interface). Must fit dataType when that is a known scalar type.";

    [McpServerTool(Name = "twincat_hmi_list_internal_symbols", ReadOnly = true)]
    [Description("List the HMI-owned symbols of a TwinCAT HMI project — variables with no PLC behind them, read " +
                 "from the files TE2000 persists them in (works without the HMI server running). Two kinds, kept " +
                 @"apart: client = framework internal symbols %i%Name%/i% from Properties\tchmiconfig.json " +
                 "(symbols.internal: name, type, valueJson, persist, readOnly); server = the non-hidden symbols of the " +
                 @"TcHmiSrv domain from Server\TcHmiSrv\TcHmiSrv.Config.default.json (name, mapping, useMapping — " +
                 "false means a pure offline server variable —, dataType, defaultJson, persist). An unreadable server " +
                 "file is reported as unknown, never as 'none'. Read-only. " +
                 "Result: one-line summary + indented JSON { hmiProject, client{file,count,symbols[]}, server{file,readable,count,offline,symbols[]} }.")]
    public string HmiListInternalSymbols(
        [Description(HmiProjectParam)] string? hmiProject = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_list_internal_symbols", new { hmiProject }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session).ListInternalSymbols(hmiProject))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_add_internal_symbol", ReadOnly = false, Destructive = false)]
    [Description("Add a CLIENT (framework) internal symbol %i%name%/i% to a TwinCAT HMI project " +
                 @"(ITcHmiProject.AddInternalSymbol; stored in Properties\tchmiconfig.json) — HMI-side state such as a " +
                 "selection, a page index or a dialog flag that needs no PLC variable. " + ConfirmNote +
                 " Refuses a name that already exists (TE2000 adds without a duplicate check and would store it " +
                 "twice). Success only after tchmiconfig.json reads the new entry back — the Automation Interface " +
                 "call returns nothing; differences in the stored type/value/flags are reported as a WARNING. " +
                 "Not a server symbol: for one the HMI server holds, use twincat_hmi_map_offline_symbol. " +
                 "Reversible with twincat_hmi_remove_internal_symbol. Result: one-line summary + indented JSON.")]
    public string HmiAddInternalSymbol(
        [Description("Symbol name — letters, digits and '_', starting with a letter or '_', max 128. Referenced as %i%name%/i%.")]
        string name,
        [Description(HmiDataTypeParam)] string dataType,
        [Description(HmiDefaultValueParam)] string? defaultValueJson = null,
        [Description("Framework-side persistence of the value in the client (not the server, not historizing). Default false.")]
        bool persist = false,
        [Description("Mark the symbol read-only for bindings. Default false.")]
        bool readOnly = false,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_add_internal_symbol",
            new { hmiProject, name, dataType, defaultValueJson, persist, readOnly, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .AddInternalSymbol(hmiProject, name, dataType, defaultValueJson, persist, readOnly,
                        confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_remove_internal_symbol", ReadOnly = false, Destructive = true)]
    [Description("Remove a CLIENT (framework) internal symbol %i%name%/i% from a TwinCAT HMI project " +
                 "(ITcHmiProject.RemoveInternalSymbol). " + ConfirmNote +
                 " A name that does not exist returns 'Nothing to do' without a popup (names are case-sensitive). " +
                 "Success only after the entry is gone from Properties\\tchmiconfig.json on read-back — the " +
                 "Automation Interface returns false here even on success, so its return value is ignored. " +
                 "Bindings that still reference the symbol no longer resolve. Result: one-line summary + indented JSON.")]
    public string HmiRemoveInternalSymbol(
        [Description("Name of the internal symbol, as twincat_hmi_list_internal_symbols reports it.")]
        string name,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_remove_internal_symbol",
            new { hmiProject, name, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .RemoveInternalSymbol(hmiProject, name, confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_map_offline_symbol", ReadOnly = false, Destructive = false)]
    [Description("Create a SERVER symbol in the TcHmiSrv domain of a TwinCAT HMI project's engineering server " +
                 "(ITcHmiServer.MapOfflineSymbol; the server persists it in " +
                 @"Server\TcHmiSrv\TcHmiSrv.Config.default.json) — a variable the HMI SERVER holds, shared by all " +
                 "clients, with no PLC behind it; bind to it like any mapped symbol. online=false (default) makes a " +
                 "pure offline server variable whose value starts at the default; online=true sets USEMAPPING " +
                 "(unverified live — prefer the default). persist keeps the last value across server restarts " +
                 "(OPTIONS.persistedOfflineValue). Needs the HMI engineering server running. " + ConfirmNote +
                 " Refuses a name that the server configuration or the running server already knows. Success only " +
                 "after the symbol's config reads back with DOMAIN TcHmiSrv and the requested USEMAPPING — live " +
                 "(TcHmiSrv.Config::SYMBOLS::<mapName>), else from the saved config file. Remove it with " +
                 "twincat_hmi_unmap_symbol (domain 'TcHmiSrv'). Result: one-line summary + indented JSON.")]
    public string HmiMapOfflineSymbol(
        [Description("Server symbol name — dotted identifiers (letters, digits, '_'), e.g. 'Recipe.SelectedIndex', max 256.")]
        string mapName,
        [Description(HmiDataTypeParam)] string dataType,
        [Description(HmiDefaultValueParam)] string? defaultValueJson = null,
        [Description("USEMAPPING of the new symbol. Default false = offline server variable. true is unverified live.")]
        bool online = false,
        [Description("Server keeps the last value across restarts (OPTIONS.persistedOfflineValue). Default false.")]
        bool persist = false,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_map_offline_symbol",
            new { hmiProject, mapName, dataType, defaultValueJson, online, persist, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .MapOfflineSymbol(hmiProject, mapName, dataType, defaultValueJson, online, persist,
                        confirmTimeoutSeconds))), cancellationToken);
}
