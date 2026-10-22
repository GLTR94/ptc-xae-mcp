using System.Text;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// hmi-ads-tmc: what a .tmc path on disk actually IS, read
/// before it is handed to the HMI server. A wrong-but-existing file is the expensive
/// failure here — the server would happily serve symbol descriptions of another machine —
/// so the file is opened and identified, never just name-checked.
/// Shape verified on the PLC.tmc of a real machine project (2026-09-26):
/// XML, one line, <c>&lt;TcModuleClass … GeneratedBy="TwinCAT XAE Plc"&gt;</c>, 734
/// <c>&lt;Symbol&gt;</c> entries.
/// </summary>
internal static class TmcFile
{
    internal const string Extension = ".tmc";
    internal const string RootElement = "<TcModuleClass";
    internal const string SymbolElement = "<Symbol>";

    /// <summary>Enough for the XML declaration plus the TcModuleClass start tag.</summary>
    internal const int HeaderProbeChars = 8192;

    /// <summary>A real PLC TMC is a few MB; refuse absurd input instead of streaming for minutes.</summary>
    internal const long MaxSizeBytes = 256L * 1024 * 1024;

    /// <summary>Facts, or a ready-to-return reason why this path is not a usable PLC TMC.</summary>
    internal static (TmcFileFacts? Facts, string? Error) Inspect(string? tmcFilePath)
    {
        string path = tmcFilePath?.Trim() ?? "";
        if (path.Length == 0)
            return (null, "tmcFile is required — the absolute path of a PLC project's .tmc file. " +
                          @"The XAE writes it next to the PLC project as <PlcProject>\<PlcProject>.tmc; " +
                          "where that project lives is your choice.");

        string full;
        try
        {
            if (!Path.IsPathFullyQualified(path))
                return (null, $"tmcFile '{path}' must be an absolute path — the HMI server resolves it, not this process.");
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, $"tmcFile '{path}' is not a valid path ({ex.Message}).");
        }

        if (!Path.GetExtension(full).Equals(Extension, StringComparison.OrdinalIgnoreCase))
            return (null, $"tmcFile '{full}' does not end in {Extension}.");

        FileInfo info;
        try
        {
            info = new FileInfo(full);
            if (!info.Exists) return (null, $"tmcFile '{full}' does not exist.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (null, $"tmcFile '{full}' is not readable ({ex.Message}).");
        }

        if (info.Length == 0) return (null, $"tmcFile '{full}' is empty.");
        if (info.Length > MaxSizeBytes)
            return (null, $"tmcFile '{full}' is {info.Length / (1024 * 1024)} MB — larger than the " +
                          $"{MaxSizeBytes / (1024 * 1024)} MB this tool reads. Is that really a PLC TMC?");

        string header;
        int symbols;
        try
        {
            using var reader = new StreamReader(full, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            header = ReadHeader(reader, HeaderProbeChars);
            symbols = CountSymbols(reader, header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (null, $"tmcFile '{full}' could not be read ({ex.Message}).");
        }

        if (!header.Contains(RootElement, StringComparison.Ordinal))
            return (null, $"tmcFile '{full}' is not a TwinCAT module description — its first " +
                          $"{HeaderProbeChars} characters contain no '{RootElement}' element.");

        return (new TmcFileFacts(full, info.Length, symbols, GeneratedBy(header)), null);
    }

    private static string ReadHeader(TextReader reader, int chars)
    {
        var buffer = new char[chars];
        int read = reader.ReadBlock(buffer, 0, chars);
        return new string(buffer, 0, read);
    }

    /// <summary>
    /// Counts <see cref="SymbolElement"/> over the header plus the rest of the stream. TMC
    /// files are a single line, so this streams in chunks with an overlap of
    /// <c>SymbolElement.Length - 1</c> characters — a tag split across two chunks is still counted once.
    /// </summary>
    private static int CountSymbols(TextReader reader, string header)
    {
        int overlap = SymbolElement.Length - 1;
        int count = Occurrences(header);
        string tail = header.Length <= overlap ? header : header[^overlap..];

        var buffer = new char[64 * 1024];
        while (true)
        {
            int read = reader.ReadBlock(buffer, 0, buffer.Length);
            if (read == 0) break;
            string chunk = tail + new string(buffer, 0, read);
            count += Occurrences(chunk);
            tail = chunk.Length <= overlap ? chunk : chunk[^overlap..];
        }
        return count;

        static int Occurrences(string text)
        {
            int n = 0;
            for (int i = text.IndexOf(SymbolElement, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(SymbolElement, i + SymbolElement.Length, StringComparison.Ordinal))
                n++;
            return n;
        }
    }

    /// <summary>The <c>GeneratedBy</c> attribute of the root element ("TwinCAT XAE Plc" for a
    /// PLC project), or null. Reported, never used as a gate — other generators are legal.</summary>
    internal static string? GeneratedBy(string header)
    {
        const string marker = "GeneratedBy=\"";
        int start = header.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return null;
        start += marker.Length;
        int end = header.IndexOf('"', start);
        return end > start ? header[start..end] : null;
    }
}
