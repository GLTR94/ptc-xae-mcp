using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #151 item 1: every <c>[McpServerTool]</c> method takes a trailing
/// <c>CancellationToken cancellationToken = default</c> parameter so the MCP
/// SDK 1.3.0 binds the request's cancellation token automatically — but the
/// SDK must NOT surface it as a schema property (clients should never be
/// asked to supply it). This is verified empirically here by building the
/// real <see cref="McpServerTool"/> for every tool method via the SDK's own
/// reflection factory and inspecting the generated
/// <see cref="Tool.InputSchema"/>, rather than assumed.
/// </summary>
public sealed class McpToolCancellationTests
{
    /// <summary>
    /// The production tool surface plus the diagnostics/research/test-support surface
    /// (<see cref="TwinCatMcpToolsDiagnostics"/>, issue #157 item 1, only registered at
    /// runtime with --diagnostics / PTC_XAE_MCP_DIAGNOSTICS=1) — both must satisfy the
    /// same CancellationToken/schema contract.
    /// </summary>
    private static readonly Type[] ToolTypes = [typeof(TwinCatMcpTools), typeof(TwinCatMcpToolsDiagnostics)];

    private static IEnumerable<MethodInfo> ToolMethods() =>
        ToolTypes.SelectMany(t => t
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null));

    [Fact]
    public void EveryMcpServerTool_TakesATrailingCancellationToken()
    {
        var missing = ToolMethods()
            .Where(m => m.GetParameters().LastOrDefault()?.ParameterType != typeof(CancellationToken))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "Tools without a trailing CancellationToken parameter:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void GeneratedInputSchema_NeverExposesCancellationTokenOrRequestContext()
    {
        var failures = new List<string>();

        foreach (var method in ToolMethods())
        {
            var toolName = method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;

            // Never actually invoked: schema generation is purely reflection over
            // the method's parameters, so the target factory only needs to satisfy
            // the SDK's non-null contract for an instance method.
            McpServerTool tool;
            try
            {
                tool = McpServerTool.Create(
                    method,
                    createTargetFunc: static _ => throw new InvalidOperationException(
                        "not expected to be invoked: schema-only test"),
                    options: null);
            }
            catch (Exception ex)
            {
                failures.Add($"{toolName}: McpServerTool.Create threw building the schema: {ex.Message}");
                continue;
            }

            var schemaJson = tool.ProtocolTool.InputSchema.GetRawText();
            using var doc = JsonDocument.Parse(schemaJson);
            if (doc.RootElement.TryGetProperty("properties", out var properties))
            {
                foreach (var prop in properties.EnumerateObject())
                {
                    var name = prop.Name;
                    if (string.Equals(name, "cancellationToken", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("RequestContext", StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add($"{toolName}: schema unexpectedly exposes '{name}': {schemaJson}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Tool input schemas leaking SDK-bound parameters:\n" + string.Join("\n", failures));
    }
}
