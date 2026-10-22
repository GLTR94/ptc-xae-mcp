namespace PtcXaeMcp.WinForms.TwinCat;

// Resolves "inline text OR a file on disk" for the PLC write tools.
//
// Why this exists: a generated block table (M_Init on a real machine is ~19 000
// characters) could previously only reach the project as an inline string, i.e.
// by being retyped or re-serialised through the caller. A single wrong character
// in an entry like FbTypes[17] is SYNTACTICALLY VALID, so neither the compiler
// nor st_check flags it — the error is silent and ends up in safety-adjacent
// code. Letting the tool read the generator's output file directly removes that
// whole class of transcription error and separates "the generator produces the
// code" from "the generator can also deploy it".
internal static class SourceTextInput
{
    internal readonly record struct Resolved(string? Text, string? Error, string? Note);

    /// <summary>
    /// Returns the section's text from either <paramref name="inline"/> or
    /// <paramref name="filePath"/>. Supplying both is an error — silently
    /// preferring one would make it impossible to tell which content landed.
    /// </summary>
    public static Resolved Resolve(string sectionName, string? inline, string? filePath)
    {
        bool hasInline = inline is not null;
        bool hasFile = !string.IsNullOrWhiteSpace(filePath);

        if (hasInline && hasFile)
            return new(null,
                $"[Error] Supply either {sectionName} or {sectionName}File, not both — " +
                "otherwise it is ambiguous which content was written.", null);

        if (!hasFile) return new(inline, null, null);

        string path = filePath!;
        if (!File.Exists(path))
            return new(null, $"[Error] {sectionName}File not found: {path}", null);

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex)
        {
            return new(null, $"[Error] Could not read {sectionName}File '{path}': {ex.Message}", null);
        }

        // Strict decoding (issue #148, B-9): the old reader replaced invalid UTF-8 (e.g. an
        // ANSI/Windows-1252 umlaut) with U+FFFD silently — exactly the silent corruption this
        // path exists to prevent. Invalid input is now refused with the byte offset.
        var (decoded, decodeError) = DecodeStrict(bytes);
        if (decodeError is not null)
            return new(null, $"[Error] {sectionName}File '{path}' is not valid UTF-8: {decodeError} " +
                             "Save the file as UTF-8 (with or without BOM) and retry.", null);
        string text = decoded!;

        // Report what was actually read, so a truncated or empty generator output
        // is visible in the result instead of quietly wiping the section.
        int lines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;
        return new(text, null,
            $"{sectionName} from {Path.GetFileName(path)} ({text.Length} chars, {lines} lines)");
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> as UTF-8 (a UTF-8 BOM is stripped; a UTF-16 BOM selects
    /// strict UTF-16). Returns an error naming the first invalid byte offset instead of ever
    /// substituting U+FFFD.
    /// </summary>
    internal static (string? Text, string? Error) DecodeStrict(byte[] bytes)
    {
        int start = 0;
        System.Text.Encoding enc = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            start = 3;
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { start = 2; enc = new System.Text.UnicodeEncoding(false, false, throwOnInvalidBytes: true); }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { start = 2; enc = new System.Text.UnicodeEncoding(true, false, throwOnInvalidBytes: true); }

        try { return (enc.GetString(bytes, start, bytes.Length - start), null); }
        catch (System.Text.DecoderFallbackException ex)
        {
            // ex.Index is relative to the decoded range (after the BOM); report the file offset.
            int offset = ex.Index >= 0 ? start + ex.Index : FirstInvalidUtf8Offset(bytes, start);
            string hex = ex.BytesUnknown is { Length: > 0 } b
                ? string.Join(" ", b.Select(x => $"0x{x:X2}"))
                : (offset >= 0 && offset < bytes.Length ? $"0x{bytes[offset]:X2}" : "?");
            return (null, $"invalid byte sequence {hex} at byte offset {offset}.");
        }
    }

    // Fallback when the exception carries no index: decode prefixes until one fails.
    private static int FirstInvalidUtf8Offset(byte[] bytes, int start)
    {
        var strict = new System.Text.UTF8Encoding(false, true);
        for (int len = 1; start + len <= bytes.Length; len++)
        {
            try { strict.GetCharCount(bytes, start, len); }
            catch (System.Text.DecoderFallbackException) { return start + len - 1; }
        }
        return -1;
    }

    /// <summary>
    /// The success-side "what was read" notes (issue #148, B-9: the Note used to be computed
    /// and never shown). Empty when every section came inline.
    /// </summary>
    public static string FormatNotes(params Resolved[] inputs)
    {
        var notes = inputs.Where(r => !string.IsNullOrEmpty(r.Note)).Select(r => r.Note!).ToList();
        return notes.Count == 0 ? string.Empty : $" Read {string.Join("; ", notes)}.";
    }
}
