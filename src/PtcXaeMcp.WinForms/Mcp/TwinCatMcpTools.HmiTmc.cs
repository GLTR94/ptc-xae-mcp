using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

// hmi-ads-tmc — attach a PLC project's TMC file to a TwinCAT HMI project so its symbols are
// known with NO PLC connected. The handle is the HMI project
// name, as in hmi-server; the second key is the virtual PLC name the file is attached under.
internal sealed partial class TwinCatMcpTools
{
    private const string HmiVirtualPlcParam =
        "Virtual PLC name the TMC file is attached under — the root of its symbol paths in the HMI " +
        "engineering. twincat_hmi_list_tmc_files reports it.";

    [McpServerTool(Name = "twincat_hmi_list_tmc_files", ReadOnly = true)]
    [Description("List the TMC files attached to a TwinCAT HMI project — the PLC symbol descriptions the HMI " +
                 "engineering can use WITHOUT a PLC connection. Read from the project's HmiTmcFiles property, " +
                 "so it reflects what was saved. Per entry: virtualPlcName (rootNamespace), the stored path " +
                 "(relative to the solution directory when the file lies below it), plcName, the resolved " +
                 "absolute path and whether that file still exists. Also reports, separately, which ADS " +
                 "runtimes serve symbols from a TMC file server-side (USE_TMC in the ADS extension config) — " +
                 "a different mechanism with the same purpose. Read-only. twincat_hmi_attach_tmc_file only " +
                 "reads a .tmc under an allowed root: the open solution's directory, %TEMP%, " +
                 @"%LOCALAPPDATA%\ptc-xae-mcp, the TwinCAT installation, or a root listed in the host's " +
                 "PTC_XAE_MCP_ALLOWED_PATHS environment variable (';'-separated). " +
                 "Result: one-line summary + indented JSON { hmiProject, count, attachments[], adsRuntimesUsingTmc[] }.")]
    public string HmiListTmcFiles(
        [Description(HmiProjectParam)] string? hmiProject = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_list_tmc_files", new { hmiProject }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session).ListTmcFiles(hmiProject))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_list_ads_runtimes", ReadOnly = true)]
    [Description("List the ADS runtimes of a TwinCAT HMI project's engineering server, read live from the " +
                 "server symbol 'ADS.Config::RUNTIMES'. Per runtime: name (e.g. PLC1), enabled, netId, port, " +
                 "readOnly, useWhitelisting, symbolCount and useTmc plus tmcFile (server-side TMC: the " +
                 "extension takes its symbol description from a converted binary instead of uploading it over " +
                 "ADS). Use it to tell 'this runtime's PLC is unreachable' apart from 'this runtime needs no " +
                 "PLC', and to get the NetId/port a failing binding actually points at. Switch a runtime to a " +
                 "TMC file with twincat_hmi_enable_runtime_tmc. Read-only. " +
                 "Result: one-line summary + indented JSON { hmiProject, count, usingTmc, runtimes[] }.")]
    public string HmiListAdsRuntimes(
        [Description(HmiProjectParam)] string? hmiProject = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_list_ads_runtimes", new { hmiProject }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session).ListAdsRuntimes(hmiProject))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_list_plc_tmc_candidates", ReadOnly = true)]
    [Description("List the TMC file of every PLC project in the open solution — the CANDIDATES for " +
                 "twincat_hmi_attach_tmc_file, found the way TE2000's 'Browse solution for TMC file' dialog " +
                 "does: each TwinCAT project's TIPC children, their PlcProjectDef/RelativeTmcPath, resolved " +
                 "against the directory of the .tsproj. Per candidate: tcProject, plcProject, relativeTmcPath, " +
                 "absolutePath, exists, lastWriteUtc and symbolCount (the <Symbol> entries of the file; null when " +
                 "it was not opened — missing, or outside the roots twincat_hmi_attach_tmc_file may read) plus a " +
                 "note saying why a field is empty. Needs no HMI project. Read-only. It does NOT choose: which " +
                 "PLC project belongs to which HMI is a project decision, so pass one absolutePath explicitly as " +
                 "tmcFile. Result: one-line summary + indented JSON { count, existing, candidates[] }.")]
    public string HmiListPlcTmcCandidates(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_list_plc_tmc_candidates", new { }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session).ListPlcTmcCandidates())), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_attach_tmc_file", ReadOnly = false, Destructive = false)]
    [Description("Attach a PLC project's TMC file to a TwinCAT HMI project, so the HMI engineering knows the " +
                 "PLC symbols WITHOUT ANY PLC CONNECTION (the TE2000 'Attach TMC file' function; the symbols " +
                 "are then browsable and bindable offline). tmcFile is the absolute path of the file the XAE " +
                 @"generates for a PLC project, '<PlcProject>\<PlcProject>.tmc' — NOT the HMI_Error/HMI_Info/" +
                 "HMI_Warning.tmc event classes in a project root, which carry no PLC symbols. The file is " +
                 "opened and identified first: it must be a <TcModuleClass> document with at least one " +
                 "<Symbol>; its symbol count and size are shown in the confirmation popup. virtualPlcName " +
                 "defaults to the file name without extension, exactly as the TE2000 dialog derives it. " +
                 "tmcFile must lie under an allowed root — the open solution's directory, %TEMP%, " +
                 @"%LOCALAPPDATA%\ptc-xae-mcp, the TwinCAT installation, or a root listed in the host's " +
                 "PTC_XAE_MCP_ALLOWED_PATHS environment variable (';'-separated); any other path is refused " +
                 "with [Error] before the file is opened. " +
                 ConfirmNote +
                 " Refuses a file that is already attached and a virtual PLC name already in use, because " +
                 "TE2000 answers those with a modal warning box in the XAE that no agent can dismiss. Success " +
                 "only after the project's HmiTmcFiles property reads the new entry back — the Automation " +
                 "Interface itself reports true even when it failed. Reversible with " +
                 "twincat_hmi_detach_tmc_file. Result: one-line summary + indented JSON.")]
    public string HmiAttachTmcFile(
        [Description(@"Absolute path of the PLC project's .tmc file. The XAE writes it next to the PLC project as <PlcProject>\<PlcProject>.tmc. It must lie under the open solution's directory, %TEMP%, %LOCALAPPDATA%\ptc-xae-mcp, the TwinCAT installation or a root in PTC_XAE_MCP_ALLOWED_PATHS.")]
        string tmcFile,
        [Description("Virtual PLC name to attach it under — letters, digits, '_' or '-', max 64 characters. " +
                     "Default: the .tmc file name without extension.")]
        string? virtualPlcName = null,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_attach_tmc_file",
            new { hmiProject, tmcFile, virtualPlcName, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .AttachTmcFile(hmiProject, tmcFile, virtualPlcName, confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_detach_tmc_file", ReadOnly = false, Destructive = true)]
    [Description("Remove an attached TMC file from a TwinCAT HMI project by its virtual PLC name — its symbols " +
                 "are then no longer known offline. " + ConfirmNote +
                 " A name that is not attached returns 'Nothing to do' without a popup. Success only after the " +
                 "project's HmiTmcFiles property no longer holds the entry — the Automation Interface returns " +
                 "false here even on success, so only the read-back counts. " +
                 "Result: one-line summary + indented JSON.")]
    public string HmiDetachTmcFile(
        [Description(HmiVirtualPlcParam)] string virtualPlcName,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_detach_tmc_file",
            new { hmiProject, virtualPlcName, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .DetachTmcFile(hmiProject, virtualPlcName, confirmTimeoutSeconds))), cancellationToken);

    // #161 — the SERVER-side mechanism: the ADS extension serves a runtime's symbols from the TMC.

    private const string HmiAdsRuntimeParam =
        "Name of the ADS runtime of the HMI server — a key under ADS.Config::RUNTIMES, e.g. 'PLC1' " +
        "(case-sensitive; twincat_hmi_list_ads_runtimes lists them). It must already exist.";

    [McpServerTool(Name = "twincat_hmi_enable_runtime_tmc", ReadOnly = false, Destructive = true)]
    [Description("Make an ADS runtime of a TwinCAT HMI project's engineering server serve its PLC symbols from " +
                 "the PLC project's TMC file instead of uploading them from a PLC — server-side USE_TMC, so " +
                 "mapping, reading and binding PLC symbols work WITHOUT ANY PLC (unlike " +
                 "twincat_hmi_attach_tmc_file, which only feeds the engineering: the server has no 'TMC' domain). " +
                 "What TE2000 does for a new project, done on demand: the .tmc is converted with TwinCAT's own " +
                 "type system (TcTypeSystem.dll, in-process) into the binary ADS symbol segment " +
                 @"'<runtime>_<tmc>.DATA_UPLOADINFO2.bin', written to 'Tmc\' below the engineering server's " +
                 @"working directory (<solution>\.engineering_servers\<project>, created when the server starts), " +
                 "then ADS.Config::RUNTIMES::<runtime>::TMC::DATA_SYMBOLUPANDDOWNLOAD is set to that relative path " +
                 "and ::USE_TMC to true. tmcFile is the PLC project's '<PlcProject>.tmc' (NOT the HMI_Error/Info/" +
                 "Warning.tmc event classes) under an allowed root — the open solution's directory, %TEMP%, " +
                 @"%LOCALAPPDATA%\ptc-xae-mcp, the TwinCAT installation, or a root in the host's " +
                 "PTC_XAE_MCP_ALLOWED_PATHS (';'-separated); any other path is refused before the file is opened. " +
                 "The file is identified and converted BEFORE the popup, so a file TwinCAT cannot read never reaches " +
                 "it. " + ConfirmNote +
                 " Success only after ADS.Config::RUNTIMES reads back USE_TMC=true with the written path — whether the " +
                 "ADS extension already serves the symbols is not checked (read one with twincat_hmi_read_symbols). " +
                 "Re-running it regenerates the file from the current .tmc. Reversible with " +
                 "twincat_hmi_disable_runtime_tmc. NOT yet verified against a live HMI server (#161). " +
                 "Result: one-line summary + indented JSON.")]
    public string HmiEnableRuntimeTmc(
        [Description(HmiAdsRuntimeParam)] string runtime,
        [Description(@"Absolute path of the PLC project's .tmc file (the XAE writes it as <PlcProject>\<PlcProject>.tmc; twincat_hmi_list_plc_tmc_candidates lists them). It must lie under the open solution's directory, %TEMP%, %LOCALAPPDATA%\ptc-xae-mcp, the TwinCAT installation or a root in PTC_XAE_MCP_ALLOWED_PATHS.")]
        string tmcFile,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_enable_runtime_tmc",
            new { hmiProject, runtime, tmcFile, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .EnableRuntimeTmc(hmiProject, runtime, tmcFile, confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_disable_runtime_tmc", ReadOnly = false, Destructive = true)]
    [Description("Switch an ADS runtime of a TwinCAT HMI project's engineering server back from a TMC file to " +
                 "the ADS upload from the PLC: DATA_SYMBOLUPANDDOWNLOAD = \"\" then USE_TMC = false, exactly as " +
                 "TE2000's own detach. Without a reachable PLC the runtime then has no symbols again. A runtime " +
                 "that uses no TMC file returns 'Nothing to do' without a popup. " + ConfirmNote +
                 " Success only after ADS.Config::RUNTIMES reads back USE_TMC=false without a file. The converted " +
                 ".bin is left on disk (engineering server scratch folder). NOT yet verified against a live HMI " +
                 "server (#161). Result: one-line summary + indented JSON.")]
    public string HmiDisableRuntimeTmc(
        [Description(HmiAdsRuntimeParam)] string runtime,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_disable_runtime_tmc",
            new { hmiProject, runtime, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .DisableRuntimeTmc(hmiProject, runtime, confirmTimeoutSeconds))), cancellationToken);
}
