using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- coredump-analysis ----
    public string FakeCoreDumpListResult { get; set; } = "(fake dump list)";
    public string FakeCoreDumpAnalyzeResult { get; set; } = "(fake dump analysis)";
    public string FakeCoreDumpReadCallStackResult { get; set; } = "(fake call stack)";
    public Exception? CoreDumpListThrows { get; set; }
    public Exception? CoreDumpAnalyzeThrows { get; set; }
    public Exception? CoreDumpReadCallStackThrows { get; set; }

    public string CoreDumpList(string? instanceName, int? port, string? projectFolder)
    {
        CallLog.Add($"CoreDumpList(instance={instanceName ?? "<null>"}, port={port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"}, folder={projectFolder ?? "<null>"})");
        if (CoreDumpListThrows is not null) throw CoreDumpListThrows;
        return FakeCoreDumpListResult;
    }

    public string CoreDumpAnalyze(string? dumpPath, string? instanceName, int? port, string? projectFolder)
    {
        CallLog.Add($"CoreDumpAnalyze(path={dumpPath ?? "<null>"}, instance={instanceName ?? "<null>"}, port={port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>"}, folder={projectFolder ?? "<null>"})");
        if (CoreDumpAnalyzeThrows is not null) throw CoreDumpAnalyzeThrows;
        return FakeCoreDumpAnalyzeResult;
    }

    public string CoreDumpReadCallStack()
    {
        CallLog.Add("CoreDumpReadCallStack()");
        if (CoreDumpReadCallStackThrows is not null) throw CoreDumpReadCallStackThrows;
        return FakeCoreDumpReadCallStackResult;
    }
}
