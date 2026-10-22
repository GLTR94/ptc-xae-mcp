using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Reads and writes the &lt;ManualSelect&gt; license entries inside a TwinCAT
/// system-manager project file (.tsproj).
///
/// In the project XML these live at:
///   TcSmProject/Project/System/Licenses/Target/ManualSelect
/// and represent the licenses manually checked in the "Lizenzen verwalten"
/// (Manage Licenses) tab of the TwinCAT License Manager.  They drive which
/// licenses are generated when clicking "Aktiviere 7 Tage Testlizenz".
///
/// The Automation Interface node TIRC^License exposes only the "Lizenz
/// Anforderung" (license-request submission) form and CANNOT be used to
/// read or write the ManualSelect list.  Direct .tsproj file access is the
/// only reliable mechanism (confirmed empirically on TC 4026.22).
/// </summary>
internal static class ManualSelectXml
{
    private static readonly Regex ManualSelectLineRx = new(
        @"[ \t]*<ManualSelect>([^<]*)</ManualSelect>[ \t]*\r?\n?",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses all &lt;ManualSelect&gt; GUID entries from the .tsproj XML.
    /// Returns an empty list on any parse error.
    /// </summary>
    internal static IReadOnlyList<RequiredLicense> Parse(string tsprojXml)
    {
        var result = new List<RequiredLicense>();
        foreach (Match m in ManualSelectLineRx.Matches(tsprojXml))
        {
            string value = m.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(value))
                result.Add(new RequiredLicense(value, null, null));
        }
        return result;
    }

