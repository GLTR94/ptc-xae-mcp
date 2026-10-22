using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

// Which entries a declaration text holds, and how to find them.
internal enum DeclarationShape
{
    /// <summary>VAR … END_VAR sections: POU, METHOD, PROPERTY Get/Set, GVL.</summary>
    VarSections,
    /// <summary>The fields between STRUCT/UNION and END_STRUCT/END_UNION of a DUT.</summary>
    StructBody,
    /// <summary>The values between the parentheses of an ENUM DUT.</summary>
    EnumBody,
}

internal enum DeclTokenKind { Whitespace, Newline, LineComment, BlockComment, Pragma, Quoted, Word, Symbol }

internal readonly record struct DeclToken(DeclTokenKind Kind, int Start, int End)
{
    public bool IsTrivia => Kind is DeclTokenKind.Whitespace or DeclTokenKind.Newline
        or DeclTokenKind.LineComment or DeclTokenKind.BlockComment or DeclTokenKind.Pragma;
}

/// <summary>One entry (a field, an enum value or a variable) with the source spans needed to edit it in place.</summary>
internal sealed class DeclEntry
{
    public string Section { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Type { get; init; }
    public string? Initial { get; init; }
    public string? Address { get; init; }
    public string? Comment { get; init; }
    public IReadOnlyList<string> Attributes { get; init; } = [];
    public int Line { get; init; }

    /// <summary>Non-null when the entry cannot be edited safely (several names, several entries on a line, …).</summary>
    public string? Problem { get; init; }

    internal int NameStart { get; init; }
    internal int NameEnd { get; init; }
    internal (int Start, int End)? TypeSpan { get; init; }
    internal (int Start, int End)? InitSpan { get; init; }
    internal (int Start, int End)? AddressSpan { get; init; }
    internal (int Start, int End)? CommentSpan { get; init; }
    /// <summary>Position of the terminating ';' (variables, fields) or ',' (enum values); -1 when there is none.</summary>
    internal int TerminatorPos { get; init; } = -1;
    /// <summary>End of the last non-trivia token of the entry.</summary>
    internal int ContentEnd { get; init; }
    internal int LineStart { get; init; }
    internal int LineEnd { get; init; }
    internal IReadOnlyList<(int Start, int End)> AttributeRemovals { get; init; } = [];
    internal string Indent { get; init; } = "";
}

internal sealed class DeclSection
{
    public string Header { get; init; } = "";
    public IReadOnlyList<DeclEntry> Entries { get; init; } = [];
    /// <summary>Start of the line that closes the section (END_VAR / END_STRUCT / ')'); insert new entries before it.</summary>
    internal int CloseLineStart { get; init; }
    /// <summary>True when the closing token shares its line with other text (so a new line must be opened first).</summary>
    internal bool CloseSharesLine { get; init; }
    internal int CloseTokenStart { get; init; }
    /// <summary>End of the line holding the closing token (where a section appended after this one goes).</summary>
    internal int CloseLineEnd { get; init; }
}

internal sealed class ParsedDeclaration
{
    public string Text { get; init; } = "";
    public string Newline { get; init; } = "\r\n";
    public IReadOnlyList<DeclSection> Sections { get; init; } = [];
    public string? Error { get; init; }
    public DeclarationShape Shape { get; init; }
}

// Pure, COM-free parser of declaration text. It is deliberately not a full ST parser: it finds the sections /
// body, splits them into entries at the top-level ';' (',' for enum values), and records the exact source span of
// every part so that an edit changes only that span and leaves the rest of the text byte-identical.
// Ambiguous or unusual input (several names in one entry, several entries on one line) is flagged, never guessed.
internal static class DeclarationParser
{
    private static readonly HashSet<string> SectionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_GLOBAL", "VAR_TEMP", "VAR_STAT", "VAR_INST",
        "VAR_EXTERNAL", "VAR_CONFIG", "VAR_ACCESS",
    };

