using System.Text;
using PtcXaeMcp.WinForms.Security;

namespace PtcXaeMcp.WinForms.TwinCat.Apply;

/// <summary>The export files of one apply run, resolved and allowed (empty = not requested).</summary>
internal sealed record ApplyExportPaths(string XtiPath, string MappingCsvPath);

/// <summary>
/// Files an apply run writes besides the TwinCAT project (issue #155).
/// <list type="bullet">
/// <item>E-22: diagnostics go to <c>%LOCALAPPDATA%\ptc-xae-mcp\diagnostics</c>. They used to go to
/// <c>AppContext.BaseDirectory\artifacts</c>, which fails (or needs elevation) under Program Files.</item>
/// <item>E-25: the schema's export paths are resolved relative to the <b>schema file</b> (not the
/// process working directory) and must pass <see cref="PathPolicy"/> — checked before the run
/// changes anything, so an export cannot overwrite an arbitrary file.</item>
/// </list>
/// </summary>
internal static class ApplyFiles
{
    /// <summary><c>%LOCALAPPDATA%\ptc-xae-mcp\diagnostics</c>.</summary>
    public static string DiagnosticsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ptc-xae-mcp", "diagnostics");

    /// <summary>Writes <paramref name="content"/> to <c>&lt;DiagnosticsFolder&gt;\&lt;name&gt;</c> and returns the path.</summary>
    public static string WriteDiagnostic(string fileName, string content)
    {
        string safe = string.Concat(fileName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        Directory.CreateDirectory(DiagnosticsFolder);
        string path = Path.Combine(DiagnosticsFolder, safe);
        File.WriteAllText(path, content ?? string.Empty, new UTF8Encoding(true));
        return path;
    }

    /// <summary>
    /// Resolves the schema's export paths: relative ones against the schema file's directory;
    /// every path must be allowed by <see cref="PathPolicy"/> (the open solution's directory,
    /// %TEMP%, %LOCALAPPDATA%\ptc-xae-mcp, ...). Throws naming the path otherwise.
    /// </summary>
    public static ApplyExportPaths ResolveExports(string xtiPath, string mappingCsvPath, string schemaPath, string? solutionDirectory)
        => new(
            ResolveExport(xtiPath, "export.xtiPath", schemaPath, solutionDirectory),
            ResolveExport(mappingCsvPath, "export.mappingCsvPath", schemaPath, solutionDirectory));

    internal static string ResolveExport(string configured, string field, string schemaPath, string? solutionDirectory)
    {
        if (string.IsNullOrWhiteSpace(configured)) return string.Empty;

        string schemaDir = Path.GetDirectoryName(Path.GetFullPath(schemaPath)) ?? string.Empty;
        string full = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(schemaDir, configured));

        if (!PathPolicy.IsAllowed(full, solutionDirectory, out string reason))
            throw new InvalidOperationException(
                $"Schema {field} '{configured}' is not an allowed export location: {reason} Nothing was changed.");
        return full;
    }
}
