using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// One EtherCAT box/terminal to create, optionally with a nested subtree.
/// Mirrors the <c>ethercat</c> node shape consumed by <c>twincat_apply_schema</c>
/// (<c>{ name, vInfo, previousPort, children[] }</c>) so an agent can replay a
/// schema slice — or a single box — through the granular add tool.
/// </summary>
internal sealed record EtherCatBoxSpec(
    string VInfo,
    string? Name = null,
    string? PreviousPort = null,
    IReadOnlyList<EtherCatBoxSpec>? Children = null)
{
    public IReadOnlyList<EtherCatBoxSpec> ChildList =>
        Children ?? Array.Empty<EtherCatBoxSpec>();

    /// <summary>Total node count of this spec including itself and descendants.</summary>
    public int NodeCount => 1 + ChildList.Sum(c => c.NodeCount);
}

/// <summary>
/// Parses the box JSON accepted by <c>twincat_add_ethercat_box</c>. Tolerant of
/// the several shapes a caller is likely to paste:
/// <list type="bullet">
/// <item>a single node object <c>{ "vInfo": "EK1100", ... }</c></item>
/// <item>an array of node objects (siblings)</item>
/// <item>a schema fragment <c>{ "root": { ... } }</c></item>
/// <item>a full schema file <c>{ "ethercat": { "root": { ... } } }</c></item>
/// </list>
/// </summary>
internal static class EtherCatSpecJson
{
    public static IReadOnlyList<EtherCatBoxSpec> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("box JSON is empty.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"box JSON is not valid JSON: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return root.EnumerateArray().Select(MapNode).ToList();

            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("box JSON must be an object or an array of nodes.");

            // Unwrap full-schema / schema-fragment shapes.
            if (root.TryGetProperty("ethercat", out var ec) &&
                ec.ValueKind == JsonValueKind.Object &&
                ec.TryGetProperty("root", out var ecRoot))
                return [MapNode(ecRoot)];

            if (root.TryGetProperty("root", out var schemaRoot) &&
                schemaRoot.ValueKind == JsonValueKind.Object)
                return [MapNode(schemaRoot)];

            return [MapNode(root)];
        }
    }

    private static EtherCatBoxSpec MapNode(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("each EtherCAT node must be a JSON object.");

        string vInfo = el.TryGetProperty("vInfo", out var v) ? v.GetString()?.Trim() ?? "" : "";
        if (string.IsNullOrWhiteSpace(vInfo))
            throw new InvalidOperationException("each EtherCAT node requires a non-empty 'vInfo'.");

        string? name = el.TryGetProperty("name", out var n) ? n.GetString() : null;
        string? prevPort = el.TryGetProperty("previousPort", out var pp) ? pp.GetString() : null;

        List<EtherCatBoxSpec>? children = null;
        if (el.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
            children = ch.EnumerateArray().Select(MapNode).ToList();

        return new EtherCatBoxSpec(vInfo,
            string.IsNullOrWhiteSpace(name) ? null : name!.Trim(),
            string.IsNullOrWhiteSpace(prevPort) ? null : prevPort!.Trim(),
            children);
    }
}
