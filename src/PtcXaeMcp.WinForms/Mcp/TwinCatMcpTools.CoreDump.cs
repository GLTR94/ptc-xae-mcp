using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_coredump_list", ReadOnly = true)]
    [Description(
        "Inventory all TwinCAT PLC core dumps for a Usermode Runtime instance/port. " +
        "File-based — no XAE or running runtime required. " +
        "Returns each dump's full path, write timestamp, size, kind " +
        "(AutoException = runtime trap; Manual = engineer-generated; CallStackOnly = no variable values), " +
        "and whether the matching .compileinfo file is present (required to interpret the dump). " +
        "Locates auto/exception and call-stack dumps under the UMR instance Boot folders; " +
        "locates manual dumps in the PLC project folder when projectFolder is supplied.")]
    public string CoreDumpList(
        [Description("UMR instance name (e.g. 'UmRT_Template'). Omit to search all registered instances.")]
        string? instanceName = null,
        [Description("ADS port number to filter by (e.g. 851). Omit to show all ports.")]
        int? port = null,
        [Description("Full path to the PLC project folder (the folder containing the .plcproj file). " +
                     "Required to find manual dumps and the _CompileInfo folder.")]
        string? projectFolder = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_coredump_list",
            new { instanceName, port, projectFolder }, () =>
            bridge.Dispatcher.Run(() =>
                bridge.Dispatcher.Session.CoreDumpList(instanceName, port, projectFolder)), cancellationToken);

    [McpServerTool(Name = "twincat_coredump_analyze", ReadOnly = true)]
    [Description(
        "Analyze a TwinCAT PLC core dump. File-based — no running runtime required. " +
        "Classifies the dump kind; computes the hang gap " +
        "(last embedded app-log timestamp vs. dump write time — a large gap means the PLC stalled, not trapped); " +
        "extracts embedded human-readable state strings (app-logger ring buffer, last state-machine steps); " +
        "lists active (unconfirmed) events by joining the offline LoggedEvents.db against CurrentEventMessages.xml. " +
        "Pass dumpPath= for a specific file, or use instanceName/port to auto-select the newest dump. " +
        "Pass projectFolder= to help locate manual dumps, the _CompileInfo, LoggedEvents.db, and CurrentEventMessages.xml.")]
    public string CoreDumpAnalyze(
        [Description("Full path to the .core file to analyze. Omit to auto-select the newest dump.")]
        string? dumpPath = null,
        [Description("UMR instance name — used to locate the newest dump when dumpPath is omitted.")]
        string? instanceName = null,
        [Description("ADS port filter — used to locate the newest dump when dumpPath is omitted.")]
        int? port = null,
        [Description("Full path to the PLC project folder (contains .plcproj, _CompileInfo, " +
                     "LoggedEvents.db, CurrentEventMessages.xml).")]
        string? projectFolder = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_coredump_analyze",
            new { dumpPath, instanceName, port, projectFolder }, () =>
            bridge.Dispatcher.Run(() =>
                bridge.Dispatcher.Session.CoreDumpAnalyze(dumpPath, instanceName, port, projectFolder)), cancellationToken);

    [McpServerTool(Name = "twincat_coredump_read_callstack", ReadOnly = true)]
    [Description(
        "GUI best-effort: read the symbol-resolved call stack (POU, instance path) from the core dump " +
        "currently loaded in the TwinCAT XAE IDE. Uses UIAutomation to find editor tab captions " +
        "that carry the '[Online]' suffix — the top-most tab is the code location of the exception. " +
        "FRAGILE: requires the dump already loaded in the IDE " +
        "(PLC → Core Dump → Load core dump — engineer loads it, v1 does NOT automate this). " +
        "Returns a precise precondition error when no dump is loaded or XAE is unreachable; " +
        "never fabricates a stack. Loading the dump into the IDE is out of scope for v1.")]
    public string CoreDumpReadCallStack(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_coredump_read_callstack", null, () =>
            bridge.Dispatcher.Run(() =>
                bridge.Dispatcher.Session.CoreDumpReadCallStack()), cancellationToken);
}
