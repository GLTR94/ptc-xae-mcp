using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Scans a .core binary for embedded human-readable strings.
/// Does NOT reverse-engineer the 3S binary header — extraction is purely
/// printable-ASCII scan + timestamp regex, avoiding fabricated crash causes.
/// </summary>
internal static class CoreDumpStringExtractor
{
    private const int MinStringLength = 6;

    // HH:MM:SS or HH:MM:SS.fff  (time-of-day, anchored at word boundary)
    private static readonly Regex TimeOfDay =
        new(@"\b([01]\d|2[0-3]):([0-5]\d):([0-5]\d)(\.\d{1,3})?\b",
            RegexOptions.Compiled);

    // YYYY-MM-DD HH:MM:SS
    private static readonly Regex IsoDateTime =
        new(@"\b(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})\b",
            RegexOptions.Compiled);

    // DD.MM.YYYY HH:MM:SS  (TwinCAT locale format)
    private static readonly Regex TcDateTime =
        new(@"\b(\d{2})\.(\d{2})\.(\d{4}) (\d{2}):(\d{2}):(\d{2})\b",
            RegexOptions.Compiled);

    /// <summary>
    /// Extract printable ASCII strings of at least <see cref="MinStringLength"/> characters.
    /// </summary>
    public static IReadOnlyList<string> ExtractPrintableStrings(byte[] data)
    {
        var results = new List<string>();
        var current = new StringBuilder();

        foreach (byte b in data)
        {
            if (b >= 0x20 && b <= 0x7E)
            {
                current.Append((char)b);
            }
            else
            {
                if (current.Length >= MinStringLength)
                    results.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length >= MinStringLength)
            results.Add(current.ToString());

        return results;
    }

    /// <summary>
    /// Scan <paramref name="data"/> for embedded timestamps and state strings.
    /// Returns the latest timestamp found (used for hang-gap computation) and a
    /// deduplicated list of strings that look like app-log or state-machine output.
    /// </summary>
    public static (DateTime? LastTimestamp, IReadOnlyList<string> StateStrings)
        ExtractStateInfo(byte[] data, DateTime dumpWriteTime)
    {
        var strings = ExtractPrintableStrings(data);
        DateTime? lastTimestamp = null;
        var candidates = new List<string>();

        foreach (var s in strings)
        {
            bool added = false;

            // ISO datetime
            var m = IsoDateTime.Match(s);
            if (m.Success &&
                DateTime.TryParse(m.Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dt1))
            {
                UpdateLast(ref lastTimestamp, dt1);
                candidates.Add(s.Trim());
                added = true;
            }

            // TwinCAT locale datetime (DD.MM.YYYY HH:MM:SS)
            if (!added)
            {
                m = TcDateTime.Match(s);
                if (m.Success &&
                    DateTime.TryParseExact(m.Value, "dd.MM.yyyy HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
                {
                    UpdateLast(ref lastTimestamp, dt2);
                    candidates.Add(s.Trim());
                    added = true;
                }
            }

            // Time-of-day only — anchor to dump-write date, allow up to 24 h before
            if (!added)
            {
                m = TimeOfDay.Match(s);
                if (m.Success && s.Length < 200)
                {
                    if (TimeSpan.TryParse(m.Value.Split('.')[0], out var ts))
                    {
                        var candidate = dumpWriteTime.Date + ts;
                        if (candidate > dumpWriteTime)
                            candidate = candidate.AddDays(-1);
                        UpdateLast(ref lastTimestamp, candidate);
                    }
                    if (LooksInteresting(s))
                    {
                        candidates.Add(s.Trim());
                        added = true;
                    }
                }
            }

            if (!added && LooksInteresting(s))
                candidates.Add(s.Trim());
        }

        // Deduplicate preserving last occurrence (newest data last in binary)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deduped = new List<string>();
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (seen.Add(candidates[i]))
                deduped.Insert(0, candidates[i]);
        }

        return (lastTimestamp, deduped);
    }

    private static void UpdateLast(ref DateTime? current, DateTime candidate)
    {
        if (current == null || candidate > current)
            current = candidate;
    }

    private static bool LooksInteresting(string s)
    {
        if (s.Length < MinStringLength || s.Length > 256) return false;
        int alphaNum = s.Count(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '.');
        return alphaNum > s.Length / 2;
    }
}
