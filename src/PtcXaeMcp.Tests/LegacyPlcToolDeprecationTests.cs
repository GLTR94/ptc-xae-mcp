using System.Text.RegularExpressions;
using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>
/// feature/plc-objects: the 15 PLC-code tools that predate the generic twincat_plc_* verbs keep their behaviour
/// but carry "[Deprecated] use twincat_plc_&lt;verb&gt; (kind=…)" in their result and say so in their description.
/// The three tools that return source text carry it at the end of the FIRST line, so that nobody who takes
/// "everything after --- Implementation ---" as code writes the notice back into the PLC.
/// Like <see cref="McpToolResultContractTests"/> this reads the tool sources, because a method body is not
/// visible to reflection.
/// </summary>
public sealed class LegacyPlcToolDeprecationTests
{
    private static readonly Dictionary<string, string> Replacements = new(StringComparer.Ordinal)
    {
        ["twincat_list_pous"] = "twincat_plc_list",
        ["twincat_list_pou_members"] = "twincat_plc_list",
        ["twincat_create_pou"] = "twincat_plc_create",
        ["twincat_create_dut"] = "twincat_plc_create",
        ["twincat_create_gvl"] = "twincat_plc_create",
        ["twincat_create_interface"] = "twincat_plc_create",
        ["twincat_add_pou_member"] = "twincat_plc_create",
        ["twincat_read_pou"] = "twincat_plc_read",
        ["twincat_read_gvl"] = "twincat_plc_read",
        ["twincat_read_pou_member"] = "twincat_plc_read",
        ["twincat_write_pou_body"] = "twincat_plc_write",
        ["twincat_write_gvl"] = "twincat_plc_write",
        ["twincat_write_pou_member"] = "twincat_plc_write",
        ["twincat_delete_plc_object"] = "twincat_plc_delete",
        ["twincat_rename_plc_object"] = "twincat_plc_rename",
    };

    private static readonly HashSet<string> SourceTextTools =
        new(StringComparer.Ordinal) { "twincat_read_pou", "twincat_read_gvl", "twincat_read_pou_member" };

    private static readonly Regex ToolAttributeRegex =
        new(@"\[McpServerTool\(Name\s*=\s*""([^""]+)""[^\]]*\)\]", RegexOptions.Compiled);

    [Fact]
    public void Append_AddsTheDeprecationLineAfterTheResult()
    {
        string text = McpDeprecation.Append("Wrote GVL 'x'.", "twincat_plc_write (kind=GVL or DUT)");
        text.Should().Be($"Wrote GVL 'x'.{Environment.NewLine}[Deprecated] use twincat_plc_write (kind=GVL or DUT).");
    }

    [Fact]
    public void Append_KeepsAnErrorAnError()
    {
        McpErrors.IsError(McpDeprecation.Append("[Error] treePath is required.", "twincat_plc_read")).Should().BeTrue();
    }

    [Fact]
    public void AppendToFirstLine_PutsTheNoticeOnTheSummaryLine_AndLeavesTheSourceTextAlone()
    {
        const string read = "Read POU 'MAIN' (ST).\n--- Declaration ---\nPROGRAM MAIN\n--- Implementation ---\nx := 1;";
        string text = McpDeprecation.AppendToFirstLine(read, "twincat_plc_read (kind=POU or INTERFACE)");

        text.Should().Be("Read POU 'MAIN' (ST). [Deprecated] use twincat_plc_read (kind=POU or INTERFACE).\n" +
                         "--- Declaration ---\nPROGRAM MAIN\n--- Implementation ---\nx := 1;");

        // What a round-tripping caller extracts as code is exactly what the tool returned before.
        const string marker = "--- Implementation ---";
        string code = text[(text.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        code.Should().Be("\nx := 1;");
    }

    [Theory]
    [InlineData("Read GVL 'x'.\r\n--- Declaration ---\r\nVAR_GLOBAL\r\nEND_VAR", "Read GVL 'x'. [Deprecated] use r.\r\n--- Declaration ---")]
    [InlineData("[Error] treePath is required.", "[Error] treePath is required. [Deprecated] use r.")]
    public void AppendToFirstLine_HandlesCrLfAndSingleLineResults(string input, string expectedStart)
    {
        string text = McpDeprecation.AppendToFirstLine(input, "r");
        text.Should().StartWith(expectedStart);
        McpErrors.IsError(text).Should().Be(input.StartsWith("[Error]", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryLegacyPlcTool_IsWrappedAndSaysDeprecatedInItsDescription()
    {
        var bodies = LoadToolBodies();
        var failures = new List<string>();

        foreach (var (tool, replacement) in Replacements)
        {
            if (!bodies.TryGetValue(tool, out var body))
            {
                failures.Add($"{tool}: no source body found.");
                continue;
            }
            string wrapper = SourceTextTools.Contains(tool) ? "McpDeprecation.AppendToFirstLine(" : "McpDeprecation.Append(";
            if (!body.Contains(wrapper, StringComparison.Ordinal))
                failures.Add($"{tool}: result is not wrapped in {wrapper.TrimEnd('(')}.");
            if (!body.Contains($"\"{replacement}", StringComparison.Ordinal))
                failures.Add($"{tool}: deprecation line does not name {replacement}.");
            if (!body.Contains($"[Description(\"DEPRECATED — use {replacement}", StringComparison.Ordinal))
                failures.Add($"{tool}: description does not start with the DEPRECATED notice for {replacement}.");
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheLegacyPlcTools_AreMarkedDeprecated()
    {
        var offenders = LoadToolBodies()
            .Where(kv => !Replacements.ContainsKey(kv.Key) && kv.Value.Contains("McpDeprecation.", StringComparison.Ordinal))
            .Select(kv => kv.Key);
        offenders.Should().BeEmpty("library tools and the plcopen tools stay un-deprecated");
    }

    private static Dictionary<string, string> LoadToolBodies()
    {
        var dir = FindMcpSourceDirectory();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(dir, "TwinCatMcpTools.*.cs"))
        {
            var text = File.ReadAllText(file);
            var matches = ToolAttributeRegex.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                int start = matches[i].Index + matches[i].Length;
                int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                result[matches[i].Groups[1].Value] = text[start..end];
            }
        }
        return result;
    }

    private static string FindMcpSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "PtcXaeMcp.WinForms", "Mcp");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "TwinCatMcpTools.*.cs").Any())
                return candidate;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/PtcXaeMcp.WinForms/Mcp by walking up from " + AppContext.BaseDirectory);
    }
}
