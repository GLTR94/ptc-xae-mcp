using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;
using System.Text.Json;

namespace PtcXaeMcp.Tests;

public class ActiveProjectStatusTests
{
    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    [Fact]
    public void WhenSolutionOpen_ReturnsOpenTrueAndPath()
    {
        var fake = new FakeTwinCatSession
        {
            SolutionOpen = true,
            ActiveSlnPath = @"C:\repos\MyProject\MyProject.sln",
        };

        var json = Parse(new ActiveProjectCoordinator(fake).GetActiveProject());

        Assert.True(json.GetProperty("solutionOpen").GetBoolean());
        Assert.Equal(@"C:\repos\MyProject\MyProject.sln",
            json.GetProperty("solutionPath").GetString());
        Assert.Equal("MyProject",
            json.GetProperty("solutionName").GetString());
    }

    [Fact]
    public void WhenSolutionClosed_ReturnsOpenFalseAndNulls()
    {
        var fake = new FakeTwinCatSession { SolutionOpen = false };

        var json = Parse(new ActiveProjectCoordinator(fake).GetActiveProject());

        Assert.False(json.GetProperty("solutionOpen").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("solutionPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("solutionName").ValueKind);
    }

    [Fact]
    public void SolutionName_DerivedFromFilenameWithoutExtension()
    {
        var fake = new FakeTwinCatSession
        {
            SolutionOpen = true,
            ActiveSlnPath = @"C:\repos\SampleProject\SampleProject.sln",
        };

        var json = Parse(new ActiveProjectCoordinator(fake).GetActiveProject());

        Assert.Equal("SampleProject",
            json.GetProperty("solutionName").GetString());
    }
}
