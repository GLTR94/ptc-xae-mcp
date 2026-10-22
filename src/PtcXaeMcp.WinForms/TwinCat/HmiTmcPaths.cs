using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// hmi-ads-tmc: the storage format TE2000 uses for attached
/// TMC files. Mirrored from the decompiled engineering
/// (TcHmiProjectMiddleware, TcHmiServerSymbolRootLazy.GetTmcStoragePath / ResolveTmcPath /
/// AttachTmcFile, TE2000 14.17.22.1) so the MCP's pre-checks and read-back compare the
/// SAME strings the product writes — otherwise a duplicate slips past our checks and the
/// XAE answers with a modal message box nobody can click.
/// </summary>
internal static class HmiTmcPaths
{
    /// <summary>MSBuild project property in the .hmiproj that holds the attached TMC files
    /// as a JSON array of { FileName, RootNamespace, PlcName }.</summary>
    internal const string ProjectProperty = "HmiTmcFiles";

    /// <summary>
    /// How TE2000 stores the path: relative to the solution directory when the file lives
    /// below it, absolute otherwise. A relative path that would climb out ("..") is stored
    /// absolute too. Mirrors GetTmcStoragePath.
    /// </summary>
    internal static string StoragePath(string absolutePath, string? solutionDirectory)
    {
        if (string.IsNullOrEmpty(solutionDirectory)) return absolutePath;
        string relative;
        try { relative = Path.GetRelativePath(solutionDirectory, absolutePath); }
        catch (ArgumentException) { return absolutePath; }

        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) return absolutePath;
        string normalized = relative.Replace('/', '\\');
        return normalized == ".." || normalized.StartsWith(@"..\", StringComparison.Ordinal)
            ? absolutePath
            : normalized;
    }

    /// <summary>The reverse: a stored path back to an absolute one. Mirrors ResolveTmcPath.</summary>
    internal static string Resolve(string storedPath, string? solutionDirectory)
    {
        if (string.IsNullOrEmpty(storedPath)) return storedPath;
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(storedPath) || string.IsNullOrEmpty(solutionDirectory)
                ? storedPath
                : Path.Combine(solutionDirectory, storedPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return storedPath;
        }
    }

    /// <summary>
    /// Our own guard on the virtual PLC name. TE2000 itself derives it from the file name
    /// and validates nothing, but the name becomes the ROOT of a symbol path, so anything
    /// with a separator, a namespace delimiter or whitespace in it cannot work. Deliberately
    /// permissive otherwise: a PLC project name may start with a digit.
    /// </summary>
    internal static bool IsValidVirtualPlcName(string? name)
    {
        string n = name?.Trim() ?? "";
        if (n.Length == 0 || n.Length > 64) return false;
        foreach (char c in n)
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
        return true;
    }

    /// <summary>The single .hmiproj of a project directory, or null when it is not unique.</summary>
    internal static string? FindProjectFile(string? projectDirectory, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory)) return null;
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            string expected = Path.Combine(projectDirectory, projectName + ".hmiproj");
            if (File.Exists(expected)) return expected;
        }
        try
        {
            string[] candidates = Directory.GetFiles(projectDirectory, "*.hmiproj", SearchOption.TopDirectoryOnly);
            return candidates.Length == 1 ? candidates[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Attached TMC files of a saved .hmiproj. Returns an empty list when the property is
    /// absent (nothing attached) and null when the file cannot be read or parsed — callers
    /// must not read "no property" and "cannot tell" as the same thing.
    /// </summary>
    internal static IReadOnlyList<HmiTmcAttachment>? ReadAttachments(string? projectFile, string? solutionDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile)) return null;
        string? json;
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(projectFile);
            json = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals(ProjectProperty, StringComparison.Ordinal))
                ?.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
        if (json is null) return Array.Empty<HmiTmcAttachment>();
        return ParseAttachments(json, solutionDirectory);
    }

    /// <summary>Parses the property's JSON array. null = not the expected shape.</summary>
    internal static IReadOnlyList<HmiTmcAttachment>? ParseAttachments(string? json, string? solutionDirectory)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<HmiTmcAttachment>();
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
        if (root.ValueKind != JsonValueKind.Array) return null;

        var list = new List<HmiTmcAttachment>();
        foreach (JsonElement entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) return null;
            string stored = Text(entry, "FileName") ?? "";
            string resolved = Resolve(stored, solutionDirectory);
            list.Add(new HmiTmcAttachment(
                Text(entry, "RootNamespace") ?? "",
                stored,
                Text(entry, "PlcName"),
                resolved,
                SafeExists(resolved)));
        }
        return list;
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
        && !string.IsNullOrEmpty(v.GetString())
            ? v.GetString()
            : null;

    private static bool SafeExists(string path)
    {
        try { return path.Length > 0 && File.Exists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
