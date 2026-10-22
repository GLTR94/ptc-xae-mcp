using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>How <c>twincat_plc_project_add_existing</c> takes an existing project into the solution.
/// The value is the <c>nSubType</c> of <c>ITcSmTreeItem.CreateChild</c> on <c>TIPC</c> (Infosys
/// "Bestehendes SPS-Projekt öffnen"; verified live 2026-10-07).</summary>
internal enum PlcProjectAddMode
{
    /// <summary>Copy the project folder next to the solution (subType 0). The source stays.</summary>
    Copy = 0,
    /// <summary>Move the project folder next to the solution (subType 1). The source is gone afterwards.</summary>
    Move = 1,
    /// <summary>Reference the project where it is (subType 2). Nothing is copied.</summary>
    InPlace = 2,
}

/// <summary>
/// Pure, COM-free argument checks of the plc-project-lifecycle tools. Every check runs BEFORE the XAE
/// is called: the live spike (2026-10-07) showed that a bad template name, a project-name collision or
/// a .plcproj whose internal name is already taken each raise a MODAL XAE dialog that blocks the
/// Automation Interface until somebody clicks it — so these cases must never reach CreateChild.
/// </summary>
internal static class PlcProjectLifecycleArgs
{
    public const string StandardTemplate = "Standard PLC Template";
    public const string EmptyTemplate = "Empty PLC Template";

    private static readonly char[] ForbiddenNameChars = ['^', '\\', '/', ':', '*', '?', '"', '<', '>', '|', '.'];

    /// <summary>Null when <paramref name="name"/> is a usable PLC project name, else the reason.</summary>
    public static string? ValidateProjectName(string? name, string paramName = "name")
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"{paramName} is required.";
        if (name != name.Trim())
            return $"{paramName} '{name}' has leading/trailing whitespace.";
        if (name.IndexOfAny(ForbiddenNameChars) >= 0)
            return $"{paramName} '{name}' contains a character that is not allowed in a PLC project name (^ \\ / : * ? \" < > | .).";
        if (name.Length > 100)
            return $"{paramName} '{name}' is longer than 100 characters.";
        return null;
    }

    /// <summary>Null when <paramref name="name"/> is not among <paramref name="existing"/> (case-insensitive).</summary>
    public static string? CheckNameFree(string name, IEnumerable<string> existing) =>
        existing.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase))
            ? $"A PLC project named '{name}' already exists under TIPC — choose another name (the XAE would block with a modal dialog)."
            : null;

    public static string? ParseMode(string? mode, out PlcProjectAddMode parsed)
    {
        parsed = PlcProjectAddMode.Copy;
        switch ((mode ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "COPY": parsed = PlcProjectAddMode.Copy; return null;
            case "MOVE": parsed = PlcProjectAddMode.Move; return null;
            case "IN_PLACE": parsed = PlcProjectAddMode.InPlace; return null;
            case "": return "mode is required: COPY | MOVE | IN_PLACE.";
            default: return $"Unknown mode '{mode}' — expected COPY | MOVE | IN_PLACE.";
        }
    }

    /// <summary>An absolute path to an existing file with the given extension, else the reason.</summary>
    public static string? RequireExistingFile(string? path, string paramName, params string[] extensions)
    {
        string? shape = RequirePathShape(path, paramName, extensions);
        if (shape is not null) return shape;
        return File.Exists(path) ? null : $"{paramName}: file not found: {path}";
    }

    /// <summary>An absolute path with the given extension whose directory exists (the file itself may not).</summary>
    public static string? RequireOutputFile(string? path, string paramName, params string[] extensions)
    {
        string? shape = RequirePathShape(path, paramName, extensions);
        if (shape is not null) return shape;
        string? dir = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(dir) || Directory.Exists(dir)
            ? null
            : $"{paramName}: target directory does not exist: {dir}";
    }

    private static string? RequirePathShape(string? path, string paramName, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(path))
            return $"{paramName} is required.";
        if (!Path.IsPathFullyQualified(path))
            return $"{paramName} must be an absolute path: {path}";
        string ext = Path.GetExtension(path);
        if (!extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
            return $"{paramName} must end with {string.Join(" or ", extensions)}: {path}";
        return null;
    }

    /// <summary>
    /// Resolves <paramref name="template"/> to the source string handed to CreateChild. A Beckhoff
    /// template is passed by NAME (Infosys), but only if a .plcproj of that name exists in one of
    /// <paramref name="templateRoots"/>: an unknown name opens a modal "path not found" dialog in the
    /// XAE (live 2026-10-07). A custom template is a full path to an existing .plcproj.
    /// </summary>
    public static string? ResolveTemplate(string? template, IEnumerable<string> templateRoots, out string source)
    {
        source = string.IsNullOrWhiteSpace(template) ? StandardTemplate : template.Trim();
        if (Path.IsPathFullyQualified(source))
            return RequireExistingFile(source, "template", ".plcproj");

        var available = AvailableTemplateNames(templateRoots);
        string name = source;
        string? match = available.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return available.Count == 0
                ? $"Template '{source}' not found: no Beckhoff PLC templates found in {string.Join("; ", templateRoots)}. Pass the full path of a .plcproj instead."
                : $"Template '{source}' not found. Available: {string.Join(", ", available)} — or pass the full path of a .plcproj.";
        source = match;
        return null;
    }

    /// <summary>Names of the .plcproj templates under the roots (both the 1.0.x flat and the 1.1.x per-folder layout).</summary>
    public static IReadOnlyList<string> AvailableTemplateNames(IEnumerable<string> templateRoots)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in templateRoots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.plcproj", SearchOption.AllDirectories).ToList(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (string f in files) names.Add(Path.GetFileNameWithoutExtension(f));
        }
        return names.ToList();
    }

    /// <summary>Template folders of TC3 4024 and 4026 (Infosys "Wie ein PLC-Projekt angelegt wird").</summary>
    public static IReadOnlyList<string> DefaultTemplateRoots()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var roots = new List<string>
        {
            Path.Combine(programData, "Beckhoff", "TwinCAT", "PlcEngineering", "PlcTemplates"),
            // Up to 4024 (Infosys): C:/TwinCAT3/.xComponents/Plc/PlcTemplate/…
            Path.Combine(Path.GetPathRoot(programData) ?? @"C:\", "TwinCAT3", ".xComponents", "Plc", "PlcTemplate"),
        };
        return roots;
    }

    /// <summary>
    /// The name the XAE gives a project taken in from <paramref name="plcprojPath"/>: CreateChild IGNORES
    /// the name argument for a .plcproj (live 2026-10-07) and uses the &lt;Name&gt; property of the file,
    /// falling back to the file name when the file has none or cannot be parsed.
    /// </summary>
    public static string EffectivePlcprojName(string plcprojPath)
    {
        try
        {
            var doc = XDocument.Load(plcprojPath);
            string? name = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name" && e.Parent?.Name.LocalName == "PropertyGroup")?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException) { }
        return Path.GetFileNameWithoutExtension(plcprojPath);
    }

    /// <summary>
    /// The instance name the XAE gives a PLC instance created from <paramref name="tmcPath"/>: CreateChild
    /// IGNORES the name argument and uses the first module's &lt;Name&gt; (live 2026-10-07). Null when the
    /// file has no module.
    /// </summary>
    public static string? TmcModuleName(string tmcPath)
    {
        try
        {
            var doc = XDocument.Load(tmcPath);
            var module = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Module");
            string? name = module?.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
