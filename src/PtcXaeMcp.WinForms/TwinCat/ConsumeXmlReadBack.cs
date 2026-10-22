using System.Globalization;
using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Issue #152 D-23: ConsumeXml returns nothing about what it applied. After the write, every leaf value of the
/// written XML must appear again in ProduceXml at the same element path (sibling indices ignored, because a
/// partial input does not repeat the siblings before the one it changes). Numbers compare by value, so
/// "#x1000", "0x1000" and "4096" are equal; other text compares case-insensitively after trimming.
/// </summary>
internal static class ConsumeXmlReadBack
{
    public static IReadOnlyList<string> Differences(string written, string readBack, int max = 20)
    {
        var writtenLeaves = Leaves(XDocument.Parse(written));
        var readValues = Leaves(XDocument.Parse(readBack))
            .GroupBy(l => l.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Value).ToList(), StringComparer.Ordinal);

        var diffs = new List<string>();
        foreach (var (path, value) in writtenLeaves)
        {
            if (!readValues.TryGetValue(path, out var candidates))
                diffs.Add($"{path}: wrote '{value}', not present in the read-back");
            else if (!candidates.Any(c => SameValue(value, c)))
                diffs.Add($"{path}: wrote '{value}', read back '{string.Join("' | '", candidates.Take(3))}'");
            if (diffs.Count >= max) break;
        }
        return diffs;
    }

    private static List<(string Path, string Value)> Leaves(XDocument doc)
    {
        var leaves = new List<(string, string)>();
        if (doc.Root is null) return leaves;
        foreach (var e in doc.Root.DescendantsAndSelf())
        {
            string path = string.Join("/", e.AncestorsAndSelf().Reverse().Select(a => a.Name.LocalName));
            foreach (var a in e.Attributes().Where(a => !a.IsNamespaceDeclaration))
                leaves.Add(($"{path}/@{a.Name.LocalName}", a.Value.Trim()));
            if (!e.HasElements && !string.IsNullOrWhiteSpace(e.Value))
                leaves.Add((path, e.Value.Trim()));
        }
        return leaves;
    }

    internal static bool SameValue(string a, string b)
    {
        if (TryNumber(a, out var x) && TryNumber(b, out var y)) return x == y;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNumber(string s, out decimal value)
    {
        s = s.Trim();
        string? hex = s.StartsWith("#x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? s[2..] : null;
        if (hex is not null)
        {
            bool ok = ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong u);
            value = u;
            return ok;
        }
        return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
