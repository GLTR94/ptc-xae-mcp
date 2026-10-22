using System.Text;
using System.Text.Json;
using PtcXaeMcp.WinForms.TwinCat.Schema;

namespace PtcXaeMcp.WinForms;

/// <summary>
/// Quick schema-file check shown in the GUI status row and returned by
/// <c>twincat_validate_schema</c>. Moved out of <c>Form1</c> (issue #268) so the MCP tools
/// no longer reference the form. The structural rules live in <see cref="SchemaModelValidator"/>,
/// shared with <c>NativeTwinCatRunner.LoadAndValidateSchema</c>.
/// </summary>
internal static class SchemaValidation
{
    internal static SchemaValidationResult Validate(string schemaPath)
    {
        if (string.IsNullOrWhiteSpace(schemaPath))
        {
            return new SchemaValidationResult(false, IndicatorState.Warn, "No file selected", null, [new SchemaIssue("No file selected.")]);
        }

        if (!File.Exists(schemaPath))
        {
            return new SchemaValidationResult(false, IndicatorState.Error, "File not found", null, [new SchemaIssue("File not found.", schemaPath)]);
        }

        string rawJson;
        try
        {
            rawJson = File.ReadAllText(schemaPath, Encoding.UTF8);
            _ = JsonDocument.Parse(rawJson);
        }
        catch (Exception ex)
        {
            return new SchemaValidationResult(false, IndicatorState.Error, "JSON invalid: Syntax error: " + ex.Message, null, [new SchemaIssue("Syntax error: " + ex.Message, "<json>")]);
        }

        JsonDocument doc = JsonDocument.Parse(rawJson);
        List<SchemaIssue> issues = SchemaModelValidator.Validate(doc.RootElement, rawJson)
            .Select(i => new SchemaIssue(i.Message, i.Path, i.Line))
            .ToList();
        if (issues.Count > 0)
        {
            string suffix = issues.Count > 1 ? $" (+{issues.Count - 1} more)" : string.Empty;
            return new SchemaValidationResult(false, IndicatorState.Error, "JSON invalid: " + issues[0].Format() + suffix, doc.RootElement.Clone(), issues);
        }

        JsonElement root = doc.RootElement;
        int ioCount = root.TryGetProperty("ioPoints", out var ioPoints) && ioPoints.ValueKind == JsonValueKind.Array ? ioPoints.GetArrayLength() : 0;
        string sourcePdf = TryGetString(root, "meta", "sourcePdf") ?? "";
        string generatedAt = TryGetString(root, "meta", "generatedAt") ?? "";
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourcePdf)) meta.Add($"PDF: {sourcePdf}");
        if (!string.IsNullOrWhiteSpace(generatedAt)) meta.Add($"Generated: {generatedAt}");
        string text = $"JSON valid, IO signals: {ioCount}";
        if (meta.Count > 0) text += Environment.NewLine + string.Join(" | ", meta);
        return new SchemaValidationResult(true, IndicatorState.Ok, text, root.Clone(), []);
    }

    private static string? TryGetString(JsonElement root, string level1, string level2)
    {
        if (root.TryGetProperty(level1, out var a) &&
            a.ValueKind == JsonValueKind.Object &&
            a.TryGetProperty(level2, out var b) &&
            b.ValueKind == JsonValueKind.String)
        {
            return b.GetString();
        }
        return null;
    }
}

internal enum IndicatorState
{
    Neutral,
    Ok,
    Warn,
    Error
}

internal sealed record SchemaIssue(string Message, string Path = "", int? Line = null)
{
    public string Format()
    {
        var parts = new List<string> { Message };
        if (!string.IsNullOrWhiteSpace(Path)) parts.Add($"Path: {Path}");
        if (Line.HasValue) parts.Add($"Line: {Line.Value}");
        return string.Join(" | ", parts);
    }
}

internal sealed record SchemaValidationResult(bool IsValid, IndicatorState State, string Text, JsonElement? Config, IReadOnlyList<SchemaIssue> Issues);
