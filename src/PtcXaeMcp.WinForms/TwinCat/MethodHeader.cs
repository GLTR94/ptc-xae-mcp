namespace PtcXaeMcp.WinForms.TwinCat;

// Pure helpers for the METHOD header fix-up after CreateChild (issue #148, B-7).
// A scalar-language-vInfo METHOD comes back declared `METHOD <name> : <langCode>`
// (e.g. "... : 1"); the session rewrites the header line to the requested return type
// and must then PROVE the rewrite landed (the old code swallowed a failed setter and
// reported success with `: 1` as the return type).
internal static class MethodHeader
{
    public static string Want(string name, string returnType) => $"METHOD {name} : {returnType}";

    /// <summary>Declaration with ONLY the METHOD header line replaced (or prepended when absent).</summary>
    public static string Rewrite(string declaration, string name, string returnType)
    {
        string decl = declaration ?? string.Empty;
        string[] lines = decl.Replace("\r\n", "\n").Split('\n');
        int hdr = HeaderIndex(lines);
        string want = Want(name, returnType);
        if (hdr >= 0)
        {
            lines[hdr] = want;
            return string.Join("\n", lines);
        }
        return want + (decl.Length > 0 ? "\n" + decl : string.Empty);
    }

    /// <summary>
    /// True when the declaration's METHOD header names <paramref name="name"/> and returns
    /// <paramref name="returnType"/> (whitespace- and case-insensitive; access modifiers allowed).
    /// </summary>
    public static bool Matches(string? declaration, string name, string returnType)
    {
        if (string.IsNullOrEmpty(declaration)) return false;
        string[] lines = declaration.Replace("\r\n", "\n").Split('\n');
        int hdr = HeaderIndex(lines);
        if (hdr < 0) return false;
        string line = StripComment(lines[hdr]);
        int colon = line.IndexOf(':');
        if (colon < 0) return false;
        string[] head = line[..colon].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string ret = Collapse(line[(colon + 1)..]).TrimEnd(';').Trim();
        return head.Length >= 2
            && string.Equals(head[^1], name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ret, Collapse(returnType), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The header line as found, for error messages; null when there is none.</summary>
    public static string? HeaderLine(string? declaration)
    {
        if (string.IsNullOrEmpty(declaration)) return null;
        string[] lines = declaration.Replace("\r\n", "\n").Split('\n');
        int hdr = HeaderIndex(lines);
        return hdr < 0 ? null : lines[hdr].Trim();
    }

    private static int HeaderIndex(string[] lines) => Array.FindIndex(lines, l =>
        l.TrimStart().StartsWith("METHOD ", StringComparison.OrdinalIgnoreCase));

    private static string StripComment(string line)
    {
        int c = line.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? line[..c] : line;
    }

    private static string Collapse(string s) =>
        string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
