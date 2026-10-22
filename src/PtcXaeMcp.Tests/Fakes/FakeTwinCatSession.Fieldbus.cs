using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    public FieldbusTopology FieldbusTopology { get; set; } =
        new FieldbusTopology(0, Array.Empty<FieldbusNode>());
    public Exception? GetTopologyThrows { get; set; }
    public FieldbusTopology GetFieldbusTopology()
    {
        CallLog.Add("GetFieldbusTopology()");
        if (GetTopologyThrows is not null) throw GetTopologyThrows;
        return FieldbusTopology;
    }

    public Dictionary<string, string> DeviceXmlByPath { get; } = new();
    public Exception? GetDeviceXmlThrows { get; set; }
    public string GetDeviceXml(string treePath)
    {
        CallLog.Add($"GetDeviceXml({treePath})");
        if (GetDeviceXmlThrows is not null) throw GetDeviceXmlThrows;
        return DeviceXmlByPath.TryGetValue(treePath, out var xml)
            ? xml
            : throw new InvalidOperationException($"Tree path not found: {treePath}");
    }

    public Dictionary<string, string> ConfiguredSlavesByPath { get; } = new();
    public Exception? ConfigureEtherCatSlaveThrows { get; set; }
    /// <summary>Simulates XAE accepting ConsumeXml without applying it (read-back unchanged).</summary>
    public bool ConfigureIgnoresWrite { get; set; }
    public void ConfigureEtherCatSlave(string treePath, string xml)
    {
        CallLog.Add($"ConfigureEtherCatSlave({treePath})");
        if (ConfigureEtherCatSlaveThrows is not null) throw ConfigureEtherCatSlaveThrows;
        if (!DeviceXmlByPath.ContainsKey(treePath))
            throw new InvalidOperationException($"Tree path not found: {treePath}");
        ConfiguredSlavesByPath[treePath] = xml;
        if (!ConfigureIgnoresWrite) DeviceXmlByPath[treePath] = xml;
    }
}
