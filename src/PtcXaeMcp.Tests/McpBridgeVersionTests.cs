using System.Reflection;
using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>
/// <see cref="McpBridge.ThisAssemblyVersion"/> is the single source of truth behind both the
/// MCP <c>ServerInfo.Version</c> (<c>McpBridge.cs</c>'s <c>AddMcpServer</c> call) and the
/// <c>/health</c> endpoint's <c>version</c> field — both call it directly, so exercising the
/// helper here covers both surfaces without starting the Kestrel host (issue #157 item 3:
/// one <c>&lt;Version&gt;</c> in <c>Directory.Build.props</c>, no independent copies).
/// </summary>
public sealed class McpBridgeVersionTests
{
    [Fact]
    public void ThisAssemblyVersion_MatchesAssemblyInformationalVersion()
    {
        var expected = typeof(McpBridge).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.NotNull(expected);
        Assert.Equal(expected, McpBridge.ThisAssemblyVersion());
    }

    [Fact]
    public void ThisAssemblyVersion_IsNotTheOldHardcodedPlaceholder()
    {
        // Guards against a regression back to the literal "1.0" that ServerInfo.Version
        // used to be hardcoded to before it read Directory.Build.props's <Version>.
        Assert.NotEqual("1.0", McpBridge.ThisAssemblyVersion());
        Assert.NotEmpty(McpBridge.ThisAssemblyVersion());
    }

    [Theory]
    [InlineData("1.3.0+6495580d940eb8146714c6b1ba16b50df92f4750", "v1.3.0 (6495580)")]
    [InlineData("1.3.0+abc", "v1.3.0 (abc)")]
    [InlineData("1.3.0", "v1.3.0")]
    [InlineData("1.3.0+", "v1.3.0")]
    [InlineData("", "v?")]
    public void DisplayVersion_ShowsReleaseAndShortCommit(string informational, string expected) =>
        Assert.Equal(expected, McpBridge.DisplayVersion(informational));

    [Fact]
    public void DisplayVersion_OfThisAssembly_StartsWithTheReleaseNumber()
    {
        string release = McpBridge.ThisAssemblyVersion().Split('+')[0];
        Assert.StartsWith("v" + release, McpBridge.DisplayVersion(McpBridge.ThisAssemblyVersion()));
    }
}
