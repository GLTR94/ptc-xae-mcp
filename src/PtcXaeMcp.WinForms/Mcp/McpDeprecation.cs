namespace PtcXaeMcp.WinForms;

// The 15 PLC-code tools that predate the generic twincat_plc_* verbs (feature/plc-objects) keep working
// unchanged; their result gets one trailing line naming the replacement, so a caller (or a log reader)
// learns the new name from the answer itself. Removing the old tools is a later step.
internal static class McpDeprecation
{
    public static string Append(string result, string replacement) =>
        $"{result}{Environment.NewLine}[Deprecated] use {replacement}.";

    // The tools that return source text (read_pou, read_gvl, read_pou_member) must not end with the notice: a caller
    // that takes everything after "--- Implementation ---" as code (and writes it back) would put the notice into the
    // PLC source. Their notice goes at the end of the first line (the summary) instead.
    public static string AppendToFirstLine(string result, string replacement)
    {
        int newline = result.IndexOfAny(['\r', '\n']);
        string notice = $" [Deprecated] use {replacement}.";
        return newline < 0 ? result + notice : result.Insert(newline, notice);
    }
}
