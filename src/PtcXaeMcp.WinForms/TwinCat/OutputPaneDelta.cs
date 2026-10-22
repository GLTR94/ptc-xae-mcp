namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// What an Output-window pane gained during one build (issue #152 C-10/C-21). The old trim cut
/// "the first N characters" where N was the pane length before the build — but Visual Studio
/// CLEARS the build pane when a build starts, so the new build's own output was cut away and only
/// its last line survived (live, TC 4026.22: the compiler summary line was never seen). A pane
/// is now trimmed only when its current text still starts with, and still contains at the same
/// place, the text it had before; otherwise it was cleared and all of it belongs to this build.
/// </summary>
internal static class OutputPaneDelta
{
    private const int MarkChars = 256;

    /// <summary>Snapshot of a pane before the build: its length plus its first and last characters.</summary>
    internal sealed record PaneMark(int Length, string Head, string Tail);

    public static PaneMark Mark(string text) => new(
        text.Length,
        text[..Math.Min(MarkChars, text.Length)],
        text[Math.Max(0, text.Length - MarkChars)..]);

    /// <summary>The part of <paramref name="now"/> written after <paramref name="before"/> was taken.</summary>
    public static string Appended(PaneMark? before, string now)
    {
        if (before is null || before.Length == 0) return now;
        bool continued = now.Length >= before.Length &&
                         now.StartsWith(before.Head, StringComparison.Ordinal) &&
                         string.CompareOrdinal(now, before.Length - before.Tail.Length, before.Tail, 0, before.Tail.Length) == 0;
        if (!continued) return now; // cleared (or rewritten) since the snapshot: all of it is new

        string appended = now[before.Length..];
        // A cut in the middle of a line would leave a truncated first line; drop it.
        if (before.Length > 0 && now[before.Length - 1] != '\n')
        {
            int firstBreak = appended.IndexOf('\n', StringComparison.Ordinal);
            appended = firstBreak >= 0 ? appended[(firstBreak + 1)..] : string.Empty;
        }
        return appended;
    }
}
