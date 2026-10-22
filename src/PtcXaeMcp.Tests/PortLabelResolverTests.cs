using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class PortLabelResolverTests
{
    private static string WriteJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"port-labels-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string SeedJson = """
        {
          "EK1122":      { "A": "X1", "B": "X2" },
          "EK1200":      { "A": "X1" },
          "EK1200-5000": { "A": "X1" },
          "EK1100":      {}
        }
        """;

    [Fact]
    public void Resolve_KnownModelAndPort_ReturnsLabel()
    {
        var path = WriteJson(SeedJson);
        var resolver = new PortLabelResolver(path);

        Assert.Equal("X1", resolver.Resolve("EK1122", "A"));
        Assert.Equal("X2", resolver.Resolve("EK1122", "B"));
        Assert.Equal("X1", resolver.Resolve("EK1200", "A"));
        Assert.Equal("X1", resolver.Resolve("EK1200-5000", "A"));
    }

    [Fact]
    public void Resolve_UnknownPortForKnownModel_ReturnsNull()
    {
        var path = WriteJson(SeedJson);
        var resolver = new PortLabelResolver(path);

        Assert.Null(resolver.Resolve("EK1200", "B"));  // EK1200 has no B port
        Assert.Null(resolver.Resolve("EK1100", "A"));  // EK1100 has empty port map
    }

    [Fact]
    public void Resolve_UnknownModel_ReturnsNull()
    {
        var path = WriteJson(SeedJson);
        var resolver = new PortLabelResolver(path);

        Assert.Null(resolver.Resolve("EK9999", "A"));
    }

    [Fact]
    public void Resolve_EmptyModelOrPort_ReturnsNull()
    {
        var path = WriteJson(SeedJson);
        var resolver = new PortLabelResolver(path);

        Assert.Null(resolver.Resolve("", "A"));
        Assert.Null(resolver.Resolve("EK1122", ""));
        Assert.Null(resolver.Resolve(null!, "A"));
        Assert.Null(resolver.Resolve("EK1122", null!));
    }

    [Fact]
    public void Resolve_MissingFile_ReturnsNullWithoutThrowing()
    {
        var resolver = new PortLabelResolver(@"C:\does\not\exist\port-labels.json");

        Assert.Null(resolver.Resolve("EK1122", "A"));
    }

    [Fact]
    public void Resolve_CacheRoundTrip_WrittenEntriesAreReadBack()
    {
        var path = WriteJson(SeedJson);
        var resolver1 = new PortLabelResolver(path);
        Assert.Equal("X1", resolver1.Resolve("EK1122", "A"));

        // Simulate adding a new model to the JSON file and re-loading.
        var updatedJson = """
            {
              "EK1122":  { "A": "X1", "B": "X2" },
              "EK1200":  { "A": "X1" },
              "EK1100":  {},
              "EK1300":  { "A": "X1", "B": "X2", "C": "X3" }
            }
            """;
        File.WriteAllText(path, updatedJson);
        var resolver2 = new PortLabelResolver(path);

        Assert.Equal("X3", resolver2.Resolve("EK1300", "C"));
        // Original entries still present
        Assert.Equal("X2", resolver2.Resolve("EK1122", "B"));
    }
}
