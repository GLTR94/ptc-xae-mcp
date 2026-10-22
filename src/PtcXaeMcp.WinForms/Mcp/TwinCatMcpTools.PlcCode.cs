using PtcXaeMcp.WinForms.Security;
using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Globalization;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_list_pous", ReadOnly = true)]
    [Description("DEPRECATED — use twincat_plc_list. Enumerate POU/GVL/DUT items in the open PLC project. Returns one line per item with kind (POU|GVL|DUT|FOLDER), tree path, and (for POUs) the implementation language.")]
    public string ListPous(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_list_pous", null, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
            {
                var (summary, entries) = new PlcCodeCoordinator(bridge.Dispatcher.Session).ListPous();
                if (McpErrors.IsError(summary) || entries.Count == 0)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                foreach (var e in entries)
                {
                    string lang = string.IsNullOrEmpty(e.Language) ? "" : $" [{e.Language}]";
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Kind,-6} {e.Path}{lang}");
                }
                return sb.ToString().TrimEnd();
            }), "twincat_plc_list"), cancellationToken);

    [McpServerTool(Name = "twincat_read_pou", ReadOnly = true)]
    [Description("DEPRECATED — use twincat_plc_read (kind=POU or INTERFACE). Read a POU's declaration and implementation by its caret-separated tree path (e.g. 'TIPC^PLC^PLC Projekt^POUs^01_Application^MAIN'). Returns the language and both sections. When openInEditor=true, also opens the POU in the visible XAE editor (video mode) — best-effort, never blocks the read.")]
    public string ReadPou(
        [Description("Caret-separated tree path of the POU to read, e.g. 'TIPC^PLC^PLC Projekt^POUs^01_Application^MAIN'.")] string treePath,
        [Description("When true, also opens the POU in the visible XAE editor (video mode), best-effort. Default false.")] bool openInEditor = false,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_pou", new { treePath, openInEditor }, () =>
            McpDeprecation.AppendToFirstLine(bridge.Dispatcher.Run(() =>
            {
                // video mode — open in visible XAE before reading (opt-in)
                if (openInEditor)
                {
                    try { bridge.Dispatcher.Session.OpenPlcObject(treePath); } catch (Exception ex) when (!ComCall.IsFinal(ex)) { } // a blocked XAE fails the tool now, not a second time in the read
                }

                var (summary, body) = new PlcCodeCoordinator(bridge.Dispatcher.Session).ReadPou(treePath);
                if (McpErrors.IsError(summary) || body is null)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                sb.AppendLine("--- Declaration ---");
                sb.AppendLine(body.Declaration);
                sb.AppendLine("--- Implementation ---");
                sb.AppendLine(body.Implementation);
                return sb.ToString().TrimEnd();
            }), "twincat_plc_read (kind=POU or INTERFACE)"), cancellationToken);

    [McpServerTool(Name = "twincat_read_gvl", ReadOnly = true)]
    [Description("DEPRECATED — use twincat_plc_read (kind=GVL or DUT). Read a GVL or DUT declaration by its caret-separated tree path. Returns the declaration block (GVLs and DUTs have no implementation section). When openInEditor=true, also opens the object in the visible XAE editor (video mode) — best-effort, never blocks the read.")]
    public string ReadGvl(
        [Description("Caret-separated tree path of the GVL or DUT to read.")] string treePath,
        [Description("When true, also opens the object in the visible XAE editor (video mode), best-effort. Default false.")] bool openInEditor = false,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_gvl", new { treePath, openInEditor }, () =>
            McpDeprecation.AppendToFirstLine(bridge.Dispatcher.Run(() =>
            {
                // video mode — open in visible XAE before reading (opt-in)
                if (openInEditor)
                {
                    try { bridge.Dispatcher.Session.OpenPlcObject(treePath); } catch (Exception ex) when (!ComCall.IsFinal(ex)) { } // a blocked XAE fails the tool now, not a second time in the read
                }

                var (summary, decl) = new PlcCodeCoordinator(bridge.Dispatcher.Session).ReadGvl(treePath);
                if (McpErrors.IsError(summary) || decl is null)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                sb.AppendLine("--- Declaration ---");
                sb.AppendLine(decl);
                return sb.ToString().TrimEnd();
            }), "twincat_plc_read (kind=GVL or DUT)"), cancellationToken);

    [McpServerTool(Name = "twincat_write_pou_body", ReadOnly = false, Destructive = true)]
    [Description("DEPRECATED — use twincat_plc_write (kind=POU or INTERFACE). Replace one or both sections of an existing ST POU. Pass null for a section to leave it unchanged. " +
                 "Each section can be given INLINE (declaration/implementation) or READ FROM A FILE " +
                 "(declarationFile/implementationFile) — use the file form for generated content, so it never has to " +
                 "be retyped. Supplying both forms for the same section is an error. " +
                 "Refuses non-ST POUs and GVLs/DUTs with a structured error.")]
    public string WritePouBody(
        [Description("Caret-separated tree path of the existing ST POU to write.")] string treePath,
        [Description("New declaration ('VAR ... END_VAR' block). Null leaves it unchanged. Alternative to 'declarationFile'.")] string? declaration = null,
        [Description("New implementation (ST source). Null leaves it unchanged. Alternative to 'implementationFile'.")] string? implementation = null,
        [Description("Path to a file whose contents become the declaration. Alternative to 'declaration'.")] string? declarationFile = null,
        [Description("Path to a file whose contents become the implementation. Alternative to 'implementation'.")] string? implementationFile = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_write_pou_body",
            new { treePath, declaration, implementation, declarationFile, implementationFile }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                McpPathGuard.CheckOptionalFile(declarationFile, bridge.Dispatcher.Session, "declarationFile")
                ?? McpPathGuard.CheckOptionalFile(implementationFile, bridge.Dispatcher.Session, "implementationFile")
                ?? new PlcCodeCoordinator(bridge.Dispatcher.Session)
                    .WritePouBody(treePath, declaration, implementation, declarationFile, implementationFile)), "twincat_plc_write (kind=POU or INTERFACE)"), cancellationToken);

    [McpServerTool(Name = "twincat_write_gvl", ReadOnly = false, Destructive = true)]
    [Description("DEPRECATED — use twincat_plc_write (kind=GVL or DUT). Replace the declaration of an existing GVL or DUT by its caret-separated tree path. These declaration-only objects have no implementation section, so only the declaration is written (ITcPlcDeclaration); the write is verified by reading back, and any mismatch throws. " +
                 "The declaration can be given inline or READ FROM A FILE via declarationFile — use the file form for generated content. " +
                 "Use twincat_write_pou_body for ST POUs (which also carry an implementation).")]
    public string WriteGvl(
        [Description("Caret-separated tree path of the existing GVL or DUT to write.")] string treePath,
        [Description("New declaration block. Null leaves it unchanged. Alternative to 'declarationFile'.")] string? declaration = null,
        [Description("Path to a file whose contents become the declaration. Alternative to 'declaration'.")] string? declarationFile = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_write_gvl",
            new { treePath, declaration, declarationFile }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                McpPathGuard.CheckOptionalFile(declarationFile, bridge.Dispatcher.Session, "declarationFile")
                ?? new PlcCodeCoordinator(bridge.Dispatcher.Session).WriteGvl(treePath, declaration, declarationFile)), "twincat_plc_write (kind=GVL or DUT)"), cancellationToken);

    [McpServerTool(Name = "twincat_create_pou", ReadOnly = false, Destructive = false)]
    [Description("DEPRECATED — use twincat_plc_create (kind=POU, subtype=<pouType>). Create a new POU under an existing PLC folder. pouType is one of PROGRAM, FUNCTION_BLOCK, FUNCTION. returnType is required for FUNCTION (defaults to BOOL). implLanguage is ST, IL, SFC, FBD, CFC or LD (defaults to ST when omitted; any other value is an [Error]). parentPath is the caret-separated tree path of the folder (e.g. 'TIPC^PLC^PLC Projekt^POUs^01_Application'). Returns the new node's tree path.")]
    public string CreatePou(
        [Description("Caret-separated tree path of the existing folder to create the POU under, e.g. 'TIPC^PLC^PLC Projekt^POUs^01_Application'.")] string parentPath,
        [Description("Name of the new POU.")] string name,
        [Description("POU kind: PROGRAM, FUNCTION_BLOCK, or FUNCTION.")] string pouType,
        [Description("Return type, required for FUNCTION. Defaults to BOOL when omitted for a FUNCTION.")] string? returnType = null,
        [Description("Implementation language: ST, IL, SFC, FBD, CFC, or LD. Defaults to ST when omitted; any other value is an [Error].")] string? implLanguage = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_pou",
            new { parentPath, name, pouType, returnType, implLanguage }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                new PlcCodeCoordinator(bridge.Dispatcher.Session).CreatePou(parentPath, name, pouType, returnType, implLanguage)), "twincat_plc_create (kind=POU, subtype=<pouType>)"), cancellationToken);

    [McpServerTool(Name = "twincat_create_dut", ReadOnly = false, Destructive = false)]
    [Description("DEPRECATED — use twincat_plc_create (kind=DUT, subtype=<dutType>). Create a new DUT (Data Unit Type) under an existing PLC folder or the PLC project root. dutType is one of STRUCT, ENUM, UNION, ALIAS (defaults to STRUCT). Pass an optional declaration (the full 'TYPE ... END_TYPE' block) to populate it; if omitted, TwinCAT's default skeleton remains. ALIAS is an exception: it REQUIRES a declaration of the form 'TYPE <name> : <baseType>; END_TYPE' (e.g. 'TYPE A_Handle : UDINT; END_TYPE'), because the base type must be supplied when the alias is created. parentPath is the caret-separated tree path of the folder (e.g. 'TIPC^PLC^PLC Projekt^DUTs'). DUTs cannot be created under a POU/GVL/DUT leaf — only a folder or the PLC project node. Returns the new node's tree path.")]
    public string CreateDut(
        [Description("Caret-separated tree path of the existing folder or PLC project node to create the DUT under, e.g. 'TIPC^PLC^PLC Projekt^DUTs'.")] string parentPath,
        [Description("Name of the new DUT.")] string name,
        [Description("DUT kind: STRUCT, ENUM, UNION, or ALIAS. Defaults to STRUCT. ALIAS requires 'declaration'.")] string? dutType = null,
        [Description("Full 'TYPE ... END_TYPE' block to populate the DUT. Required for ALIAS (e.g. 'TYPE A_Handle : UDINT; END_TYPE'); optional otherwise (TwinCAT's default skeleton is used when omitted).")] string? declaration = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_dut",
            new { parentPath, name, dutType, declaration }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                new PlcCodeCoordinator(bridge.Dispatcher.Session).CreateDut(parentPath, name, dutType, declaration)), "twincat_plc_create (kind=DUT, subtype=<dutType>)"), cancellationToken);

    [McpServerTool(Name = "twincat_create_gvl", ReadOnly = false, Destructive = false)]
    [Description("DEPRECATED — use twincat_plc_create (kind=GVL). Create a new GVL (Global Variable List) under an existing PLC folder or the PLC project root. Set persistent=true to get persistent variables: an ordinary GVL whose declaration has a 'VAR_GLOBAL PERSISTENT' section (persistent is a section, not an object type). Pass an optional declaration (the full 'VAR_GLOBAL ... END_VAR' block; for persistent=true it must contain the PERSISTENT keyword, otherwise [Error]) to populate it; if omitted, TwinCAT's default skeleton remains (for persistent=true an empty 'VAR_GLOBAL PERSISTENT ... END_VAR'). parentPath is the caret-separated tree path of the folder (e.g. 'TIPC^PLC^PLC Projekt^GVLs'). GVLs cannot be created under a POU/GVL/DUT leaf — only a folder or the PLC project node. Returns the new node's tree path.")]
    public string CreateGvl(
        [Description("Caret-separated tree path of the existing folder or PLC project node to create the GVL under, e.g. 'TIPC^PLC^PLC Projekt^GVLs'.")] string parentPath,
        [Description("Name of the new GVL.")] string name,
        [Description("True creates an ordinary GVL whose declaration has a 'VAR_GLOBAL PERSISTENT ... END_VAR' section (persistent variables are a section of a GVL, not an object type). A given declaration must contain the PERSISTENT keyword, otherwise [Error]. Default false.")] bool persistent = false,
        [Description("Full 'VAR_GLOBAL ... END_VAR' (or PERSISTENT variant) block to populate the GVL. TwinCAT's default skeleton is used when omitted.")] string? declaration = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_gvl",
            new { parentPath, name, persistent, declaration }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                new PlcCodeCoordinator(bridge.Dispatcher.Session).CreateGvl(parentPath, name, persistent, declaration)), "twincat_plc_create (kind=GVL)"), cancellationToken);

    [McpServerTool(Name = "twincat_create_interface", ReadOnly = false, Destructive = false)]
    [Description("DEPRECATED — use twincat_plc_create (kind=INTERFACE). Create a new INTERFACE under an existing PLC folder or the PLC project root. Pass an optional declaration (the full 'INTERFACE ... END_INTERFACE' block) to populate it; if omitted, TwinCAT's default skeleton remains. Interface methods are added afterwards with twincat_add_pou_member (kind=METHOD). parentPath is the caret-separated tree path of the folder (e.g. 'TIPC^PLC^PLC Projekt^POUs^04_Components'). Interfaces cannot be created under a POU/GVL/DUT leaf — only a folder or the PLC project node. Returns the new node's tree path.")]
    public string CreateInterface(
        [Description("Caret-separated tree path of the existing folder or PLC project node to create the interface under, e.g. 'TIPC^PLC^PLC Projekt^POUs^04_Components'.")] string parentPath,
        [Description("Name of the new interface.")] string name,
        [Description("Full 'INTERFACE ... END_INTERFACE' block to populate it. TwinCAT's default skeleton is used when omitted.")] string? declaration = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_interface",
            new { parentPath, name, declaration }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                new PlcCodeCoordinator(bridge.Dispatcher.Session).CreateInterface(parentPath, name, declaration)), "twincat_plc_create (kind=INTERFACE)"), cancellationToken);
}
