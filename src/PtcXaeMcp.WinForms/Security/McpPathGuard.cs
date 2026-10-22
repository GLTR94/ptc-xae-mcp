using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.WinForms.Security;

/// <summary>
/// Thin adapter between the MCP tool layer's "[Error] ..." string
/// convention and <see cref="PathPolicy"/>. Every MCP tool parameter that
/// accepts an arbitrary file path calls one of these at the top, before any
/// COM call or filesystem access, so a disallowed path never reaches a
/// coordinator (#156 item 7).
/// </summary>
internal static class McpPathGuard
{
    /// <summary>Null (nothing to check) when <paramref name="path"/> is
    /// null/empty; an "[Error] ..." string when it is set but disallowed;
    /// otherwise null (allowed).</summary>
    public static string? CheckOptionalFile(string? path, ITwinCatSession session, string paramName) =>
        CheckOptionalFile(path, SolutionDirectory(session), paramName);

    public static string? CheckOptionalFile(string? path, string? solutionDirectory, string paramName)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return PathPolicy.IsAllowed(path, solutionDirectory, out string reason)
            ? null
            : $"[Error] {paramName}: {reason}";
    }

    /// <summary>Same as <see cref="CheckOptionalFile"/> but for a required
    /// path parameter — an empty/null path is itself the error.</summary>
    public static string? CheckRequiredFile(string path, ITwinCatSession session, string paramName) =>
        CheckRequiredFile(path, SolutionDirectory(session), paramName);

    public static string? CheckRequiredFile(string path, string? solutionDirectory, string paramName) =>
        PathPolicy.IsAllowed(path, solutionDirectory, out string reason)
            ? null
            : $"[Error] {paramName}: {reason}";

    /// <summary>umr_start's exePath: an .exe under a local TwinCAT
    /// installation, never an arbitrary executable.</summary>
    public static string? CheckOptionalRuntimeExe(string? exePath, string paramName)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        return PathPolicy.IsAllowedRuntimeExe(exePath, out string reason)
            ? null
            : $"[Error] {paramName}: {reason}";
    }

    private static string? SolutionDirectory(ITwinCatSession session)
    {
        try
        {
            string? solutionPath = session.ActiveSolutionPath();
            return string.IsNullOrEmpty(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
        }
        catch
        {
            return null;
        }
    }
}
