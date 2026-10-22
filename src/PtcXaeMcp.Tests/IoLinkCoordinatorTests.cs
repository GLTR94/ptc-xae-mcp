using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class IoLinkCoordinatorTests
{
    private static IoLinkConfig SampleConfig(string path = "TIID^Dev^Term 28 (EL6224)") =>
        new(path, "EL6224", 2,
        [
            new IoLinkPort(1, true, "VABX-A-P-EL-E12-IOS", "Festo",
                "8000870 VABX-A-P-EL-E12-IOS", "VTUX", "Festo-VABX.xml",
                [
                    new IoLinkPdoEntry("Valve 1 solenoid 00", "Q", "BIT", 1, "#x7000", 1),
                    new IoLinkPdoEntry("Valve 1 solenoid 01", "Q", "BIT", 1, "#x7000", 2),
                ]),
            new IoLinkPort(2, false, null, null, null, null, null, Array.Empty<IoLinkPdoEntry>()),
        ]);

    [Fact]
    public void GetConfig_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var (summary, json) = new IoLinkCoordinator(fake).GetConfig("TIID^Dev^Term 28 (EL6224)");

        Assert.StartsWith("[Error]", summary);
        Assert.Equal("{}", json);
        Assert.DoesNotContain("GetIoLinkConfig", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void GetConfig_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };

        var (summary, _) = new IoLinkCoordinator(fake).GetConfig("TIID^Dev^Term 28 (EL6224)");

        Assert.StartsWith("[Error]", summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetConfig_EmptyPath_ReturnsError(string path)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var (summary, json) = new IoLinkCoordinator(fake).GetConfig(path);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("required", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{}", json);
        Assert.DoesNotContain("GetIoLinkConfig", string.Join(",", fake.CallLog));
    }

    [Theory]
    [InlineData("TIID")]
    [InlineData("tiid")]
    public void GetConfig_RootPath_ReturnsError(string path)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        var (summary, _) = new IoLinkCoordinator(fake).GetConfig(path);

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("root", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetIoLinkConfig", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void GetConfig_HappyPath_ReturnsSummaryAndJson()
    {
        const string path = "TIID^Dev^Term 28 (EL6224)";
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.IoLinkConfigByPath[path] = SampleConfig(path);

        var (summary, json) = new IoLinkCoordinator(fake).GetConfig(path);

        Assert.False(summary.StartsWith("[Error]", StringComparison.Ordinal), summary);
        Assert.Contains("EL6224", summary);
        Assert.Contains("2 IO-Link port(s), 1 configured", summary);
        Assert.Contains("Port 1 = VABX-A-P-EL-E12-IOS (2 process-data point(s))", summary);
        Assert.Contains("GetIoLinkConfig(" + path + ")", fake.CallLog);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("EL6224", doc.RootElement.GetProperty("masterType").GetString());
        var ports = doc.RootElement.GetProperty("ports");
        Assert.Equal(2, ports.GetArrayLength());
        Assert.True(ports[0].GetProperty("configured").GetBoolean());
        Assert.Equal("Valve 1 solenoid 00",
            ports[0].GetProperty("processData")[0].GetProperty("name").GetString());
        Assert.Equal("Q", ports[0].GetProperty("processData")[0].GetProperty("direction").GetString());
        Assert.False(ports[1].GetProperty("configured").GetBoolean());
    }

    [Fact]
    public void GetConfig_NoIoLinkPorts_ReportsNotAMaster()
    {
        const string path = "TIID^Dev^Box 1 (EL1809)";
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.IoLinkConfigByPath[path] = new IoLinkConfig(path, "EL1809", 0, Array.Empty<IoLinkPort>());

        var (summary, _) = new IoLinkCoordinator(fake).GetConfig(path);

        Assert.Contains("No IO-Link ports", summary);
        Assert.Contains("EL1809", summary);
    }

    [Fact]
    public void GetConfig_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            GetIoLinkConfigThrows = new InvalidOperationException("ProduceXml failed: boom"),
        };

        var (summary, json) = new IoLinkCoordinator(fake).GetConfig("TIID^Dev^Term 28 (EL6224)");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("ProduceXml failed", summary);
        Assert.Equal("{}", json);
    }
}
