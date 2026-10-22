using System.Globalization;
using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Issue #152 C-10 / C-19: when a build counts as succeeded, and how an Error List item is
/// classified. Pure, so the rules are unit-tested without an XAE.
/// </summary>
internal static class BuildTruth
{
    /// <summary>Slack for file-system timestamp resolution when judging artifact freshness.</summary>
    internal static readonly TimeSpan TimestampSlack = TimeSpan.FromSeconds(2);

    private static readonly Regex SummaryCounts = new(
        @"(?<errors>\d+)\s+(errors?|Fehler)\s*,\s*(?<warnings>\d+)\s+(warnings?|Warnungen)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// C-10: "Build succeeded" used to come from <c>LastBuildInfo == 0</c> alone — also reported
    /// when nothing was translated. A PLC build succeeds only when, in addition, THIS build's output
    /// has the compiler summary line, that line counts 0 errors, and the newest compile artifact
    /// was written by this build. Returns null when the build succeeded, otherwise why not.
    /// </summary>
    /// <param name="expectsPlcCompile">False for a solution without a PLC project: then there is no
    /// compiler summary or PLC artifact to demand, and LastBuildInfo decides.</param>
    public static string? FailureReason(int lastBuildInfo, string? compilerSummary, BuildArtifactInfo? artifact,
        DateTime buildStartedUtc, bool expectsPlcCompile)
    {
        if (lastBuildInfo != 0)
            return $"{lastBuildInfo} project(s) failed to build (SolutionBuild.LastBuildInfo={lastBuildInfo}).";
        if (!expectsPlcCompile) return null;

        if (compilerSummary is null)
            return "LastBuildInfo reports success, but this build's output has no compiler summary line " +
                   "(\"N errors, M warnings\") — nothing appears to have been translated.";
        int? errors = SummaryErrorCount(compilerSummary);
        if (errors is null)
            return $"the compiler summary '{compilerSummary}' could not be parsed.";
        if (errors > 0)
            return $"the compiler reports {compilerSummary}.";
        if (artifact is null)
            return "LastBuildInfo reports success, but no compile artifact (*.compileinfo) was found.";
        if (artifact.LastWriteUtc < buildStartedUtc - TimestampSlack)
            return $"LastBuildInfo reports success, but the newest compile artifact {artifact.Path} was written " +
                   $"{artifact.LastWriteUtc:yyyy-MM-dd HH:mm:ss}Z, before this build started — it is stale.";
        return null;
    }

    /// <summary>
    /// The compiler's own summary line ("N errors, M warnings", also the German XAE wording) from a
    /// build log, or null when there is none. The LAST match wins: a log can contain per-project
    /// lines followed by the overall summary.
    /// </summary>
    public static string? ExtractCompilerSummary(string? log)
    {
        if (string.IsNullOrEmpty(log)) return null;
        Match? last = null;
        foreach (Match m in SummaryCounts.Matches(log))
            last = m;
        return last?.Value;
    }

    /// <summary>Hard backstop so one pathological build cannot blow the tool-result limit.</summary>
    internal const int MaxBuildLogChars = 20_000;

    /// <summary>
    /// Caps a build log at <see cref="MaxBuildLogChars"/> characters, keeping the TAIL (the compiler
    /// summary and the errors are at the end) behind a one-line note of how much was omitted.
    /// </summary>
    public static string CapBuildLog(string log)
    {
        if (log.Length <= MaxBuildLogChars) return log;
        int cut = log.Length - MaxBuildLogChars;
        return string.Create(CultureInfo.InvariantCulture,
            $"[… {cut} earlier characters omitted — showing the last {MaxBuildLogChars} …]\n") + log[cut..];
    }

    /// <summary>The error count of a compiler summary line ("2 errors, 0 warnings" → 2), or null.</summary>
    public static int? SummaryErrorCount(string summary)
    {
        var m = SummaryCounts.Match(summary);
        return m.Success && int.TryParse(m.Groups["errors"].Value, out int n) ? n : null;
    }

    /// <summary>
    /// C-19: classifies an Error List item by <c>ErrorItem.ErrorLevel</c> (EnvDTE80.vsBuildErrorLevel:
    /// Low=1 message, Medium=2 warning, High=4 error). Only an unreadable/unknown level falls back to
    /// keywords in the description. Returns "error", "warning" or "message".
    /// </summary>
    public static string ClassifyErrorLevel(int? level, string description) => level switch
    {
        4 => "error",
        2 => "warning",
        1 => "message",
        _ => description.Contains("warning", StringComparison.OrdinalIgnoreCase) ? "warning"
           : description.Contains("error", StringComparison.OrdinalIgnoreCase)
             || description.Contains("fehler", StringComparison.OrdinalIgnoreCase) ? "error"
           : "message",
    };

    // "<file>(<line>) : error: C0046: <description>" — the PLC compiler's own severity tag.
    private static readonly Regex CompilerLine = new(
        @":\s*(?<sev>error|warning|fehler|warnung)\s*:\s*(?:C\d+\s*:\s*)?(?<desc>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Issue #178: classifies an Error List item. Seen live on TcXaeShell 4026.22, PLC compiler
    /// errors AND warnings both carry ErrorLevel 2, so level 2 (or an unreadable level) is decided
    /// by the compiler line for the same description in <paramref name="compilerOutput"/> (the
    /// Output window text; the latest matching line wins). Level 4 and 1 are taken as they are;
    /// without a matching line <see cref="ClassifyErrorLevel"/> decides.
    /// </summary>
    public static string ClassifyErrorItem(int? level, string description, string? compilerOutput)
    {
        if (level is 4 or 1) return ClassifyErrorLevel(level, description);
        string? fromOutput = SeverityFromCompilerOutput(description, compilerOutput);
        return fromOutput ?? ClassifyErrorLevel(level, description);
    }

    private static string? SeverityFromCompilerOutput(string description, string? compilerOutput)
    {
        if (string.IsNullOrWhiteSpace(compilerOutput) || string.IsNullOrWhiteSpace(description)) return null;
        string wanted = description.Trim();
        string? severity = null;
        foreach (string line in compilerOutput.Split('\n'))
        {
            if (!line.Contains(wanted, StringComparison.Ordinal)) continue;
            var m = CompilerLine.Match(line);
            if (!m.Success || !string.Equals(m.Groups["desc"].Value, wanted, StringComparison.Ordinal)) continue;
            string sev = m.Groups["sev"].Value;
            severity = sev.Equals("error", StringComparison.OrdinalIgnoreCase)
                    || sev.Equals("fehler", StringComparison.OrdinalIgnoreCase) ? "error" : "warning";
        }
        return severity;
    }
}