    /// <summary>
    /// Returns true when <paramref name="licenses"/> contains a &lt;ManualSelect&gt;
    /// entry matching <paramref name="licenseId"/> or <paramref name="orderNo"/>
    /// (case-insensitive, braces stripped for GUID comparison).
    /// </summary>
    internal static bool Contains(
        IReadOnlyList<RequiredLicense> licenses, string? licenseId, string? orderNo)
    {
        if (licenses.Count == 0) return false;
        string? normId    = NormGuid(licenseId);
        string? normOrder = orderNo?.Trim();

        return licenses.Any(l =>
        {
            if (normId    is not null && NormGuid(l.LicenseId)?.Equals(normId,    StringComparison.OrdinalIgnoreCase) == true) return true;
            if (normOrder is not null && l.LicenseId.Equals(normOrder, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        });
    }

    /// <summary>
    /// Issue #148 (B-3): a ManualSelect value is a GUID (TcSmProject.xsd, LicenseAreaType).
    /// Returns the canonical "{XXXXXXXX-...}" form, or null for anything that is not a GUID
    /// (an order number such as "TC1200" used to be written verbatim as "{TC1200}").
    /// </summary>
    internal static string? ValidateAddInput(string? licenseId, string? orderNo)
    {
        if (string.IsNullOrWhiteSpace(licenseId))
            return string.IsNullOrWhiteSpace(orderNo)
                ? "licenseId is required (a license GUID, e.g. {854588BA-345B-43F2-9354-0CF7AFF76EA3})."
                : $"orderNo '{orderNo}' alone cannot be added: the .tsproj <ManualSelect> list takes the " +
                  "license GUID, and an order number cannot be resolved to one here. Pass licenseId " +
                  "(the GUID shown in the TwinCAT License Manager).";
        if (TryFormatGuid(licenseId) is null)
            return $"licenseId '{licenseId}' is not a GUID. Expected a license GUID such as " +
                   "{854588BA-345B-43F2-9354-0CF7AFF76EA3}.";
        return null;
    }

    internal static string? TryFormatGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Guid.TryParse(value.Trim(), out var g) ? "{" + g.ToString("D").ToUpperInvariant() + "}" : null;
    }

    /// <summary>
    /// Inserts a &lt;ManualSelect&gt; entry for the GUID <paramref name="licenseId"/> into the
    /// project's &lt;System&gt;&lt;Licenses&gt;&lt;Target&gt; section and returns the modified XML.
    /// Missing &lt;Licenses&gt; / &lt;Target&gt; sections are CREATED at their schema position
    /// (a fresh .tsproj has neither — the old code then returned the XML unchanged while the tool
    /// reported "added", issue #148). The entry goes after the last existing ManualSelect (or
    /// CustomComment), i.e. before TargetSelect/LicenseDevice as the schema sequence requires.
    /// Throws <see cref="InvalidOperationException"/> when the id is not a GUID or the project
    /// structure has no &lt;System&gt; element to host the section; never returns unchanged XML.
    /// </summary>
    internal static string Add(string tsprojXml, string licenseId)
    {
        string formatted = TryFormatGuid(licenseId)
            ?? throw new InvalidOperationException(
                $"'{licenseId}' is not a license GUID. <ManualSelect> takes a GUID such as " +
                "{854588BA-345B-43F2-9354-0CF7AFF76EA3}.");

        string nl = tsprojXml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var system = FindElement(tsprojXml, "System", 0, tsprojXml.Length)
            ?? throw new InvalidOperationException(
                "The .tsproj has no <System> element — cannot add a <Licenses> section.");
        if (system.SelfClosing)
            throw new InvalidOperationException(
                "The .tsproj <System/> element is empty (self-closing) — refusing to restructure it.");

        var licenses = FindChild(tsprojXml, system, "Licenses");
        if (licenses is null)
        {
            // <Licenses> follows <Settings> (schema order Settings, Licenses, Tasks, ...).
            string sysIndent = LineIndent(tsprojXml, system.Start);
            string ci = sysIndent + "\t";
            var settings = FindChild(tsprojXml, system, "Settings");
            int insertAt = settings is not null
                ? LineEnd(tsprojXml, settings.End, nl)
                : LineEnd(tsprojXml, system.OpenEnd, nl);
            string block =
                $"{ci}<Licenses>{nl}{ci}\t<Target>{nl}{ci}\t\t<ManualSelect>{formatted}</ManualSelect>{nl}" +
                $"{ci}\t</Target>{nl}{ci}</Licenses>{nl}";
            return tsprojXml.Insert(insertAt, block);
        }
        if (licenses.SelfClosing)
        {
            string li = LineIndent(tsprojXml, licenses.Start);
            string open = tsprojXml[licenses.Start..licenses.OpenEnd].TrimEnd('>').TrimEnd().TrimEnd('/').TrimEnd() + ">";
            string block =
                $"{open}{nl}{li}\t<Target>{nl}{li}\t\t<ManualSelect>{formatted}</ManualSelect>{nl}{li}\t</Target>{nl}{li}</Licenses>";
            return tsprojXml[..licenses.Start] + block + tsprojXml[licenses.End..];
        }

        var target = FindChild(tsprojXml, licenses, "Target");
        if (target is null)
        {
            // <Target> is the first child of <Licenses>.
            string ti = LineIndent(tsprojXml, licenses.Start) + "\t";
            int insertAt = LineEnd(tsprojXml, licenses.OpenEnd, nl);
            string block = $"{ti}<Target>{nl}{ti}\t<ManualSelect>{formatted}</ManualSelect>{nl}{ti}</Target>{nl}";
            return tsprojXml.Insert(insertAt, block);
        }
        if (target.SelfClosing)
        {
            string ti = LineIndent(tsprojXml, target.Start);
            string block = $"<Target>{nl}{ti}\t<ManualSelect>{formatted}</ManualSelect>{nl}{ti}</Target>";
            return tsprojXml[..target.Start] + block + tsprojXml[target.End..];
        }

        // Insert after the last ManualSelect / CustomComment inside <Target>, else first.
        string itemIndent = LineIndent(tsprojXml, target.Start) + "\t";
        int after = -1;
        foreach (var name in new[] { "ManualSelect", "CustomComment" })
        {
            int idx = target.OpenEnd;
            while (true)
            {
                var el = FindElement(tsprojXml, name, idx, target.CloseStart);
                if (el is null) break;
                after = Math.Max(after, el.End);
                itemIndent = LineIndent(tsprojXml, el.Start);
                idx = el.End;
            }
            if (after >= 0) break;
        }
        int at = LineEnd(tsprojXml, after >= 0 ? after : target.OpenEnd, nl);
        return tsprojXml.Insert(at, $"{itemIndent}<ManualSelect>{formatted}</ManualSelect>{nl}");
    }

    // ---- minimal, text-preserving element locator (the .tsproj is rewritten by XAE on save,
    // so we keep its exact formatting instead of round-tripping it through an XML writer) ----

    private sealed record ElementSpan(int Start, int OpenEnd, int CloseStart, int End, bool SelfClosing);

    // First <name ...> ... </name> (or <name .../>) whose start lies in [from, to).
    private static ElementSpan? FindElement(string xml, string name, int from, int to)
    {
        var rx = new Regex($@"<{name}(?=[\s/>])", RegexOptions.CultureInvariant);
        var m = rx.Match(xml, from);
        if (!m.Success || m.Index >= to) return null;
        int gt = xml.IndexOf('>', m.Index);
        if (gt < 0) return null;
        int openEnd = gt + 1;
        if (xml[gt - 1] == '/')
            return new ElementSpan(m.Index, openEnd, openEnd, openEnd, true);
        // Depth-aware search for the matching close tag.
        var tag = new Regex($@"<(/?){name}(?=[\s/>])", RegexOptions.CultureInvariant);
        int depth = 1, pos = openEnd;
        while (depth > 0)
        {
            var t = tag.Match(xml, pos);
            if (!t.Success) return null;
            int tgt = xml.IndexOf('>', t.Index);
            if (tgt < 0) return null;
            if (t.Groups[1].Value == "/") depth--;
            else if (xml[tgt - 1] != '/') depth++;
            if (depth == 0)
                return new ElementSpan(m.Index, openEnd, t.Index, tgt + 1, false);
            pos = tgt + 1;
        }
        return null;
    }

    // Direct child <name> of parent (skips same-named elements nested deeper).
    private static ElementSpan? FindChild(string xml, ElementSpan parent, string name)
    {
        int pos = parent.OpenEnd;
        var anyOpen = new Regex(@"<([A-Za-z_][\w.\-]*)(?=[\s/>])", RegexOptions.CultureInvariant);
        while (pos < parent.CloseStart)
        {
            var m = anyOpen.Match(xml, pos);
            if (!m.Success || m.Index >= parent.CloseStart) return null;
            var el = FindElement(xml, m.Groups[1].Value, m.Index, m.Index + 1);
            if (el is null) return null;
            if (m.Groups[1].Value == name) return el;
            pos = el.End;
        }
        return null;
    }

    private static string LineIndent(string xml, int index)
    {
        int ls = index;
        while (ls > 0 && xml[ls - 1] != '\n') ls--;
        int e = ls;
        while (e < xml.Length && (xml[e] == ' ' || xml[e] == '\t')) e++;
        return xml[ls..e];
    }

    // Position just after the line break that follows index (start of the next line).
    private static int LineEnd(string xml, int index, string nl)
    {
        int n = xml.IndexOf('\n', index);
        return n < 0 ? xml.Length : n + 1;
    }

    /// <summary>
    /// Removes all &lt;ManualSelect&gt; lines whose value matches
    /// <paramref name="licenseId"/> (GUID, braces-insensitive) or
    /// <paramref name="orderNo"/> (exact match).
    /// Returns (modifiedXml, removedCount).
    /// </summary>
    internal static (string Xml, int RemovedCount) Remove(
        string tsprojXml, string? licenseId, string? orderNo)
    {
        string? normId    = NormGuid(licenseId);
        string? normOrder = orderNo?.Trim();

        int removed = 0;
        string result = ManualSelectLineRx.Replace(tsprojXml, m =>
        {
            string value = m.Groups[1].Value.Trim();
            bool idMatch    = normId    is not null && NormGuid(value)?.Equals(normId,    StringComparison.OrdinalIgnoreCase) == true;
            bool orderMatch = normOrder is not null && value.Equals(normOrder, StringComparison.OrdinalIgnoreCase);
            if (idMatch || orderMatch) { removed++; return string.Empty; }
            return m.Value; // keep
        });
        return (result, removed);
    }

    // Returns the GUID string without braces, for case-insensitive comparison.
    private static string? NormGuid(string? guid) =>
        string.IsNullOrEmpty(guid) ? null : guid.Trim('{', '}', ' ');

    // Formats a GUID as {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX} (uppercase, braces).
    private static string FormatGuid(string guid) =>
        "{" + guid.Trim('{', '}', ' ').ToUpperInvariant() + "}";
}
