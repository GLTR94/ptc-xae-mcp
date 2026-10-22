using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    public List<NcTaskEntry> NcTasks { get; } = new();
    public Exception? ListNcTasksAxesThrows { get; set; }
    public IReadOnlyList<NcTaskEntry> ListNcTasksAxes()
    {
        CallLog.Add("ListNcTasksAxes()");
        if (ListNcTasksAxesThrows is not null) throw ListNcTasksAxesThrows;
        return NcTasks;
    }

    public string CreateNcTaskResult { get; set; } = "TINC^NC-Task 1";
    public Exception? CreateNcTaskThrows { get; set; }
    public string CreateNcTask(string name)
    {
        CallLog.Add($"CreateNcTask({name})");
        if (CreateNcTaskThrows is not null) throw CreateNcTaskThrows;
        return CreateNcTaskResult;
    }

    public string CreateAxisResult { get; set; } = "TINC^NC-Task 1^Axes^Axis 1";
    public Exception? CreateAxisThrows { get; set; }
    public string CreateAxis(string ncTaskPath, string axisName)
    {
        CallLog.Add($"CreateAxis({ncTaskPath},{axisName})");
        if (CreateAxisThrows is not null) throw CreateAxisThrows;
        return CreateAxisResult;
    }

    public string ConfigureAxisResult { get; set; } = "(applied)";
    public Exception? ConfigureAxisThrows { get; set; }
    public IReadOnlyDictionary<string, string>? LastConfigureAxisFields { get; private set; }
    public string ConfigureAxis(string axisPath, IReadOnlyDictionary<string, string> fields)
    {
        CallLog.Add($"ConfigureAxis({axisPath},{fields.Count})");
        LastConfigureAxisFields = fields;
        if (ConfigureAxisThrows is not null) throw ConfigureAxisThrows;
        if (!NoOpMutations.Contains(nameof(ConfigureAxis)))
        {
            // Like the real axis: the new values show up in the next ProduceXml.
            var merged = new Dictionary<string, string>(ReadAxisParamsResult.Fields, StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in fields)
                merged[k] = k.Equals(NcAxisField.AxisTypeKey, StringComparison.OrdinalIgnoreCase)
                    && NcAxisField.AxisTypeMap.TryGetValue(v.Trim(), out var t) ? t.Io : v;
            ReadAxisParamsResult = ReadAxisParamsResult with { Fields = merged };
        }
        return ConfigureAxisResult;
    }

    public NcAxisParams ReadAxisParamsResult { get; set; } =
        new("<TreeItem/>", new Dictionary<string, string> { ["unit"] = "mm" });
    public Exception? ReadAxisParamsThrows { get; set; }
    public NcAxisParams ReadAxisParams(string axisPath)
    {
        CallLog.Add($"ReadAxisParams({axisPath})");
        if (ReadAxisParamsThrows is not null) throw ReadAxisParamsThrows;
        return ReadAxisParamsResult;
    }
}
