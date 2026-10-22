using PtcXaeMcp.WinForms.TwinCat;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Globalization;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    // twincat_dump_nc_tree moved to TwinCatMcpToolsDiagnostics (Mcp/TwinCatMcpTools.Diagnostics.cs) —
    // a research/discovery diagnostic, registered only with --diagnostics / PTC_XAE_MCP_DIAGNOSTICS=1.

    [McpServerTool(Name = "twincat_list_nc_tasks_axes", ReadOnly = true)]
    [Description("Enumerate the NC (Motion) tree: every NC-Task and the axes under it. " +
                 "Read-only. Requires XAE running with a solution open. Returns a summary " +
                 "line followed by one indented line per NC-Task and axis with their tree paths " +
                 "(use those paths with twincat_create_axis / twincat_configure_axis).")]
    public string ListNcTasksAxes(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_list_nc_tasks_axes", null, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, tasks) = new NcAxesCoordinator(bridge.Dispatcher.Session).ListNcTasksAxes();
                if (McpErrors.IsError(summary) || tasks.Count == 0)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                foreach (var t in tasks)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  NC-Task  {t.Path}");
                    foreach (var a in t.Axes)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    AXIS   {a.Path}");
                }
                return sb.ToString().TrimEnd();
            }), cancellationToken);

    [McpServerTool(Name = "twincat_create_nc_task", ReadOnly = false, Destructive = false)]
    [Description("Create a new NC-Task under the Motion (TINC) root. Requires XAE running with a " +
                 "solution open. NOTE (TC3 4026.22): the Automation Interface only creates an " +
                 "NC-Task when the project has none yet (green-field). If an NC-Task already " +
                 "exists, this returns guidance to reuse it via twincat_list_nc_tasks_axes + " +
                 "twincat_create_axis. The actual created tree path is read back and returned. " +
                 "NOT saved to disk automatically — call twincat_save_project to persist.")]
    public string CreateNcTask(
        [Description("Name for the new NC-Task, e.g. 'NC-Task 1'.")] string name,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_nc_task", new { name }, () =>
            bridge.Dispatcher.Run(() =>
                new NcAxesCoordinator(bridge.Dispatcher.Session).CreateNcTask(name)), cancellationToken);

    [McpServerTool(Name = "twincat_create_axis", ReadOnly = false, Destructive = false)]
    [Description("Create an axis under an existing NC-Task. Requires XAE running with a solution " +
                 "open. The NC-Task is resolved by browsing (not by reconstructing a path); get a " +
                 "valid ncTaskPath from twincat_list_nc_tasks_axes. The actual created axis path " +
                 "is read back and returned. NOT saved to disk automatically — call " +
                 "twincat_save_project to persist.")]
    public string CreateAxis(
        [Description("Tree path of the target NC-Task, from twincat_list_nc_tasks_axes.")] string ncTaskPath,
        [Description("Name for the new axis, e.g. 'Axis 1'.")] string axisName,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_create_axis", new { ncTaskPath, axisName }, () =>
            bridge.Dispatcher.Run(() =>
                new NcAxesCoordinator(bridge.Dispatcher.Session).CreateAxis(ncTaskPath, axisName)), cancellationToken);

    [McpServerTool(Name = "twincat_configure_axis", ReadOnly = false, Destructive = true)]
    [Description("Configure parameters of an existing axis via a ProduceXml→modify→ConsumeXml " +
                 "round-trip (unspecified parameters are preserved). Requires XAE running with a " +
                 "solution open. Resolve axisPath via twincat_list_nc_tasks_axes. Supply only the " +
                 "fields you want to change; all are optional strings (numbers/booleans as text). " +
                 "NOT saved to disk automatically — call twincat_save_project to persist.")]
    public string ConfigureAxis(
        [Description("Tree path of the axis to configure, from twincat_list_nc_tasks_axes.")]
        string axisPath,
        [Description("Axis type / drive class (Achstyp). 'canopen_ds402' (EL72xx/ELM72xx/AX8xxx, CoE) " +
                     "or 'sercos_soe' (AX5xxx, SoE). Sets AxisIoType + Encoder/EncType + Drive/DrvType " +
                     "together; TwinCAT regenerates the dependent process image.")]
        string? axisType = null,
        [Description("Engineering unit, e.g. 'mm' or 'deg'. Maps to AxisParam/Unit.")]
        string? unit = null,
        [Description("Encoder scaling numerator. Maps to Encoder/EncParam/ScaleFactorNumerator.")]
        string? scaleFactorNumerator = null,
        [Description("Encoder scaling denominator. Maps to Encoder/EncParam/ScaleFactorDenominator.")]
        string? scaleFactorDenominator = null,
        [Description("Maximum velocity. Maps to AxisParam/Velo/Maximum.")]
        string? velocityMax = null,
        [Description("Acceleration. Maps to AxisParam/Dynamic/Acceleration.")]
        string? acceleration = null,
        [Description("Deceleration. Maps to AxisParam/Dynamic/Deceleration.")]
        string? deceleration = null,
        [Description("Jerk. Maps to AxisParam/Dynamic/Jerk.")]
        string? jerk = null,
        [Description("Encoder inversion 'true'/'false'. Maps to Encoder/EncParam/Inverse.")]
        string? encoderInverse = null,
        [Description("Modulo factor. Maps to Encoder/EncParam/ModuloFactor.")]
        string? moduloFactor = null,
        [Description("Enable soft minimum position limit 'true'/'false'.")]
        string? softMinEnable = null,
        [Description("Soft minimum position limit value.")]
        string? softMinRange = null,
        [Description("Enable soft maximum position limit 'true'/'false'.")]
        string? softMaxEnable = null,
        [Description("Soft maximum position limit value.")]
        string? softMaxRange = null,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_configure_axis",
            new
            {
                axisPath, axisType, unit, scaleFactorNumerator, scaleFactorDenominator,
                velocityMax, acceleration, deceleration, jerk, encoderInverse, moduloFactor,
                softMinEnable, softMinRange, softMaxEnable, softMaxRange
            }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                void Add(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) fields[k] = v!.Trim(); }
                Add("axisType", axisType);
                Add("unit", unit);
                Add("scaleFactorNumerator", scaleFactorNumerator);
                Add("scaleFactorDenominator", scaleFactorDenominator);
                Add("velocityMax", velocityMax);
                Add("acceleration", acceleration);
                Add("deceleration", deceleration);
                Add("jerk", jerk);
                Add("encoderInverse", encoderInverse);
                Add("moduloFactor", moduloFactor);
                Add("softMinEnable", softMinEnable);
                Add("softMinRange", softMinRange);
                Add("softMaxEnable", softMaxEnable);
                Add("softMaxRange", softMaxRange);
                return new NcAxesCoordinator(bridge.Dispatcher.Session).ConfigureAxis(axisPath, fields);
            }), cancellationToken);

    [McpServerTool(Name = "twincat_read_axis_params", ReadOnly = true)]
    [Description("Read the current parameters of an axis. Read-only. Requires XAE running with a " +
                 "solution open. Resolve axisPath via twincat_list_nc_tasks_axes. Returns a " +
                 "summary, the known field values (the same keys twincat_configure_axis accepts: " +
                 "unit, scaleFactorNumerator, velocityMax, …) and the raw axis ProduceXml.")]
    public string ReadAxisParams(
        [Description("Tree path of the axis, from twincat_list_nc_tasks_axes.")] string axisPath,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_read_axis_params", new { axisPath }, () =>
            bridge.Dispatcher.Run(() =>
            {
                var (summary, p) = new NcAxesCoordinator(bridge.Dispatcher.Session).ReadAxisParams(axisPath);
                if (McpErrors.IsError(summary) || p is null)
                    return summary;

                var sb = new StringBuilder();
                sb.AppendLine(summary);
                foreach (var kv in p.Fields)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {kv.Key} = {kv.Value}");
                sb.AppendLine("--- raw ProduceXml ---");
                sb.Append(p.RawXml);
                return sb.ToString();
            }), cancellationToken);
}
