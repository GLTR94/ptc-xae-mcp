using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #267: every <c>[McpServerTool]</c> declares whether it only reads or changes
/// something, so MCP clients get <c>readOnlyHint</c>/<c>destructiveHint</c> and the GUI
/// tool console can ask before a write. The MCP spec defaults <c>destructiveHint</c> to
/// true for a non-read-only tool, so a non-read-only tool must set <c>Destructive</c>
/// explicitly. The attribute's bool properties cannot tell "unset" from "false", hence
/// the check on <see cref="CustomAttributeData.NamedArguments"/>.
/// </summary>
public sealed class McpToolAnnotationTests
{
    private static readonly Type[] ToolTypes = [typeof(TwinCatMcpTools), typeof(TwinCatMcpToolsDiagnostics)];

    private static IEnumerable<MethodInfo> ToolMethods() =>
        ToolTypes.SelectMany(t => t
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null));

    private static IReadOnlyDictionary<string, object?> NamedArguments(MethodInfo method) =>
        method.GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(McpServerToolAttribute))
            .NamedArguments
            .ToDictionary(a => a.MemberName, a => a.TypedValue.Value);

    [Fact]
    public void EveryMcpServerTool_DeclaresReadOnly_AndNonReadOnlyToolsDeclareDestructive()
    {
        var failures = new List<string>();

        foreach (var method in ToolMethods())
        {
            var args = NamedArguments(method);
            var name = args.TryGetValue(nameof(McpServerToolAttribute.Name), out var n) ? n as string : method.Name;

            if (!args.TryGetValue(nameof(McpServerToolAttribute.ReadOnly), out var readOnly))
            {
                failures.Add($"{name}: ReadOnly not set");
                continue;
            }

            if (readOnly is false && !args.ContainsKey(nameof(McpServerToolAttribute.Destructive)))
                failures.Add($"{name}: ReadOnly = false but Destructive not set explicitly");

            if (readOnly is true && args.TryGetValue(nameof(McpServerToolAttribute.Destructive), out var destructive) && destructive is true)
                failures.Add($"{name}: ReadOnly = true contradicts Destructive = true");
        }

        Assert.True(failures.Count == 0, "Tool annotation problems:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Cheap plausibility guard on the classification itself: a tool whose own description
    /// says it overwrites something cannot be read-only (review of PR #278 found
    /// twincat_get_mapping_info marked read-only while it overwrites its export file).
    /// </summary>
    [Fact]
    public void ReadOnlyTools_DoNotDescribeOverwriting()
    {
        var overwrites = new Regex(@"(?<!\b(?:not|never|without|no)\s)overwrit", RegexOptions.IgnoreCase);
        var suspicious = ToolMethods()
            .Where(m => NamedArguments(m).TryGetValue(nameof(McpServerToolAttribute.ReadOnly), out var ro) && ro is true)
            .Where(m => overwrites.IsMatch(m.GetCustomAttribute<DescriptionAttribute>()?.Description ?? ""))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .ToList();

        Assert.True(suspicious.Count == 0,
            "ReadOnly = true but the description mentions overwriting:\n" + string.Join("\n", suspicious));
    }

    [Fact]
    public void GeneratedProtocolTool_CarriesTheReadOnlyHint()
    {
        var missing = new List<string>();

        foreach (var method in ToolMethods())
        {
            var tool = McpServerTool.Create(
                method,
                createTargetFunc: static _ => throw new InvalidOperationException(
                    "not expected to be invoked: annotation-only test"),
                options: null);

            if (tool.ProtocolTool.Annotations?.ReadOnlyHint is null)
                missing.Add(tool.ProtocolTool.Name);
        }

        Assert.True(missing.Count == 0, "Tools without readOnlyHint in the generated tool:\n" + string.Join("\n", missing));
    }
}
