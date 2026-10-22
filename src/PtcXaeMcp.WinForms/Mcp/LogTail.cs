using System.Globalization;

namespace PtcXaeMcp.WinForms;

/// <summary>
/// Bounds the text returned by <c>twincat_get_log</c> (issue #157 item 1): the UI log grows for the
/// whole host session, and returning all of it can exceed an MCP client's tool-result limit.
/// Keeps the TAIL (the newest lines) behind a one-line note of how much was omitted.
/// </summary>
internal static class LogTail
{
    /// <param name="text">Full log text.</param>
    /// <param name="maxChars">Maximum characters of log kept; 0 or less returns the full text.</param>
    public static string Tail(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxChars <= 0 || text.Length <= maxChars) return text;
        int cut = text.Length - maxChars;
        return OmittedNote(cut, maxChars) + text[cut..];
    }

    /// <summary>The one-line note put in front of a cut log.</summary>
    internal static string OmittedNote(int cut, int maxChars) =>
        string.Create(CultureInfo.InvariantCulture,
            $"[{cut} earlier characters omitted - showing the last {maxChars}; pass maxChars=0 for the full log]\n");
}
