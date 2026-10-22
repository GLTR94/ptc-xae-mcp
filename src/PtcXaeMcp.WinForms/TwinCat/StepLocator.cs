using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Outcome of <see cref="StepLocator.Locate"/>. When <see cref="Found"/> is false,
/// <see cref="Reason"/> says why and <see cref="LabelsSeen"/> lists every label the
/// target CASE carries (numeric or not) so the calling agent can pick a line itself.
/// </summary>
internal sealed record StepLocateResult(
    bool Found,
    int Line,
    string Label,
    int EndLine,
    string Snippet,
    IReadOnlyList<string> LabelsSeen,
    string? Reason);

/// <summary>
/// Pure, headless CASE-branch finder for FB_Base-style step chains
/// (<c>CASE Step OF … 1010: … END_CASE</c>). Works on the cleartext
/// ImplementationText; line numbers are 1-based implementation lines (LF split).
///
/// Scope: only NUMERIC LITERAL
/// labels are matched — a single literal (<c>1010:</c>) or a comma list of literals
/// (<c>100, 1110:</c>). Ranges (<c>10..20:</c>), constants and enum members are
/// reported in <see cref="StepLocateResult.LabelsSeen"/> but never matched.
///
/// Robustness: line comments (<c>// 10:</c> — the PTC template has such a commented-out
/// step), block comments <c>(* … *)</c> and string literals are blanked out before
/// matching, and only labels at the target CASE's own nesting depth count — a
/// nested <c>CASE x OF</c> inside a branch is skipped.
/// </summary>
internal static class StepLocator
{
    private static readonly Regex CaseOpen = new(@"^\s*CASE\s+(?<sel>[A-Za-z_][\w.\[\]]*)\s+OF\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CaseClose = new(@"^\s*END_CASE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CaseElse = new(@"^\s*ELSE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // A branch label: one or more label atoms separated by commas, then ':' that is
    // NOT the start of ':=' (assignments) and not '::'.
    private static readonly Regex Label = new(
        @"^\s*(?<lbl>[\w.\-+]+(?:\s*\.\.\s*[\w.\-+]+)?(?:\s*,\s*[\w.\-+]+(?:\s*\.\.\s*[\w.\-+]+)?)*)\s*:(?![=:])",
        RegexOptions.Compiled);

    public static StepLocateResult Locate(string implementation, int step, string stepVariable)
    {
        if (string.IsNullOrWhiteSpace(stepVariable))
            throw new ArgumentException("stepVariable is required.", nameof(stepVariable));

        string[] raw = (implementation ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        string[] code = StripCommentsAndStrings(raw);

        // Pass 1: find the top-level CASE whose selector matches stepVariable.
        int depth = 0, caseLine = -1, otherTopLevelCase = -1; string? otherSelector = null;
        for (int i = 0; i < code.Length; i++)
        {
            var open = CaseOpen.Match(code[i]);
            if (open.Success)
            {
                if (depth == 0)
                {
                    string sel = open.Groups["sel"].Value;
                    if (string.Equals(sel, stepVariable, StringComparison.OrdinalIgnoreCase)) { caseLine = i; break; }
                    if (otherTopLevelCase < 0) { otherTopLevelCase = i; otherSelector = sel; }
                }
                depth++;
                continue;
            }
            if (CaseClose.IsMatch(code[i])) depth = Math.Max(0, depth - 1);
        }

        if (caseLine < 0)
        {
            string reason = otherTopLevelCase >= 0
                ? $"no top-level 'CASE {stepVariable} OF' found (the first top-level CASE at line {otherTopLevelCase + 1} selects '{otherSelector}')"
                : $"no top-level 'CASE {stepVariable} OF' found";
            return new StepLocateResult(false, 0, string.Empty, 0, string.Empty, Array.Empty<string>(), reason);
        }

        // Pass 2: walk the target CASE body at its own depth; collect labels, find the hit.
        var labelsSeen = new List<string>();
        var labelLines = new List<(int Line, string Label)>();
        int endCaseLine = code.Length; // exclusive fallback if END_CASE is missing
        int elseLine = -1;
        depth = 1;
        for (int i = caseLine + 1; i < code.Length; i++)
        {
            if (CaseOpen.IsMatch(code[i])) { depth++; continue; }
            if (CaseClose.IsMatch(code[i]))
            {
                depth--;
                if (depth == 0) { endCaseLine = i; break; }
                continue;
            }
            if (depth != 1) continue;
            if (CaseElse.IsMatch(code[i])) { if (elseLine < 0) elseLine = i; continue; }
            var m = Label.Match(code[i]);
            if (!m.Success) continue;
            string lbl = NormalizeLabel(m.Groups["lbl"].Value);
            labelLines.Add((i, lbl));
            if (!labelsSeen.Contains(lbl)) labelsSeen.Add(lbl);
        }

        for (int k = 0; k < labelLines.Count; k++)
        {
            var (line, lbl) = labelLines[k];
            if (!LabelMatchesStep(lbl, step)) continue;

            int nextBoundary = k + 1 < labelLines.Count ? labelLines[k + 1].Line : endCaseLine;
            if (elseLine > line && elseLine < nextBoundary) nextBoundary = elseLine;
            // Trim trailing blank and comment-only lines (judged on the code view) so
            // the branch does not swallow the next step's leading comment header.
            int end = nextBoundary - 1;
            while (end > line && string.IsNullOrWhiteSpace(code[end])) end--;
            string snippet = string.Join("\n", raw.Skip(line).Take(end - line + 1));
            return new StepLocateResult(true, line + 1, lbl, end + 1, snippet, labelsSeen, null);
        }

        string seen = labelsSeen.Count == 0 ? "none" : string.Join(", ", labelsSeen);
        return new StepLocateResult(false, 0, string.Empty, 0, string.Empty, labelsSeen,
            $"step {step} has no numeric-literal label in 'CASE {stepVariable} OF' (line {caseLine + 1}); labels seen: {seen}");
    }

    /// <summary>True when the label is a numeric literal or a comma list of literals containing <paramref name="step"/>.</summary>
    internal static bool LabelMatchesStep(string label, int step)
    {
        foreach (string part in label.Split(','))
        {
            if (int.TryParse(part.Trim(), out int v) && v == step) return true;
        }
        return false;
    }

    private static string NormalizeLabel(string label) =>
        Regex.Replace(label.Trim(), @"\s*,\s*", ", ");

    /// <summary>
    /// Returns a line-aligned copy with string literals, <c>//</c> comments and
    /// <c>(* … *)</c> block comments replaced by spaces, so label/CASE matching never
    /// trips over commented-out steps or colons inside strings.
    /// </summary>
    internal static string[] StripCommentsAndStrings(string[] lines)
    {
        var result = new string[lines.Length];
        bool inBlock = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string s = lines[i];
            var sb = new System.Text.StringBuilder(s.Length);
            bool inString = false;
            for (int j = 0; j < s.Length; j++)
            {
                char c = s[j];
                if (inBlock)
                {
                    if (c == '*' && j + 1 < s.Length && s[j + 1] == ')') { inBlock = false; sb.Append("  "); j++; }
                    else sb.Append(' ');
                    continue;
                }
                if (inString)
                {
                    if (c == '$' && j + 1 < s.Length) { sb.Append("  "); j++; continue; } // $' escape
                    if (c == '\'') inString = false;
                    sb.Append(' ');
                    continue;
                }
                if (c == '\'') { inString = true; sb.Append(' '); continue; }
                if (c == '(' && j + 1 < s.Length && s[j + 1] == '*') { inBlock = true; sb.Append("  "); j++; continue; }
                if (c == '/' && j + 1 < s.Length && s[j + 1] == '/') { sb.Append(' ', s.Length - j); break; }
                sb.Append(c);
            }
            result[i] = sb.ToString();
        }
        return result;
    }
}
