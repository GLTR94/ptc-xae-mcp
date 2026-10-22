using System.Text.Json;
using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

// hmi-internal-symbols (issue #165). Two different things
// carry the name "internal symbol" in TE2000, and both are read back from the files the product
// writes, never from what the Automation Interface returns (decompiled TE2000 14.17.22.1):
//   A. CLIENT (framework) internal symbols %i%…%/i% — ITcHmiProject.AddInternalSymbol /
//      RemoveInternalSymbol, persisted in Properties\tchmiconfig.json under
//      symbols.internal.<name> = { value, type, persist, readonly }. RemoveInternalSymbol
//      returns false ALWAYS, even on success.
//   B. SERVER symbols in the TcHmiSrv domain — ITcHmiServer.MapOfflineSymbol, persisted by the
//      server in Server\TcHmiSrv\TcHmiSrv.Config.default.json under SYMBOLS.<mapName> =
//      { ACCESS, DOMAIN:"TcHmiSrv", MAPPING, OPTIONS, SCHEMA, USEMAPPING, … }.

/// <summary>A client (framework) internal symbol <c>%i%Name%/i%</c> from tchmiconfig.json.</summary>
/// <param name="ValueJson">The stored default value as raw JSON text (null when absent).</param>
internal sealed record HmiClientInternalSymbol(
    string Name,
    string? Type,
    string? ValueJson,
    bool Persist,
    bool ReadOnly);

/// <summary>One entry of the server's <c>SYMBOLS</c> table.</summary>
/// <param name="UseMapping">USEMAPPING — false for a pure server-held (offline) variable.</param>
/// <param name="DataType">SCHEMA.type, SCHEMA.$ref or the $ref inside SCHEMA.allOf.</param>
/// <param name="DefaultJson">SCHEMA.default (or the default inside allOf) as raw JSON.</param>
/// <param name="PersistedValueJson">OPTIONS.persistedOfflineValue as raw JSON — present only
/// when the symbol was created with persist=true.</param>
internal sealed record HmiServerSymbolEntry(
    string Name,
    string Domain,
    string? Mapping,
    bool UseMapping,
    bool Hidden,
    int? Access,
    string? DataType,
    string? DefaultJson,
    bool Persist,
    string? PersistedValueJson);

internal static partial class HmiInternalSymbols
{
    /// <summary>Client internal symbols, relative to the HMI project directory.</summary>
    internal const string ClientConfigRelativePath = @"Properties\tchmiconfig.json";

    /// <summary>The server's own configuration, relative to the HMI project directory.</summary>
    internal const string ServerConfigRelativePath = @"Server\TcHmiSrv\TcHmiSrv.Config.default.json";

    /// <summary>Domain MapOfflineSymbol always sends (decompiled SrvComm.MapSymbol).</summary>
    internal const string ServerDomain = "TcHmiSrv";

    /// <summary>Live config path of one server symbol — the same pattern as ADS.Config::RUNTIMES.</summary>
    internal static string ServerConfigSymbol(string mapName) => $"{ServerDomain}.Config::SYMBOLS::{mapName}";

    internal const int MaxNameLength = 128;
    internal const int MaxMapNameLength = 256;

    /// <summary>JSON-schema primitive types the server accepts as <c>{ "type": … }</c>.</summary>
    internal static readonly string[] PrimitiveTypes = ["boolean", "number", "integer", "string"];

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ClientNameRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex ServerNameRegex();

    // tchmi:general#/definitions/BOOL, tchmi:framework#/definitions/TcHmi.Functions.BaseTemplate.MediaQueryList
    [GeneratedRegex(@"^tchmi:[A-Za-z][A-Za-z0-9_\-]*#/definitions/[A-Za-z_][A-Za-z0-9_.\-]*$")]
    private static partial Regex TchmiRefRegex();

