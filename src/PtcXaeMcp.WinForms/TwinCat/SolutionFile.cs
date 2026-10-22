using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// What kind of projects a .sln declares, read from its Project(...) lines. Used by
/// open_project to tell an HMI-only solution (TE2000, no .tsproj) apart from a TwinCAT
/// solution: waiting for a System Manager there cost 120 s and ended in an [Error] although
/// the solution was open (seen live 2026-09-25 with HMI-only fixture solutions).
/// </summary>
internal static partial class SolutionFile
{
    [GeneratedRegex(@"^Project\(""\{[0-9A-Fa-f-]+\}""\)\s*=\s*""[^""]*""\s*,\s*""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    internal readonly record struct Kinds(bool HasTwinCat, bool HasHmi);

    public static Kinds Classify(string slnText)
    {
        bool twinCat = false, hmi = false;
        foreach (Match m in ProjectLine().Matches(slnText))
        {
            string path = m.Groups[1].Value;
            if (path.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".tspproj", StringComparison.OrdinalIgnoreCase)) twinCat = true;
            else if (path.EndsWith(".hmiproj", StringComparison.OrdinalIgnoreCase)) hmi = true;
        }
        return new Kinds(twinCat, hmi);
    }

    private const string TwinCatTypeGuid = "{B1E792BE-AA5F-4E3C-8C82-674BF9C0715B}";
    private const string HmiTypeGuid = "{FE7A1B72-C5B7-4D7C-BB7D-76384D4DE8E1}";

    public static bool IsSolution(string path) => path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);

    public static bool IsProjectFile(string path) =>
        path.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tspproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".hmiproj", StringComparison.OrdinalIgnoreCase);

    /// <summary>Project paths a solution declares, as written (possibly relative to the .sln).</summary>
    public static IEnumerable<string> ProjectPaths(string slnText) =>
        ProjectLine().Matches(slnText).Select(m => m.Groups[1].Value);

    /// <summary>
    /// The text of a one-project solution. Teams commit the project file (.tsproj / .hmiproj)
    /// and gitignore the .sln, so open_project builds this throwaway solution around it.
    /// </summary>
    public static string SolutionTextFor(string projectPath)
    {
        string typeGuid = projectPath.EndsWith(".hmiproj", StringComparison.OrdinalIgnoreCase)
            ? HmiTypeGuid : TwinCatTypeGuid;
        string name = Path.GetFileNameWithoutExtension(projectPath);
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(projectPath.ToLowerInvariant()));
        string projectGuid = new Guid(hash.AsSpan(0, 16)).ToString("B").ToUpperInvariant();
        return string.Join("\r\n",
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            "# TcXaeShell Solution File, Format Version 11.00",
            $"Project(\"{typeGuid}\") = \"{name}\", \"{projectPath}\", \"{projectGuid}\"",
            "EndProject", "");
    }

    /// <summary>
    /// A .sln path is returned as is. A project file gets a generated solution under
    /// %TEMP%\ptc-xae-mcp\solutions\&lt;hash&gt; (rewritten on every call, so it never goes stale).
    /// </summary>
    public static string ResolveSolution(string path)
    {
        if (IsSolution(path)) return path;
        string full = Path.GetFullPath(path);
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(full.ToLowerInvariant()));
        string dir = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp", "solutions",
            Convert.ToHexString(hash, 0, 4));
        Directory.CreateDirectory(dir);
        string sln = Path.Combine(dir, Path.GetFileNameWithoutExtension(full) + ".sln");
        File.WriteAllText(sln, SolutionTextFor(full));
        return sln;
    }

    /// <summary>True only when the file is readable AND declares an .hmiproj but no TwinCAT
    /// project. Anything unclear keeps the classic "wait for the System Manager" path.</summary>
    public static bool IsHmiOnly(string slnPath)
    {
        try
        {
            var k = Classify(File.ReadAllText(slnPath));
            return k.HasHmi && !k.HasTwinCat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
