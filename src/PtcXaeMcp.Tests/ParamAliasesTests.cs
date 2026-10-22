using System.Reflection;
using ModelContextProtocol.Server;
using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #157 item 1: parameter renames (RuntimeControl's <c>netId</c> →
/// <c>amsNetId</c>; IoMapping's <c>v1</c>/<c>v2</c>/<c>offs1</c>/<c>offs2</c> →
/// <c>variable1TreePath</c>/<c>variable2TreePath</c>/<c>bitOffset1</c>/<c>bitOffset2</c>)
/// must keep the old name working for one release, because MCP clients bind
/// tool arguments by name. Two things are verified:
/// <list type="number">
/// <item>the <see cref="ParamAliases.Coalesce(string?, string?)"/>/
/// <see cref="ParamAliases.Coalesce(int, int?)"/> helper itself prefers the new
/// name and falls back to the deprecated one;</item>
/// <item>the real generated MCP input schema for each affected tool method
/// still exposes BOTH the new and the deprecated parameter name, proving an
/// MCP client binding by either name can still call the tool — mirroring how
/// <see cref="McpToolCancellationTests"/> inspects the schema without invoking
/// the tool.</item>
/// </list>
/// </summary>
public sealed class ParamAliasesTests
{
    // ---- Coalesce helper --------------------------------------------------

    [Fact]
    public void StringCoalesce_PrefersNewName_WhenBothSupplied()
        => Assert.Equal("new", ParamAliases.Coalesce("new", "old"));

    [Fact]
    public void StringCoalesce_FallsBackToDeprecatedName_WhenNewNameOmitted()
        => Assert.Equal("old", ParamAliases.Coalesce(null, "old"));

    [Fact]
    public void StringCoalesce_FallsBackToDeprecatedName_WhenNewNameEmpty()
        => Assert.Equal("old", ParamAliases.Coalesce("", "old"));

    [Fact]
    public void StringCoalesce_ReturnsNull_WhenNeitherSupplied()
        => Assert.Null(ParamAliases.Coalesce(null, null));

    [Fact]
    public void IntCoalesce_PrefersDeprecatedName_WhenSupplied()
        // The value-typed overload: the "preferred" parameter always carries a
        // non-null default (0), so the *deprecated* side is the nullable one
        // that signals whether the caller actually supplied it.
        => Assert.Equal(5, ParamAliases.Coalesce(0, 5));

    [Fact]
    public void IntCoalesce_FallsBackToPreferredName_WhenDeprecatedNameOmitted()
        => Assert.Equal(3, ParamAliases.Coalesce(3, null));

    // ---- schema exposes both names -----------------------------------------

    private static IEnumerable<string> SchemaParameterNames(MethodInfo method)
    {
        var tool = McpServerTool.Create(
            method,
            createTargetFunc: static _ => throw new InvalidOperationException(
                "not expected to be invoked: schema-only test"),
            options: null);

        var schemaJson = tool.ProtocolTool.InputSchema.GetRawText();
        using var doc = System.Text.Json.JsonDocument.Parse(schemaJson);
        if (!doc.RootElement.TryGetProperty("properties", out var properties))
            return [];
        return properties.EnumerateObject().Select(p => p.Name).ToList();
    }

    private static MethodInfo ToolMethod(string name) =>
        typeof(TwinCatMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == name);

    [Theory]
    [InlineData(nameof(TwinCatMcpTools.AddRoute), "amsNetId", "netId")]
    [InlineData(nameof(TwinCatMcpTools.SetTarget), "amsNetId", "netId")]
    [InlineData(nameof(TwinCatMcpTools.LinkVariables), "variable1TreePath", "v1")]
    [InlineData(nameof(TwinCatMcpTools.LinkVariables), "variable2TreePath", "v2")]
    [InlineData(nameof(TwinCatMcpTools.LinkVariables), "bitOffset1", "offs1")]
    [InlineData(nameof(TwinCatMcpTools.LinkVariables), "bitOffset2", "offs2")]
    [InlineData(nameof(TwinCatMcpTools.UnlinkVariables), "variable1TreePath", "v1")]
    [InlineData(nameof(TwinCatMcpTools.UnlinkVariables), "variable2TreePath", "v2")]
    public void RenamedToolParameter_SchemaExposesBothNewAndDeprecatedName(
        string methodName, string newName, string deprecatedName)
    {
        var names = SchemaParameterNames(ToolMethod(methodName)).ToList();

        Assert.Contains(newName, names);
        Assert.Contains(deprecatedName, names);
    }
}