    /// <summary>A client internal symbol is referenced as <c>%i%Name%/i%</c>, so the name must
    /// not contain '%', '/', ':' or whitespace. Deliberately strict: letters, digits, '_'.</summary>
    internal static bool IsValidClientName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength } && ClientNameRegex().IsMatch(name);

    /// <summary>A server symbol name: dotted identifiers, like every mapped name
    /// (<c>ADS.PLC1.MAIN.bStart</c>). No "::" — that is the server's own path separator.</summary>
    internal static bool IsValidServerName(string? name) =>
        name is { Length: > 0 and <= MaxMapNameLength } && ServerNameRegex().IsMatch(name);

    /// <summary>Either a <c>tchmi:&lt;schema&gt;#/definitions/&lt;Type&gt;</c> reference (the form
    /// tchmiconfig.json uses) or one of <see cref="PrimitiveTypes"/>.</summary>
    internal static bool IsValidDataType(string? dataType) =>
        !string.IsNullOrWhiteSpace(dataType)
        && (PrimitiveTypes.Contains(dataType, StringComparer.Ordinal) || TchmiRefRegex().IsMatch(dataType));

    internal const string DataTypeHint =
        "dataType must be a TwinCAT HMI type reference such as 'tchmi:general#/definitions/Boolean', " +
        "'tchmi:general#/definitions/INT16' or 'tchmi:general#/definitions/String', or one of the JSON-schema " +
        "types boolean, number, integer, string.";

    private enum ValueKind { Any, Boolean, Number, Integer, String }

    private static readonly HashSet<string> NumberDefinitions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Number", "REAL", "LREAL", "Float", "Double",
    };

    private static readonly HashSet<string> IntegerDefinitions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Integer", "SINT", "INT", "DINT", "LINT", "USINT", "UINT", "UDINT", "ULINT", "BYTE", "WORD", "DWORD", "LWORD",
        "INT8", "INT16", "INT32", "INT64", "UINT8", "UINT16", "UINT32", "UINT64",
    };

    private static ValueKind KindOf(string dataType)
    {
        switch (dataType)
        {
            case "boolean": return ValueKind.Boolean;
            case "number": return ValueKind.Number;
            case "integer": return ValueKind.Integer;
            case "string": return ValueKind.String;
        }
        int hash = dataType.LastIndexOf('/');
        string definition = hash >= 0 ? dataType[(hash + 1)..] : dataType;
        if (definition.Equals("Boolean", StringComparison.OrdinalIgnoreCase)
            || definition.Equals("BOOL", StringComparison.OrdinalIgnoreCase)) return ValueKind.Boolean;
        if (definition.Equals("String", StringComparison.OrdinalIgnoreCase)
            || definition.StartsWith("STRING", StringComparison.OrdinalIgnoreCase)) return ValueKind.String;
        if (IntegerDefinitions.Contains(definition)) return ValueKind.Integer;
        if (NumberDefinitions.Contains(definition)) return ValueKind.Number;
        return ValueKind.Any; // a framework/project type we do not know — the engine decides
    }

    /// <summary>
    /// Validates an optional default value. Only JSON scalars (or null) are accepted: the value
    /// crosses the out-of-proc COM boundary as a VARIANT, and an object/array would not survive
    /// it. For a known scalar type the value's kind must match. Returns the normalised JSON text
    /// (null when no default was given).
    /// </summary>
    internal static bool TryNormalizeDefault(string? defaultValueJson, string dataType,
        out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(defaultValueJson)) return true;

        JsonElement value;
        try
        {
            using var doc = JsonDocument.Parse(defaultValueJson);
            value = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            error = $"defaultValueJson is not valid JSON ({ex.Message}). Strings need quotes: \"text\".";
            return false;
        }

        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            error = "defaultValueJson must be a JSON scalar (true, 42, 3.5, \"text\" or null) — objects and " +
                    "arrays cannot cross the Automation Interface as a default value.";
            return false;
        }

        ValueKind expected = KindOf(dataType);
        bool fits = value.ValueKind == JsonValueKind.Null || expected switch
        {
            ValueKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ValueKind.Number => value.ValueKind == JsonValueKind.Number,
            ValueKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            ValueKind.String => value.ValueKind == JsonValueKind.String,
            _ => true,
        };
        if (!fits)
        {
            error = $"defaultValueJson {value.GetRawText()} does not fit dataType '{dataType}' " +
                    $"(expected {expected.ToString().ToLowerInvariant()}).";
            return false;
        }

        normalized = value.GetRawText();
        return true;
    }

    /// <summary>The VARIANT-friendly .NET value for a normalised scalar JSON default.</summary>
    internal static object? ToComValue(string? scalarJson)
    {
        if (string.IsNullOrWhiteSpace(scalarJson)) return null;
        using var doc = JsonDocument.Parse(scalarJson);
        JsonElement v = doc.RootElement;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number when v.TryGetInt32(out int i) => i,
            JsonValueKind.Number when v.TryGetInt64(out long l) => l,
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException("Only JSON scalars can be passed as a default value."),
        };
    }

    /// <summary>Do two JSON texts denote the same value? Both null/absent also counts as equal.</summary>
    internal static bool SameJson(string? a, string? b)
    {
        bool aEmpty = string.IsNullOrWhiteSpace(a) || a.Trim() == "null";
        bool bEmpty = string.IsNullOrWhiteSpace(b) || b.Trim() == "null";
        if (aEmpty || bEmpty) return aEmpty && bEmpty;
        try
        {
            return System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(a!), System.Text.Json.Nodes.JsonNode.Parse(b!));
        }
        catch (JsonException) { return false; }
    }

    // ---- A. client internal symbols (tchmiconfig.json) ------------------------

    /// <summary>
    /// Parses tchmiconfig.json into its client internal symbols. A file without
    /// <c>symbols</c> or <c>symbols.internal</c> has none (empty list); null = not a config
    /// object, so callers fail closed instead of reading it as "none".
    /// </summary>
    internal static IReadOnlyList<HmiClientInternalSymbol>? ParseClientSymbols(string? json)
    {
        if (json is null) return null;
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
        if (root.ValueKind != JsonValueKind.Object) return null;

        JsonElement symbols = Member(root, "symbols");
        if (symbols.ValueKind == JsonValueKind.Undefined || symbols.ValueKind == JsonValueKind.Null)
            return Array.Empty<HmiClientInternalSymbol>();
        if (symbols.ValueKind != JsonValueKind.Object) return null;
        JsonElement internals = Member(symbols, "internal");
        if (internals.ValueKind == JsonValueKind.Undefined || internals.ValueKind == JsonValueKind.Null)
            return Array.Empty<HmiClientInternalSymbol>();
        if (internals.ValueKind != JsonValueKind.Object) return null;

        var list = new List<HmiClientInternalSymbol>();
        foreach (JsonProperty entry in internals.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) return null;
            JsonElement value = Member(entry.Value, "value");
            list.Add(new HmiClientInternalSymbol(
                entry.Name,
                Text(entry.Value, "type"),
                value.ValueKind == JsonValueKind.Undefined ? null : value.GetRawText(),
                Bool(entry.Value, "persist") ?? false,
                Bool(entry.Value, "readonly") ?? false));
        }
        return list.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Reads the client internal symbols of an HMI project directory from disk.</summary>
    internal static (IReadOnlyList<HmiClientInternalSymbol>? Symbols, string Path, string? Error) ReadClientSymbols(
        string projectDirectory)
    {
        string path = System.IO.Path.Combine(projectDirectory, ClientConfigRelativePath);
        if (!File.Exists(path))
            return (null, path, $"'{path}' does not exist — is '{projectDirectory}' a TwinCAT HMI project directory?");
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, path, $"'{path}' could not be read ({ex.Message}).");
        }
        IReadOnlyList<HmiClientInternalSymbol>? parsed = ParseClientSymbols(text);
        return parsed is null
            ? (null, path, $"'{path}' is not a tchmiconfig object with a symbols.internal table.")
            : (parsed, path, null);
    }

    // ---- B. server symbols (TcHmiSrv.Config.default.json) ---------------------

    /// <summary>
    /// Parses the server's config file into its <c>SYMBOLS</c> table. An empty file is what the
    /// project template ships before the server first started: no symbols. null = not the
    /// expected shape (fail closed).
    /// </summary>
    internal static IReadOnlyList<HmiServerSymbolEntry>? ParseServerConfig(string? json)
    {
        if (json is null) return null;
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<HmiServerSymbolEntry>();
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
        if (root.ValueKind != JsonValueKind.Object) return null;

        JsonElement symbols = Member(root, "SYMBOLS");
        if (symbols.ValueKind == JsonValueKind.Undefined || symbols.ValueKind == JsonValueKind.Null)
            return Array.Empty<HmiServerSymbolEntry>();
        if (symbols.ValueKind != JsonValueKind.Object) return null;

        var list = new List<HmiServerSymbolEntry>();
        foreach (JsonProperty entry in symbols.EnumerateObject())
        {
            HmiServerSymbolEntry? parsed = ToServerEntry(entry.Name, entry.Value);
            if (parsed is null) return null;
            list.Add(parsed);
        }
        return list.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>One server symbol as the live config read (<see cref="ServerConfigSymbol"/>)
    /// returns it. null = not a symbol object.</summary>
    internal static HmiServerSymbolEntry? ParseServerSymbolEntry(string name, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ToServerEntry(name, doc.RootElement.Clone());
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Reads the server symbol table of an HMI project directory from disk.</summary>
    internal static (IReadOnlyList<HmiServerSymbolEntry>? Symbols, string Path, string? Error) ReadServerSymbols(
        string projectDirectory)
    {
        string path = System.IO.Path.Combine(projectDirectory, ServerConfigRelativePath);
        if (!File.Exists(path))
            return (null, path, $"'{path}' does not exist — the HMI server has not written its configuration yet.");
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, path, $"'{path}' could not be read ({ex.Message}).");
        }
        IReadOnlyList<HmiServerSymbolEntry>? parsed = ParseServerConfig(text);
        return parsed is null
            ? (null, path, $"'{path}' is not a server configuration object with a SYMBOLS table.")
            : (parsed, path, null);
    }

    /// <summary>The symbols an engineer created in the TcHmiSrv domain: the server's own
    /// (hidden) function symbols are left out.</summary>
    internal static bool IsUserServerSymbol(HmiServerSymbolEntry e) =>
        e.Domain.Equals(ServerDomain, StringComparison.OrdinalIgnoreCase) && !e.Hidden;

    private static HmiServerSymbolEntry? ToServerEntry(string name, JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        JsonElement schema = Member(e, "SCHEMA");
        var (dataType, defaultJson) = DescribeSchema(schema);
        JsonElement options = Member(e, "OPTIONS");
        JsonElement persisted = Member(options, "persistedOfflineValue");
        return new HmiServerSymbolEntry(
            name,
            Text(e, "DOMAIN") ?? "",
            Member(e, "MAPPING") is { ValueKind: JsonValueKind.String } m ? m.GetString() : null,
            Bool(e, "USEMAPPING") ?? false,
            Bool(e, "HIDDEN") ?? false,
            Member(e, "ACCESS") is { ValueKind: JsonValueKind.Number } a && a.TryGetInt32(out int access) ? access : null,
            dataType,
            defaultJson,
            persisted.ValueKind != JsonValueKind.Undefined,
            persisted.ValueKind == JsonValueKind.Undefined ? null : persisted.GetRawText());
    }

    /// <summary>
    /// The schema shapes OfflineSymbolSchema.GetJson writes: <c>{ type, default }</c>,
    /// <c>{ $ref }</c> or <c>{ allOf: [ { $ref }, { default } ] }</c>.
    /// </summary>
    internal static (string? DataType, string? DefaultJson) DescribeSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return (null, null);
        string? type = Text(schema, "$ref") ?? Text(schema, "type");
        JsonElement def = Member(schema, "default");
        string? defaultJson = def.ValueKind == JsonValueKind.Undefined ? null : def.GetRawText();
        if (Member(schema, "allOf") is { ValueKind: JsonValueKind.Array } allOf)
            foreach (JsonElement part in allOf.EnumerateArray())
            {
                type ??= Text(part, "$ref") ?? Text(part, "type");
                if (defaultJson is null && Member(part, "default") is { ValueKind: not JsonValueKind.Undefined } d)
                    defaultJson = d.GetRawText();
            }
        return (type, defaultJson);
    }

    private static JsonElement Member(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement value) ? value : default;

    private static bool? Bool(JsonElement obj, string name) => Member(obj, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static string? Text(JsonElement obj, string name) =>
        Member(obj, name) is { ValueKind: JsonValueKind.String } v && !string.IsNullOrEmpty(v.GetString())
            ? v.GetString()
            : null;

    internal static string Describe(HmiClientInternalSymbol s) =>
        $"{s.Name} ({s.Type ?? "?"}" + (s.ValueJson is null ? "" : $" = {Short(s.ValueJson)}") +
        (s.Persist ? ", persist" : "") + (s.ReadOnly ? ", read-only" : "") + ")";

    internal static string Describe(HmiServerSymbolEntry s) =>
        $"{s.Name} ({s.DataType ?? "?"}" + (s.DefaultJson is null ? "" : $" = {Short(s.DefaultJson)}") +
        (s.UseMapping ? $", online → '{s.Mapping}'" : ", offline") + (s.Persist ? ", persist" : "") + ")";

    private static string Short(string json) =>
        json.Length <= 40 ? json : json[..40] + "…";
}
