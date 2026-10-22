using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class FieldbusCoordinatorTests
{
    private static FieldbusNode Node(string path, string name, string kind,
                                     params FieldbusNode[] children) =>
        new(path, name, ItemType: 0, ItemSubType: 0, Kind: kind, Port: null, Children: children);

    [Fact]
    public void GetTopology_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var (summary, json) = new FieldbusCoordinator(fake).GetTopology();

        Assert.StartsWith("[Error]", summary);
        Assert.Equal("{\"deviceCount\":0,\"devices\":[]}", json);
        Assert.DoesNotContain("GetFieldbusTopology", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void GetTopology_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };

        var (summary, json) = new FieldbusCoordinator(fake).GetTopology();

        Assert.StartsWith("[Error]", summary);
        Assert.Equal("{\"deviceCount\":0,\"devices\":[]}", json);
    }

    [Fact]
    public void GetTopology_EmptyTopology_ReportsNone()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var (summary, json) = new FieldbusCoordinator(fake).GetTopology();

        Assert.Equal("No fieldbus devices found under TIID.", summary);
        Assert.Equal("{\"deviceCount\":0,\"devices\":[]}", json);
        Assert.Contains("GetFieldbusTopology()", fake.CallLog);
    }

    [Fact]
    public void GetTopology_HappyPath_ReturnsSummaryAndJson()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.FieldbusTopology = new FieldbusTopology(
            DeviceCount: 2,
            Devices:
            [
                Node("TIID^Device 1 (EtherCAT)", "Device 1 (EtherCAT)", FieldbusKind.Device,
                    Node("TIID^Device 1 (EtherCAT)^Box 1 (EK1100)", "Box 1 (EK1100)", FieldbusKind.Box)),
                Node("TIID^Device 2 (EtherCAT)", "Device 2 (EtherCAT)", FieldbusKind.Device,
                    Node("TIID^Device 2 (EtherCAT)^Box 1 (EK1100)", "Box 1 (EK1100)", FieldbusKind.Box)),
            ]);

        var (summary, json) = new FieldbusCoordinator(fake).GetTopology();

        Assert.Equal("Found 2 fieldbus devices, 4 nodes total.", summary);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(2, doc.RootElement.GetProperty("deviceCount").GetInt32());
        var devices = doc.RootElement.GetProperty("devices");
        Assert.Equal(2, devices.GetArrayLength());
        Assert.Equal("Device 1 (EtherCAT)", devices[0].GetProperty("name").GetString());
        Assert.Equal("TIID^Device 1 (EtherCAT)", devices[0].GetProperty("path").GetString());
        Assert.Equal(FieldbusKind.Device, devices[0].GetProperty("kind").GetString());
        var box = devices[0].GetProperty("children")[0];
        Assert.Equal("Box 1 (EK1100)", box.GetProperty("name").GetString());
        Assert.Equal(FieldbusKind.Box, box.GetProperty("kind").GetString());
        Assert.Contains("GetFieldbusTopology()", fake.CallLog);
    }

    [Fact]
    public void GetTopology_SingleDevice_UsesSingularWord()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.FieldbusTopology = new FieldbusTopology(
            1, [Node("TIID^Device 1 (EtherCAT)", "Device 1 (EtherCAT)", FieldbusKind.Device)]);

        var (summary, _) = new FieldbusCoordinator(fake).GetTopology();

        Assert.Equal("Found 1 fieldbus device, 1 nodes total.", summary);
    }

    [Fact]
    public void GetTopology_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            GetTopologyThrows = new InvalidOperationException("TIID walk failed: boom"),
        };

        var (summary, json) = new FieldbusCoordinator(fake).GetTopology();

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("TIID walk failed", summary);
        Assert.Equal("{\"deviceCount\":0,\"devices\":[]}", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDeviceXml_EmptyPath_ReturnsError(string path)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var result = new FieldbusCoordinator(fake).GetDeviceXml(path);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("required", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetDeviceXml", string.Join(",", fake.CallLog));
    }

    [Theory]
    [InlineData("TIID")]
    [InlineData("tiid")]
    public void GetDeviceXml_RootPath_ReturnsError(string path)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var result = new FieldbusCoordinator(fake).GetDeviceXml(path);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("root", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetDeviceXml", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void GetDeviceXml_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new FieldbusCoordinator(fake).GetDeviceXml("TIID^Device 1");

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("GetDeviceXml", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void GetDeviceXml_HappyPath_ReturnsXml()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.DeviceXmlByPath["TIID^Device 1 (EtherCAT)"] = "<Device><Name>Device 1</Name></Device>";

        var result = new FieldbusCoordinator(fake).GetDeviceXml("TIID^Device 1 (EtherCAT)");

        Assert.Equal("<Device><Name>Device 1</Name></Device>", result);
        Assert.Contains("GetDeviceXml(TIID^Device 1 (EtherCAT))", fake.CallLog);
    }

    [Fact]
    public void GetDeviceXml_UnknownPath_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var result = new FieldbusCoordinator(fake).GetDeviceXml("TIID^Nope");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDeviceXml_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            GetDeviceXmlThrows = new InvalidOperationException("ProduceXml failed: boom"),
        };

        var result = new FieldbusCoordinator(fake).GetDeviceXml("TIID^Device 1");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("ProduceXml failed", result);
    }

    // ---- ConfigureEtherCatSlave tests ----------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfigureEtherCatSlave_EmptyPath_ReturnsError(string path)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave(path, "<Slave/>");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("required", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfigureEtherCatSlave", string.Join(",", fake.CallLog));
    }

    [Theory]
    [InlineData("TIID")]
    [InlineData("tiid")]
    public void ConfigureEtherCatSlave_RootPath_ReturnsError(string path)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave(path, "<Slave/>");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("root", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfigureEtherCatSlave", string.Join(",", fake.CallLog));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfigureEtherCatSlave_EmptyXml_ReturnsError(string xml)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave("TIID^Device 1", xml);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("required", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfigureEtherCatSlave", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void ConfigureEtherCatSlave_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave("TIID^Device 1", "<Slave/>");

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("ConfigureEtherCatSlave", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void ConfigureEtherCatSlave_HappyPath_ReturnsSuccess()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.DeviceXmlByPath["TIID^Device 1 (EtherCAT)^Box 1 (EK1100)"] = "<orig/>";

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave(
            "TIID^Device 1 (EtherCAT)^Box 1 (EK1100)", "<Slave><Name>Box 1</Name></Slave>");

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), $"Expected success but got: {result}");
        Assert.Contains("configured", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConfigureEtherCatSlave(TIID^Device 1 (EtherCAT)^Box 1 (EK1100))", fake.CallLog);
        Assert.Equal("<Slave><Name>Box 1</Name></Slave>",
            fake.ConfiguredSlavesByPath["TIID^Device 1 (EtherCAT)^Box 1 (EK1100)"]);
    }

    [Fact]
    public void ConfigureEtherCatSlave_UnknownPath_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave("TIID^Nope", "<Slave/>");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigureEtherCatSlave_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            ConfigureEtherCatSlaveThrows = new InvalidOperationException("ConsumeXml failed: boom"),
        };
        fake.DeviceXmlByPath["TIID^Device 1"] = "<orig/>";

        var result = new FieldbusCoordinator(fake).ConfigureEtherCatSlave("TIID^Device 1", "<Slave/>");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("ConsumeXml failed", result);
    }
}