    private static readonly HashSet<string> SectionModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "CONSTANT", "RETAIN", "PERSISTENT", "NON_RETAIN",
    };

    /// <summary>True for "VAR_INPUT", "VAR_GLOBAL PERSISTENT", … : a section keyword followed only by known modifiers.</summary>
    public static bool IsValidSectionHeader(string normalizedHeader)
    {
        var words = normalizedHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && SectionKeywords.Contains(words[0]) && words.Skip(1).All(SectionModifiers.Contains);
    }

    // ---- tokenizer -----------------------------------------------------------

    public static (List<DeclToken>? Tokens, string? Error) Tokenize(string text)
    {
        var tokens = new List<DeclToken>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int start = i;
            if (c == '\r' || c == '\n')
            {
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                tokens.Add(new(DeclTokenKind.Newline, start, i));
            }
            else if (char.IsWhiteSpace(c))
            {
                while (i < text.Length && text[i] != '\r' && text[i] != '\n' && char.IsWhiteSpace(text[i])) i++;
                tokens.Add(new(DeclTokenKind.Whitespace, start, i));
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\r' && text[i] != '\n') i++;
                tokens.Add(new(DeclTokenKind.LineComment, start, i));
            }
            else if ((c == '(' && i + 1 < text.Length && text[i + 1] == '*') || (c == '/' && i + 1 < text.Length && text[i + 1] == '*'))
            {
                char open = c;
                char close = c == '(' ? ')' : '/';
                int depth = 0;
                bool done = false;
                while (i < text.Length)
                {
                    if (text[i] == open && i + 1 < text.Length && text[i + 1] == '*') { depth++; i += 2; }
                    else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == close) { depth--; i += 2; if (depth == 0) { done = true; break; } }
                    else i++;
                }
                if (!done) return (null, $"[Error] Unterminated block comment starting at line {LineOf(text, start)}.");
                tokens.Add(new(DeclTokenKind.BlockComment, start, i));
            }
            else if (c == '{')
            {
                int depth = 0;
                bool done = false;
                while (i < text.Length)
                {
                    if (text[i] == '\'' || text[i] == '"')
                    {
                        int q = SkipQuoted(text, i);
                        if (q < 0) return (null, $"[Error] Unterminated string inside a pragma starting at line {LineOf(text, start)}.");
                        i = q;
                        continue;
                    }
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') { depth--; if (depth == 0) { i++; done = true; break; } }
                    i++;
                }
                if (!done) return (null, $"[Error] Unterminated pragma starting at line {LineOf(text, start)}.");
                tokens.Add(new(DeclTokenKind.Pragma, start, i));
            }
            else if (c == '\'' || c == '"')
            {
                int q = SkipQuoted(text, i);
                if (q < 0) return (null, $"[Error] Unterminated string starting at line {LineOf(text, start)}.");
                i = q;
                tokens.Add(new(DeclTokenKind.Quoted, start, i));
            }
            else if (char.IsLetterOrDigit(c) || c == '_' || c == '#')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '#')) i++;
                tokens.Add(new(DeclTokenKind.Word, start, i));
            }
            else if (c == ':' && i + 1 < text.Length && text[i + 1] == '=')
            {
                i += 2;
                tokens.Add(new(DeclTokenKind.Symbol, start, i));
            }
            else
            {
                i++;
                tokens.Add(new(DeclTokenKind.Symbol, start, i));
            }
        }
        return (tokens, null);
    }

    // Index just past the closing quote, or -1 when the string is not closed on its line. '$' escapes the next char.
    private static int SkipQuoted(string text, int start)
    {
        char quote = text[start];
        int i = start + 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '$' && i + 1 < text.Length) { i += 2; continue; }
            if (c == '\r' || c == '\n') return -1;
            if (c == quote) return i + 1;
            i++;
        }
        return -1;
    }

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') starts.Add(i + 1);
            else if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) starts.Add(i + 1);
        }
        return starts;
    }

    internal static int LineOf(string text, int pos)
    {
        int line = 1;
        for (int i = 0; i < pos && i < text.Length; i++)
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))) line++;
        return line;
    }

    // ---- structure -----------------------------------------------------------

    public static ParsedDeclaration Parse(string text, DeclarationShape shape)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
            : text.Contains('\n', StringComparison.Ordinal) ? "\n"
            : text.Contains('\r', StringComparison.Ordinal) ? "\r" : "\r\n";

        var (tokens, tokError) = Tokenize(text);
        if (tokens is null) return new ParsedDeclaration { Text = text, Newline = newline, Error = tokError, Shape = shape };

        var ctx = new Ctx(text, tokens);
        List<DeclSection> sections;
        string? error;
        switch (shape)
        {
            case DeclarationShape.VarSections: (sections, error) = ParseVarSections(ctx); break;
            case DeclarationShape.StructBody: (sections, error) = ParseStruct(ctx); break;
            default: (sections, error) = ParseEnum(ctx); break;
        }
        return new ParsedDeclaration { Text = text, Newline = newline, Sections = sections, Error = error, Shape = shape };
    }

    private sealed class Ctx(string text, List<DeclToken> tokens)
    {
        public string Text { get; } = text;
        public List<DeclToken> Tokens { get; } = tokens;
        // Start offset of every line, so a line number is a binary search instead of a scan.
        private readonly List<int> _lineStarts = LineStarts(text);
        public int LineNumber(int pos)
        {
            int idx = _lineStarts.BinarySearch(pos);
            return (idx >= 0 ? idx : ~idx - 1) + 1;
        }
        public string Slice(DeclToken t) => Text[t.Start..t.End];
        public string Slice(int start, int end) => Text[start..end];
        public bool IsWord(int index, string word) =>
            Tokens[index].Kind == DeclTokenKind.Word && string.Equals(Slice(Tokens[index]), word, StringComparison.OrdinalIgnoreCase);
        public bool IsSymbol(int index, char ch) =>
            Tokens[index].Kind == DeclTokenKind.Symbol && Tokens[index].End - Tokens[index].Start == 1 && Text[Tokens[index].Start] == ch;
        public bool IsAssign(int index) =>
            Tokens[index].Kind == DeclTokenKind.Symbol && Tokens[index].End - Tokens[index].Start == 2 && Slice(Tokens[index]) == ":=";
        public int LineStartOf(int pos)
        {
            int p = Math.Min(pos, Text.Length);
            while (p > 0 && Text[p - 1] != '\n' && Text[p - 1] != '\r') p--;
            return p;
        }
        // End of the line containing pos, including its line break.
        public int LineEndOf(int pos)
        {
            int p = pos;
            while (p < Text.Length && Text[p] != '\n' && Text[p] != '\r') p++;
            if (p < Text.Length) p += Text[p] == '\r' && p + 1 < Text.Length && Text[p + 1] == '\n' ? 2 : 1;
            return p;
        }
        public string IndentOf(int lineStart)
        {
            int p = lineStart;
            while (p < Text.Length && (Text[p] == ' ' || Text[p] == '\t')) p++;
            return Text[lineStart..p];
        }
        public bool IsBlank(int start, int end)
        {
            for (int p = start; p < end; p++)
                if (!char.IsWhiteSpace(Text[p])) return false;
            return true;
        }
    }

    private static (List<DeclSection>, string?) ParseVarSections(Ctx c)
    {
        var sections = new List<DeclSection>();
        int i = 0;
        while (i < c.Tokens.Count)
        {
            var t = c.Tokens[i];
            if (t.Kind != DeclTokenKind.Word || !SectionKeywords.Contains(c.Slice(t))) { i++; continue; }

            var header = new List<string> { c.Slice(t).ToUpperInvariant() };
            int bodyStart = i + 1;
            int j = i + 1;
            while (j < c.Tokens.Count && c.Tokens[j].Kind is DeclTokenKind.Whitespace or DeclTokenKind.Word or DeclTokenKind.Pragma)
            {
                if (c.Tokens[j].Kind == DeclTokenKind.Word)
                {
                    if (!SectionModifiers.Contains(c.Slice(c.Tokens[j]))) break;
                    header.Add(c.Slice(c.Tokens[j]).ToUpperInvariant());
                    bodyStart = j + 1;
                }
                j++;
            }

            int end = -1;
            for (int k = bodyStart; k < c.Tokens.Count; k++)
                if (c.IsWord(k, "END_VAR")) { end = k; break; }
            string headerText = string.Join(' ', header);
            if (end < 0) return (sections, $"[Error] Section '{headerText}' at line {LineOf(c.Text, t.Start)} has no END_VAR.");

            sections.Add(BuildSection(c, headerText, bodyStart, end, enumMode: false));
            i = end + 1;
        }
        return (sections, null);
    }

    private static (List<DeclSection>, string?) ParseStruct(Ctx c)
    {
        for (int i = 0; i < c.Tokens.Count; i++)
        {
            bool isStruct = c.IsWord(i, "STRUCT");
            if (!isStruct && !c.IsWord(i, "UNION")) continue;
            string close = isStruct ? "END_STRUCT" : "END_UNION";
            int end = -1;
            for (int k = i + 1; k < c.Tokens.Count; k++)
                if (c.IsWord(k, close)) { end = k; break; }
            if (end < 0) return ([], $"[Error] {(isStruct ? "STRUCT" : "UNION")} at line {LineOf(c.Text, c.Tokens[i].Start)} has no {close}.");
            return ([BuildSection(c, isStruct ? "STRUCT" : "UNION", i + 1, end, enumMode: false)], null);
        }
        return ([], "[Error] No STRUCT or UNION body found in the declaration.");
    }

    private static (List<DeclSection>, string?) ParseEnum(Ctx c)
    {
        int open = -1;
        for (int i = 0; i < c.Tokens.Count; i++)
            if (c.IsSymbol(i, '(')) { open = i; break; }
        if (open < 0) return ([], "[Error] No enumeration body '( … )' found in the declaration.");

        int depth = 0, close = -1;
        for (int i = open; i < c.Tokens.Count; i++)
        {
            if (c.IsSymbol(i, '(')) depth++;
            else if (c.IsSymbol(i, ')')) { depth--; if (depth == 0) { close = i; break; } }
        }
        if (close < 0) return ([], $"[Error] The enumeration body at line {LineOf(c.Text, c.Tokens[open].Start)} is not closed.");
        return ([BuildSection(c, "ENUM", open + 1, close, enumMode: true)], null);
    }

    private static DeclSection BuildSection(Ctx c, string header, int bodyStart, int closeToken, bool enumMode)
    {
        var entries = ParseEntries(c, header, bodyStart, closeToken, enumMode);
        int closeStart = c.Tokens[closeToken].Start;
        int lineStart = c.LineStartOf(closeStart);
        bool shares = !c.IsBlank(lineStart, closeStart);
        return new DeclSection
        {
            Header = header,
            Entries = entries,
            CloseLineStart = lineStart,
            CloseSharesLine = shares,
            CloseTokenStart = closeStart,
            CloseLineEnd = c.LineEndOf(closeStart),
        };
    }

    private static bool IsAttributePragma(Ctx c, DeclToken t) =>
        t.Kind == DeclTokenKind.Pragma
        && c.Slice(t).AsSpan(1).TrimStart().StartsWith("attribute", StringComparison.OrdinalIgnoreCase);

    private static List<DeclEntry> ParseEntries(Ctx c, string section, int from, int to, bool enumMode)
    {
        var entries = new List<DeclEntry>();
        var pending = new List<int>(); // attribute pragmas directly above the next statement
        int i = from;
        while (i < to)
        {
            var t = c.Tokens[i];
            if (t.Kind is DeclTokenKind.Whitespace or DeclTokenKind.Newline or DeclTokenKind.LineComment or DeclTokenKind.BlockComment)
            { i++; continue; }
            if (t.Kind == DeclTokenKind.Pragma)
            {
                if (IsAttributePragma(c, t)) pending.Add(i); else pending.Clear();
                i++;
                continue;
            }

            // A statement: runs to the top-level ';' (',' for enum values), or to the end of the body (enum, last value).
            int depth = 0, term = -1, j = i;
            var content = new List<int>();
            var inlineAttrs = new List<int>();
            for (; j < to; j++)
            {
                var u = c.Tokens[j];
                if (u.Kind == DeclTokenKind.Symbol && u.End - u.Start == 1)
                {
                    char ch = c.Text[u.Start];
                    if (ch is '(' or '[') depth++;
                    else if (ch is ')' or ']') depth = Math.Max(0, depth - 1);
                    else if (depth == 0 && ch == (enumMode ? ',' : ';')) { term = j; break; }
                }
                if (u.Kind == DeclTokenKind.Pragma) { if (IsAttributePragma(c, u)) inlineAttrs.Add(j); continue; }
                if (!u.IsTrivia) content.Add(j);
            }

            var entry = ParseStatement(c, section, enumMode, pending, inlineAttrs, content, term);
            entries.Add(entry);
            pending = [];

            // Resume after the terminator and after the trailing comment, which belongs to the entry.
            i = term >= 0 ? term + 1 : to;
            if (entry.CommentSpan is { } cs)
                while (i < to && c.Tokens[i].Start < cs.End) i++;
        }

        // Two entries on one line cannot be edited line-wise.
        var flagged = new List<DeclEntry>(entries.Count);
        for (int k = 0; k < entries.Count; k++)
        {
            var e = entries[k];
            bool shares = !c.IsBlank(e.LineStart, FirstTokenStart(e)) || !c.IsBlank(TrailEnd(e), LineBreakStart(c, e.LineEnd));
            flagged.Add(shares && e.Problem is null ? CopyWithProblem(e, "shares its line with other entries or code") : e);
        }
        return flagged;
    }

    private static int FirstTokenStart(DeclEntry e) =>
        e.AttributeRemovals.Count > 0 ? Math.Min(e.NameStart, e.AttributeRemovals.Min(r => r.Start)) : e.NameStart;

    private static int TrailEnd(DeclEntry e) =>
        Math.Max(e.TerminatorPos >= 0 ? e.TerminatorPos + 1 : e.ContentEnd, e.CommentSpan?.End ?? 0);

    private static int LineBreakStart(Ctx c, int lineEnd)
    {
        int p = lineEnd;
        while (p > 0 && (c.Text[p - 1] == '\n' || c.Text[p - 1] == '\r')) p--;
        return p;
    }

    private static DeclEntry CopyWithProblem(DeclEntry e, string problem) => new()
    {
        Section = e.Section, Name = e.Name, Type = e.Type, Initial = e.Initial, Address = e.Address, Comment = e.Comment,
        Attributes = e.Attributes, Line = e.Line, Problem = problem,
        NameStart = e.NameStart, NameEnd = e.NameEnd, TypeSpan = e.TypeSpan, InitSpan = e.InitSpan,
        AddressSpan = e.AddressSpan, CommentSpan = e.CommentSpan, TerminatorPos = e.TerminatorPos, ContentEnd = e.ContentEnd,
        LineStart = e.LineStart, LineEnd = e.LineEnd, AttributeRemovals = e.AttributeRemovals, Indent = e.Indent,
    };

    private static DeclEntry ParseStatement(Ctx c, string section, bool enumMode, List<int> pending,
        List<int> inlineAttrs, List<int> content, int term)
    {
        var first = c.Tokens[content[0]];
        int termPos = term >= 0 ? c.Tokens[term].Start : -1;
        int contentEnd = c.Tokens[content[^1]].End;
        string? problem = null;

        // Attributes: the pragma lines directly above (no blank line between), plus pragmas inside the statement.
        var attrs = new List<string>();
        var removals = new List<(int, int)>();
        var lead = new List<int>();
        int boundary = first.Start;
        for (int k = pending.Count - 1; k >= 0; k--)
        {
            var p = c.Tokens[pending[k]];
            if (CountLineBreaks(c.Text, p.End, boundary) > 1) break; // a blank line separates it from the entry
            lead.Insert(0, pending[k]);
            boundary = p.Start;
        }
        foreach (int idx in lead.Concat(inlineAttrs))
            attrs.Add(c.Slice(c.Tokens[idx]));

        // Whole lines made only of attribute pragmas are removed as lines; a pragma sharing its line is removed alone.
        var seenLines = new HashSet<int>();
        foreach (int idx in lead)
        {
            var p = c.Tokens[idx];
            int ls = c.LineStartOf(p.Start);
            if (!seenLines.Add(ls)) continue;
            int le = c.LineEndOf(p.Start);

            int l = idx, r = idx;
            while (l > 0 && c.Tokens[l - 1].Kind != DeclTokenKind.Newline) l--;
            while (r + 1 < c.Tokens.Count && c.Tokens[r].Kind != DeclTokenKind.Newline) r++;
            bool pure = true;
            for (int k = l; k <= r && pure; k++)
                pure = c.Tokens[k].Kind is DeclTokenKind.Whitespace or DeclTokenKind.Newline || lead.Contains(k);
            if (pure && le <= first.Start) removals.Add((ls, le));
            else foreach (int li in lead.Where(x => c.LineStartOf(c.Tokens[x].Start) == ls)) removals.Add(TokenRemoval(c, c.Tokens[li]));
        }
        foreach (int idx in inlineAttrs) removals.Add(TokenRemoval(c, c.Tokens[idx]));

        // name, optional AT address, ':' type, ':=' initial value
        (int, int)? addr = null, type = null, init = null;
        string name = c.Slice(first);
        if (first.Kind != DeclTokenKind.Word) problem = $"does not start with a name ('{name}')";

        if (enumMode)
        {
            int assign = content.FindIndex(1, x => c.IsAssign(x));
            if (assign >= 0 && assign + 1 < content.Count)
                init = (c.Tokens[content[assign + 1]].Start, contentEnd);
            else if (assign < 0 && content.Count > 1)
                problem ??= "has unexpected text after its name";
        }
        else
        {
            int depth = 0, colon = -1, assign = -1;
            for (int k = 1; k < content.Count; k++)
            {
                var u = c.Tokens[content[k]];
                if (u.Kind == DeclTokenKind.Symbol && u.End - u.Start == 1)
                {
                    char ch = c.Text[u.Start];
                    if (ch is '(' or '[') depth++;
                    else if (ch is ')' or ']') depth = Math.Max(0, depth - 1);
                    else if (ch == ',' && depth == 0 && colon < 0) problem ??= "declares several names in one entry";
                    else if (ch == ':' && depth == 0 && colon < 0) colon = k;
                }
                else if (c.IsAssign(content[k]) && depth == 0 && colon >= 0 && assign < 0) assign = k;
            }

            if (colon < 0) problem ??= "has no ':' and type";
            else
            {
                int addrFirst = -1;
                for (int k = 1; k < colon; k++)
                    if (c.IsWord(content[k], "AT")) { addrFirst = k; break; }
                if (addrFirst >= 0 && colon - 1 >= addrFirst)
                    addr = (c.Tokens[content[addrFirst]].Start, c.Tokens[content[colon - 1]].End);

                int typeEnd = assign >= 0 ? assign - 1 : content.Count - 1;
                if (colon + 1 <= typeEnd) type = (c.Tokens[content[colon + 1]].Start, c.Tokens[content[typeEnd]].End);
                else problem ??= "has no type";
                if (assign >= 0 && assign + 1 < content.Count)
                    init = (c.Tokens[content[assign + 1]].Start, contentEnd);
            }
            if (term < 0) problem ??= "is not terminated by ';'";
        }

        // Trailing comment on the line of the terminator (or of the last token, for the final enum value).
        (int, int)? comment = null;
        int scan = (term >= 0 ? term : content[^1]) + 1;
        while (scan < c.Tokens.Count && c.Tokens[scan].Kind == DeclTokenKind.Whitespace) scan++;
        if (scan < c.Tokens.Count && c.Tokens[scan].Kind is DeclTokenKind.LineComment or DeclTokenKind.BlockComment)
            comment = (c.Tokens[scan].Start, c.Tokens[scan].End);

        int firstPos = lead.Count > 0 ? Math.Min(first.Start, c.Tokens[lead[0]].Start) : first.Start;
        int lineStart = c.LineStartOf(firstPos);
        int lastPos = Math.Max(termPos >= 0 ? termPos + 1 : contentEnd, comment?.Item2 ?? 0);
        int lineEnd = c.LineEndOf(Math.Max(lastPos - 1, 0));

        string? Text((int Start, int End)? s) => s is { } v ? c.Slice(v.Start, v.End).Trim() : null;
        string? commentText = null;
        if (comment is { } cm)
        {
            string raw = c.Slice(cm.Item1, cm.Item2);
            commentText = raw.StartsWith("//", StringComparison.Ordinal) ? raw[2..].Trim()
                : raw.Length >= 4 ? raw[2..^2].Trim() : raw;
        }

        return new DeclEntry
        {
            Section = section,
            Name = name,
            Type = Text(type),
            Initial = Text(init),
            Address = addr is { } a ? c.Slice(a.Item1, a.Item2) : null,
            Comment = commentText,
            Attributes = attrs,
            Line = c.LineNumber(first.Start),
            Problem = problem,
            NameStart = first.Start,
            NameEnd = first.End,
            TypeSpan = type,
            InitSpan = init,
            AddressSpan = addr,
            CommentSpan = comment,
            TerminatorPos = termPos,
            ContentEnd = contentEnd,
            LineStart = lineStart,
            LineEnd = lineEnd,
            AttributeRemovals = removals,
            Indent = c.IndentOf(c.LineStartOf(first.Start)),
        };
    }

    // A pragma that shares its line is removed together with the whitespace that follows it.
    private static (int, int) TokenRemoval(Ctx c, DeclToken p)
    {
        int end = p.End;
        while (end < c.Text.Length && (c.Text[end] == ' ' || c.Text[end] == '\t')) end++;
        return (p.Start, end);
    }

    private static int CountLineBreaks(string text, int from, int to)
    {
        int n = 0;
        for (int p = from; p < to && p < text.Length; p++)
        {
            if (text[p] == '\n') n++;
            else if (text[p] == '\r' && (p + 1 >= text.Length || text[p + 1] != '\n')) n++;
        }
        return n;
    }

    internal static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
