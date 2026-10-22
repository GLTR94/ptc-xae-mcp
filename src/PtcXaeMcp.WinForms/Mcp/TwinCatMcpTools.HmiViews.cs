using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

// hmi-views — create and change files of a TwinCAT HMI project (user controls, contents,
// functions, imported documents) via the HMI Automation Interface.
// Paths are project-relative with '/', e.g. 'UserControls/PtcCamCurve/PtcCamCurve.usercontrol'.
internal sealed partial class TwinCatMcpTools
{
    private const string HmiPathParam =
        "Project-relative path with '/' separators, e.g. 'UserControls/PtcCamCurve/PtcCamCurve.usercontrol' " +
        "or 'Functions/PtcCamCurve.js'. No drive, no leading '/', no '..'.";

    [McpServerTool(Name = "twincat_hmi_read_file", ReadOnly = true)]
    [Description("Read the source text of a file of a TwinCAT HMI project (view, content, user control, " +
                 ".usercontrol.json, function .js/.function.json, CSS …) via ITcHmiFile.GetSource. Read-only. " +
                 "Result: one-line summary + indented JSON { path, length, lines, source }.")]
    public string HmiReadFile(
        [Description(HmiPathParam)] string path,
        [Description(HmiProjectParam)] string? hmiProject = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_read_file", new { hmiProject, path }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session).ReadFile(hmiProject, path))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_add_item", ReadOnly = false, Destructive = false)]
    [Description("Create a new file in a TwinCAT HMI project from a TE2000 item template (ITcHmiItem.AddItem), " +
                 "creating missing folders. kind: view, content, usercontrol (creates .usercontrol + .usercontrol.json), " +
                 "function_js / function_ts (creates .js/.ts + .function.json, namespace = project name), codebehind_js, " +
                 "codebehind_ts, css, html, js, json, ts, text (.txt), xml. html, view and text are created by importing " +
                 "a correctly named file (empty; view = the vendor view skeleton) because their TE2000 templates misname " +
                 "the file (x.html.html, x.view.view, x.text.txt). Refuses when the file exists. " +
                 ConfirmNote + " Result: one-line summary + indented JSON (created paths).")]
    public string HmiAddItem(
        [Description("Template kind, see description.")] string kind,
        [Description("Target folder, project-relative with '/', e.g. 'UserControls/PtcCamCurve'. The parameter must be " +
                     "passed, but its value may be null or empty — both mean the project root.")]
        string? folder,
        [Description("File name without folder; the template's extension is added, e.g. 'PtcCamCurve'.")] string name,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_add_item", new { hmiProject, kind, folder, name, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .AddItem(hmiProject, folder, name, kind, confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_write_file", ReadOnly = false, Destructive = true)]
    [Description("Replace the source text of an EXISTING file of a TwinCAT HMI project (ITcHmiFile.SetSource), then " +
                 "save and verify by read-back. Pass the text as 'source' or an absolute 'sourceFile' (max 5 MB). For JSON " +
                 "files the project's own \"$schema\" value is kept (keepSchema, default true) because it points into the " +
                 "project's Packages folder. Identical content → 'Nothing to do' without a popup. " + ConfirmNote +
                 " Result: one-line summary + indented JSON.")]
    public string HmiWriteFile(
        [Description(HmiPathParam)] string path,
        [Description("The complete new file content. Alternative to sourceFile.")] string? source = null,
        [Description("Absolute path of a local file whose content is written. Alternative to source.")] string? sourceFile = null,
        [Description("Keep the existing \"$schema\" value of a JSON file. Default true.")] bool keepSchema = true,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_write_file",
            new { hmiProject, path, sourceLength = source?.Length, sourceFile, keepSchema, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .WriteFile(hmiProject, path, source, sourceFile, keepSchema, confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_import_file", ReadOnly = false, Destructive = false)]
    [Description("Copy an external file (e.g. an HTML document, image, PDF, JavaScript library) into a TwinCAT HMI project " +
                 "(ITcHmiItem.AddExistingItem), creating the folder when missing. Refuses when the target exists. " +
                 "When TE2000 raises an error although the file was added (seen for .js), the import counts only if the " +
                 "copy on disk is byte-identical (engineError, verifiedOnDisk). For .js/.css the result says how " +
                 "Properties/tchmiconfig.json lists it in dependencyFiles (dependencyType); run twincat_hmi_build afterwards " +
                 "so the live view loads it. " +
                 ConfirmNote + " Result: one-line summary + indented JSON.")]
    public string HmiImportFile(
        [Description("Absolute path of the file to import.")] string sourcePath,
        [Description("Target folder, project-relative with '/'. Empty = project root.")] string? folder = null,
        [Description("Target file name. Default: the source file name.")] string? name = null,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_import_file", new { hmiProject, sourcePath, folder, name, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .ImportFile(hmiProject, sourcePath, folder, name, confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_build", ReadOnly = false, Destructive = false)]
    [Description("Build a TwinCAT HMI project (ITcHmiProject.Build) so new user controls, functions and contents reach " +
                 "the engineering server's live view. Changes no source file (no popup). On failure see " +
                 "twincat_get_error_list. Result: one-line summary + indented JSON.")]
    public string HmiBuild(
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description("Solution configuration. Default 'Debug'.")] string? configuration = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_build", new { hmiProject, configuration }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session).Build(hmiProject, configuration))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_apply_bundle", ReadOnly = false, Destructive = true)]
    [Description("Create/write a whole package of HMI files from a manifest with ONE engineer confirmation — e.g. a user " +
                 "control with its .usercontrol.json and function. Manifest (JSON): { \"name\": \"…\", \"entries\": " +
                 "[ { \"path\": \"UserControls/X/X.usercontrol\", \"kind\": \"usercontrol\", \"source\": \"X.usercontrol\" }, " +
                 "{ \"path\": \"UserControls/X/X.usercontrol.json\", \"source\": \"X.usercontrol.json\" } ] } — 'source' " +
                 "relative to the manifest; existing files are written, missing ones created from 'kind' first; a " +
                 "dependent file (.usercontrol.json, .function.json) needs no kind when an earlier entry's template creates " +
                 "it. JSON files keep the project's \"$schema\". Everything is validated before the popup; afterwards the " +
                 "entries run in order and stop at the first error (reporting what was already written). " + ConfirmNote +
                 " Result: one-line summary + indented JSON.")]
    public string HmiApplyBundle(
        [Description("Absolute path of the bundle manifest (JSON).")] string manifestPath,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_apply_bundle", new { hmiProject, manifestPath, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .ApplyBundle(hmiProject, manifestPath, confirmTimeoutSeconds))), cancellationToken);

    [McpServerTool(Name = "twincat_hmi_delete_item", ReadOnly = false, Destructive = true)]
    [Description("Delete a file or folder of a TwinCAT HMI project (ITcHmiItem.DeleteChild) — removed from the project AND " +
                 "from the disk, no recycle bin. Refused without a popup: the project infrastructure (Server, Properties, " +
                 "Packages), the startup view and folders containing it; a missing item is reported without a popup. The " +
                 "popup shows file or folder and how many items lie below it. " + ConfirmNote + " Success only when the " +
                 "item is gone afterwards. Result: one-line summary + indented JSON.")]
    public string HmiDeleteItem(
        [Description(HmiPathParam)] string path,
        [Description(HmiProjectParam)] string? hmiProject = null,
        [Description(ConfirmTimeoutParam)] int confirmTimeoutSeconds = HmiCoordinator.DefaultConfirmTimeoutSeconds,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_hmi_delete_item", new { hmiProject, path, confirmTimeoutSeconds }, () =>
            bridge.Dispatcher.Run(() =>
                HmiResult(new HmiCoordinator(bridge.Dispatcher.Session, host.HmiConfirmer)
                    .DeleteItem(hmiProject, path, confirmTimeoutSeconds))), cancellationToken);
}
