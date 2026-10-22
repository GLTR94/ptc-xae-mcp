using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- iolink-devices -----------------------------------------------------

    public Dictionary<string, IoLinkConfig> IoLinkConfigByPath { get; } = new();
    public Exception? GetIoLinkConfigThrows { get; set; }

    public IoLinkConfig GetIoLinkConfig(string treePath)
    {
        CallLog.Add($"GetIoLinkConfig({treePath})");
        if (GetIoLinkConfigThrows is not null) throw GetIoLinkConfigThrows;
        return IoLinkConfigByPath.TryGetValue(treePath, out var cfg)
            ? cfg
            : throw new InvalidOperationException($"Tree path not found: {treePath}");
    }
}
