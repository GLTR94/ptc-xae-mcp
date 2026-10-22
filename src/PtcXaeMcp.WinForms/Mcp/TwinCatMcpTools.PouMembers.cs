using PtcXaeMcp.WinForms.Security;
using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Globalization;

namespace PtcXaeMcp.WinForms;

// feature/pou-handling MCP tools: structured POU members, object lifecycle,
// PLCopen interchange. nSubType / accessor / selector behaviour was verified
// empirically against a live XAE on 2026-05-15.
internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_list_pou_members", ReadOnly = true)]
    [Description("DEPRECATED — use twincat_plc_list (kind=MEMBER, parentPath=<pouPath>). Enumerate the METHOD/ACTION/PROPERTY members of a POU by its caret-separated tree path. PROPERTY nodes also list their Get/Set accessor children. One line per member with kind and tree path.")]
    public string ListPouMembers(
        [Description("Caret-separated tree path of the POU to enumerate members of.")] string pouPath,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_list_pou_members", new { pouPath }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
            {
                var (summary, members) = new PouMembersCoordinator(bridge.Dispatcher.Session).ListPouMembers(pouPath);
                if (McpErrors.IsError(summary) || members.Count == 0)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                foreach (var m in members)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {m.Kind,-8} {m.Path}");
                return sb.ToString().TrimEnd();
            }), "twincat_plc_list (kind=MEMBER, parentPath=<pouPath>)"), cancellationToken);

    [McpServerTool(Name = "twincat_add_pou_member", ReadOnly = false, Destructive = false)]
    [Description("DEPRECATED — use twincat_plc_create (kind=MEMBER, subtype=<kind>, parentPath=<pouPath>). Add a structured member to an existing POU. kind is METHOD, ACTION, PROPERTY, GET, or SET. For METHOD/PROPERTY, returnType defaults to BOOL (a METHOD header is verified to read 'METHOD <name> : <returnType>'). implLanguage is ST, IL, SFC, FBD, CFC or LD (defaults to ST when omitted; any other value is an [Error]). GET/SET create nodes named Get/Set; name is ignored for them. PROPERTY is created together with its Get and/or Set accessor(s) (XAE does not auto-generate them) — accessors picks which: GET, SET, or BOTH (default). Use GET-only on an INTERFACE property that should be read-only, so implementers are not forced to write a Set. GET/SET add a single accessor to an existing property node. Returns the new member's tree path.")]
    public string AddPouMember(
        [Description("Caret-separated tree path of the existing POU to add the member to.")] string pouPath,
        [Description("Member kind: METHOD, ACTION, PROPERTY, GET, or SET.")] string kind,
        [Description("Name of the new member. Ignored for kind=GET/SET (nodes are always named Get/Set).")] string name,
        [Description("Return type for METHOD/PROPERTY. Defaults to BOOL when omitted.")] string? returnType = null,
        [Description("Implementation language: ST, IL, SFC, FBD, CFC, or LD. Defaults to ST when omitted; any other value is an [Error].")] string? implLanguage = null,
        [Description("For kind=PROPERTY only: GET, SET, or BOTH (default). Ignored for other kinds.")] string? accessors = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_add_pou_member",
            new { pouPath, kind, name, returnType, implLanguage, accessors }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                new PouMembersCoordinator(bridge.Dispatcher.Session).AddPouMember(pouPath, kind, name, returnType, implLanguage, accessors)), "twincat_plc_create (kind=MEMBER, subtype=<kind>, parentPath=<pouPath>)"), cancellationToken);

    [McpServerTool(Name = "twincat_read_pou_member", ReadOnly = true)]
    [Description("DEPRECATED — use twincat_plc_read (kind=MEMBER). Read a POU member's declaration and implementation by its caret-separated tree path (e.g. a METHOD, ACTION, or a PROPERTY's Get/Set accessor). A bare PROPERTY node returns only its declaration. When openInEditor=true, also opens the member (or its parent POU file) in the visible XAE editor (video mode) — best-effort, never blocks the read.")]
    public string ReadPouMember(
        [Description("Caret-separated tree path of the member to read.")] string memberPath,
        [Description("When true, also opens the member (or its parent POU file) in the visible XAE editor (video mode), best-effort. Default false.")] bool openInEditor = false,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_pou_member", new { memberPath, openInEditor }, () =>
            McpDeprecation.AppendToFirstLine(bridge.Dispatcher.Run(() =>
            {
                // video mode — open in visible XAE before reading (opt-in)
                if (openInEditor)
                {
                    try { bridge.Dispatcher.Session.OpenPlcObject(memberPath); } catch (Exception ex) when (!ComCall.IsFinal(ex)) { } // a blocked XAE fails the tool now, not a second time in the read
                }

                var (summary, body) = new PouMembersCoordinator(bridge.Dispatcher.Session).ReadPouMember(memberPath);
                if (McpErrors.IsError(summary) || body is null)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                sb.AppendLine("--- Declaration ---");
                sb.AppendLine(body.Declaration);
                sb.AppendLine("--- Implementation ---");
                sb.AppendLine(body.Implementation);
                return sb.ToString().TrimEnd();
            }), "twincat_plc_read (kind=MEMBER)"), cancellationToken);

    [McpServerTool(Name = "twincat_write_pou_member", ReadOnly = false, Destructive = true)]
    [Description("DEPRECATED — use twincat_plc_write (kind=MEMBER). Replace one or both sections of an existing ST POU member. Pass null for a section to leave it unchanged. " +
                 "Each section can be given INLINE (declaration/implementation) or READ FROM A FILE " +
                 "(declarationFile/implementationFile) — use the file form for generated content such as a large " +
                 "block table, so it never has to be retyped or re-serialised. A transcription slip in an entry " +
                 "like FbTypes[17] is syntactically valid and therefore invisible to the compiler and to st_check. " +
                 "Supplying both forms for the same section is an error. Refuses non-ST members with a structured error.")]
    public string WritePouMember(
        [Description("Caret-separated tree path of the existing ST POU member to write.")] string memberPath,
        [Description("New declaration ('VAR ... END_VAR' block). Null leaves it unchanged. Alternative to 'declarationFile'.")] string? declaration = null,
        [Description("New implementation (ST source). Null leaves it unchanged. Alternative to 'implementationFile'.")] string? implementation = null,
        [Description("Path to a file whose contents become the declaration. Alternative to 'declaration'.")] string? declarationFile = null,
        [Description("Path to a file whose contents become the implementation. Alternative to 'implementation'.")] string? implementationFile = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_write_pou_member",
            new { memberPath, declaration, implementation, declarationFile, implementationFile }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                McpPathGuard.CheckOptionalFile(declarationFile, bridge.Dispatcher.Session, "declarationFile")
                ?? McpPathGuard.CheckOptionalFile(implementationFile, bridge.Dispatcher.Session, "implementationFile")
                ?? new PouMembersCoordinator(bridge.Dispatcher.Session)
                    .WritePouMember(memberPath, declaration, implementation, declarationFile, implementationFile)), "twincat_plc_write (kind=MEMBER)"), cancellationToken);

    [McpServerTool(Name = "twincat_delete_plc_object", ReadOnly = false, Destructive = true)]
    [Description("DEPRECATED — use twincat_plc_delete (kind=<kind of the element>). Delete a POU/GVL/DUT/INTERFACE/member (METHOD, ACTION, PROPERTY, Get/Set, TRANSITION) node by its caret-separated tree path. Whitelist: the PLC project root, the PLC instance, References, folders and any node whose type cannot be read are refused with an [Error]; the target is validated before the automatic logout. Deleting an object referenced elsewhere may break the build — follow with twincat_build_plc if that matters.")]
    public string DeletePlcObject(
        [Description("Caret-separated tree path of the node to delete.")] string treePath,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_delete_plc_object", new { treePath }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                new PouMembersCoordinator(bridge.Dispatcher.Session).DeletePlcObject(treePath)), "twincat_plc_delete (kind=<kind of the element>)"), cancellationToken);

    [McpServerTool(Name = "twincat_rename_plc_object", ReadOnly = false, Destructive = true)]
    [Description("DEPRECATED — use twincat_plc_rename (kind=<kind of the element>). Rename a POU/GVL/DUT/member node by its caret-separated tree path. Returns the new tree path. TwinCAT's prompt to update the references to the old name is answered by the tool: \"Nein\" by default, \"Ja\" with updateReferences=true; the result says whether it appeared. Rebuild afterwards.")]
    public string RenamePlcObject(
        [Description("Caret-separated tree path of the node to rename.")] string treePath,
        [Description("New name for the node.")] string newName,
        [Description(UpdateReferencesHelp)] bool updateReferences = false,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_rename_plc_object",
            new { treePath, newName, updateReferences }, () =>
            McpDeprecation.Append(bridge.Dispatcher.Run(() =>
                new PouMembersCoordinator(bridge.Dispatcher.Session).RenamePlcObject(treePath, newName, updateReferences: updateReferences)), "twincat_plc_rename (kind=<kind of the element>)"), cancellationToken);

    [McpServerTool(Name = "twincat_plcopen_export", ReadOnly = false, Destructive = true)]
    [Description("Export one or more objects to a PLCopen XML file. selector is a ';'-separated list of dotted object names relative to the PLC project, e.g. \"POUs.FB_Foo;POUs.MAIN\" (a bare name without its container fails). filePath is the absolute path of the XML file to write.")]
    public string PlcOpenExport(
        [Description("Absolute path of the PLCopen XML file to write.")] string filePath,
        [Description("';'-separated list of dotted object names relative to the PLC project, e.g. \"POUs.FB_Foo;POUs.MAIN\" (a bare name without its container fails).")] string selector,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_plcopen_export",
            new { filePath, selector }, () =>
            bridge.Dispatcher.Run(() =>
                McpPathGuard.CheckRequiredFile(filePath, bridge.Dispatcher.Session, "filePath")
                ?? new PouMembersCoordinator(bridge.Dispatcher.Session).PlcOpenExport(filePath, selector)), cancellationToken);

    [McpServerTool(Name = "twincat_plcopen_import", ReadOnly = false, Destructive = true)]
    [Description("Import objects from a PLCopen XML file. conflictMode is None, Rename, Replace, or Skip and defaults to Skip (non-destructive). filePath is the absolute path of the XML file to read.")]
    public string PlcOpenImport(
        [Description("Absolute path of the PLCopen XML file to read.")] string filePath,
        [Description("Conflict resolution: None, Rename, Replace, or Skip. Defaults to Skip (non-destructive).")] string? conflictMode = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_plcopen_import",
            new { filePath, conflictMode }, () =>
            bridge.Dispatcher.Run(() =>
                McpPathGuard.CheckRequiredFile(filePath, bridge.Dispatcher.Session, "filePath")
                ?? new PouMembersCoordinator(bridge.Dispatcher.Session).PlcOpenImport(filePath, conflictMode)), cancellationToken);
}
