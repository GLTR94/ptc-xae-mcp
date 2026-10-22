using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.WinForms.TwinCat.Schema;

internal readonly record struct SchemaCheckIssue(string Message, string Path, int? Line);

/// <summary>
/// The one structural validator for the apply-schema JSON (R1). Was E-23: two independently
/// maintained copies existed — NativeTwinCatRunner.ValidateSchema (throw-based, ran before an
/// actual apply) and Form1.GetSchemaValidationResult's line-numbered issue list (used by the UI
/// and twincat_validate_schema) — and both used lenient deserialization that silently dropped
/// unknown/misspelled JSON properties instead of reporting them. Both call sites now delegate
/// here.
/// </summary>
internal static class SchemaModelValidator
{
    /// <param name="root">The parsed schema JSON.</param>
    /// <param name="rawJson">The exact text <paramref name="root"/> was parsed from — used only
    /// to compute 1-based line numbers for issues (the UI shows them; the runner ignores them).</param>
    internal static List<SchemaCheckIssue> Validate(JsonElement root, string rawJson)
    {
        var issues = new List<SchemaCheckIssue>();

        if (!root.TryGetProperty("twincat", out var twincat) || twincat.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new SchemaCheckIssue("Root 'twincat' is missing.", "twincat", FindJsonLine(rawJson, "\"twincat\"\\s*:")));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TryGetString(root, "twincat", "deviceName")))
                issues.Add(new SchemaCheckIssue("twincat.deviceName is missing or empty.", "twincat.deviceName", FindJsonLine(rawJson, "\"deviceName\"\\s*:")));
            if (string.IsNullOrWhiteSpace(TryGetString(root, "twincat", "plcProjectName")))
                issues.Add(new SchemaCheckIssue("twincat.plcProjectName is missing or empty.", "twincat.plcProjectName", FindJsonLine(rawJson, "\"plcProjectName\"\\s*:")));
            if (string.IsNullOrWhiteSpace(TryGetString(root, "twincat", "gvlName")))
                issues.Add(new SchemaCheckIssue("twincat.gvlName is missing or empty.", "twincat.gvlName", FindJsonLine(rawJson, "\"gvlName\"\\s*:")));
        }

        if (!root.TryGetProperty("ethercat", out var ethercat) || ethercat.ValueKind != JsonValueKind.Object ||
            !ethercat.TryGetProperty("root", out var ethercatRoot) || ethercatRoot.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new SchemaCheckIssue("ethercat.root is missing.", "ethercat.root", FindJsonLine(rawJson, "\"root\"\\s*:")));
        }
        else if (!ethercatRoot.TryGetProperty("name", out var rootName) || string.IsNullOrWhiteSpace(rootName.GetString()))
        {
            // Preserved from the runner's pre-merge ValidateSchema: an ethercat.root object
            // with no name is just as unusable as a missing root altogether.
            issues.Add(new SchemaCheckIssue("ethercat.root.name is missing or empty.", "ethercat.root.name", FindJsonLine(rawJson, "\"root\"\\s*:")));
        }

        if (!root.TryGetProperty("ioPoints", out var ioPoints) || ioPoints.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new SchemaCheckIssue("ioPoints is missing.", "ioPoints", FindJsonLine(rawJson, "\"ioPoints\"\\s*:")));
            issues.AddRange(CheckUnmappedMembers(rawJson));
            return issues;
        }
        if (ioPoints.GetArrayLength() == 0)
            issues.Add(new SchemaCheckIssue("ioPoints is empty.", "ioPoints", FindJsonLine(rawJson, "\"ioPoints\"\\s*:")));

        var seenVarNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int idx = 0;
        foreach (JsonElement p in ioPoints.EnumerateArray())
        {
            idx++;
            string varPath = $"ioPoints[{idx}].varName";
            string termPath = $"ioPoints[{idx}].terminalName";
            string channelPath = $"ioPoints[{idx}].channel";
            string dirPath = $"ioPoints[{idx}].direction";

            string varName = p.TryGetProperty("varName", out var varNameEl) ? varNameEl.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(varName))
            {
                issues.Add(new SchemaCheckIssue($"{varPath} is missing.", varPath, FindJsonLineOccurrence(rawJson, "\"varName\"\\s*:", idx)));
            }
            else if (seenVarNames.TryGetValue(varName, out int firstIdx))
            {
                // Duplicate-varName check (new in step 1): two ioPoints writing the same GVL
                // symbol used to silently overwrite each other's link with no diagnostic.
                issues.Add(new SchemaCheckIssue(
                    $"{varPath} duplicates ioPoints[{firstIdx}].varName ('{varName}').",
                    varPath,
                    FindJsonLineOccurrence(rawJson, "\"varName\"\\s*:", idx)));
            }
            else
            {
                seenVarNames[varName] = idx;
            }

            if (!p.TryGetProperty("terminalName", out var terminalName) || string.IsNullOrWhiteSpace(terminalName.GetString()))
                issues.Add(new SchemaCheckIssue($"{termPath} is missing.", termPath, FindJsonLineOccurrence(rawJson, "\"terminalName\"\\s*:", idx)));
            if (!p.TryGetProperty("channel", out var channel) || channel.ValueKind != JsonValueKind.Number || !channel.TryGetInt32(out int channelValue))
                issues.Add(new SchemaCheckIssue($"{channelPath} is not a valid number.", channelPath, FindJsonLineOccurrence(rawJson, "\"channel\"\\s*:", idx)));
            else if (channelValue < 0)
                issues.Add(new SchemaCheckIssue($"{channelPath} must be >= 0.", channelPath, FindJsonLineOccurrence(rawJson, "\"channel\"\\s*:", idx)));
            string dir = p.TryGetProperty("direction", out var direction) ? direction.GetString()?.Trim().ToUpperInvariant() ?? string.Empty : string.Empty;
            if (dir is not "I" and not "Q")
                issues.Add(new SchemaCheckIssue($"{dirPath} must be 'I' or 'Q'.", dirPath, FindJsonLineOccurrence(rawJson, "\"direction\"\\s*:", idx)));
        }

        issues.AddRange(CheckUnmappedMembers(rawJson));
        return issues;
    }

    /// <summary>
    /// Deserializes with UnmappedMemberHandling.Disallow so a JSON property that has no
    /// matching member anywhere in the SchemaConfig model (e.g. "plcProjectNam", a typo)
    /// throws instead of being silently dropped. SchemaConfig carries a [JsonExtensionData]
    /// catch-all for legitimate free-form top-level metadata (e.g. "meta"), so only genuinely
    /// unmodeled properties - typically typos in the fields that matter - surface here.
    /// </summary>
    private static List<SchemaCheckIssue> CheckUnmappedMembers(string rawJson)
    {
        try
        {
            JsonSerializer.Deserialize<SchemaConfig>(rawJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            });
            return [];
        }
        catch (JsonException ex)
        {
            // Live 2026-09-26 (#155 c): the issue used to carry "<json>" as its path; the
            // exception knows the exact property path and line, so the caller sees where the typo is.
            string path = string.IsNullOrEmpty(ex.Path) ? "<json>" : ex.Path.TrimStart('$').TrimStart('.');
            int? line = ex.LineNumber is long l ? (int)l + 1 : null;
            string prefix = ex.Message.Contains("could not be mapped", StringComparison.Ordinal)
                ? "Unknown or misspelled JSON property"
                : "JSON value does not match the schema model";
            return [new SchemaCheckIssue($"{prefix}: {ex.Message}", path, line)];
        }
    }

    private static string? TryGetString(JsonElement root, string level1, string level2)
    {
        if (root.TryGetProperty(level1, out var obj) && obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(level2, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private static int? FindJsonLine(string rawJson, string pattern)
    {
        string[] lines = rawJson.Split(["\r\n", "\n"], StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], pattern))
            {
                return i + 1;
            }
        }
        return null;
    }

    private static int? FindJsonLineOccurrence(string rawJson, string pattern, int occurrence)
    {
        if (occurrence < 1) return null;
        string[] lines = rawJson.Split(["\r\n", "\n"], StringSplitOptions.None);
        int hits = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], pattern))
            {
                hits++;
                if (hits == occurrence)
                {
                    return i + 1;
                }
            }
        }
        return null;
    }
}
