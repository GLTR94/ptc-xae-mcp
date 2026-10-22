using System.Xml;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Read-back for twincat_plcopen_import (issue #152, B-14). The import call itself reports nothing
/// about what happened, so the top-level objects in the file are compared with a before/after
/// snapshot of the PLC tree (twincat_list_pous): new ones must appear, and objects that already
/// existed are counted as replaced / skipped / renamed according to the conflict mode. Pure.
/// </summary>
internal static class PlcOpenImportCheck
{
    // Top-level PLCopen objects: POUs, DUTs, GVLs (TwinCAT addData) and interfaces (TwinCAT addData).
    // Lower-case <interface> is a POU's declaration part and has no name — deliberately not matched.
    private static readonly HashSet<string> ObjectElements = new(StringComparer.Ordinal)
    {
        "pou", "dataType", "globalVars", "Interface",
    };

    /// <summary>Names of the top-level objects in a PLCopen XML document, in file order, without duplicates.</summary>
    public static IReadOnlyList<string> ObjectNames(string xml)
    {
        var doc = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(new StringReader(xml),
                   new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            doc.Load(reader);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(doc.DocumentElement, insideObject: false);
        return names;

        void Walk(XmlNode? node, bool insideObject)
        {
            if (node is null) return;
            bool isObject = node is XmlElement el && ObjectElements.Contains(el.LocalName) &&
                            el.GetAttribute("name") is { Length: > 0 };
            if (isObject && !insideObject)
            {
                string name = ((XmlElement)node).GetAttribute("name");
                if (seen.Add(name)) names.Add(name);
            }
            foreach (XmlNode child in node.ChildNodes)
                Walk(child, insideObject || isObject);
        }
    }

    /// <summary>
    /// Compares the file's objects with the PLC tree (non-folder entries) before and after the import.
    /// <paramref name="conflictMode"/>: 0=None, 1=Rename, 2=Replace, 3=Skip. An object from the file that
    /// is not in the tree afterwards, or a pre-existing object that is gone, is an error. Objects whose
    /// tree path changed are reported as moved (live, TC 4026.22: imported objects land at the PLC
    /// project root, and a Replace/Rename of an object in a folder leaves it at the root).
    /// </summary>
    public static Outcome Evaluate(IReadOnlyList<string> fileObjects, IReadOnlyList<PouEntry> before,
        IReadOnlyList<PouEntry> after, int conflictMode)
    {
        var beforeByName = ByName(before);
        var afterByName = ByName(after);
        var inFile = new HashSet<string>(fileObjects, StringComparer.OrdinalIgnoreCase);

        var added = fileObjects.Where(n => !beforeByName.ContainsKey(n) && afterByName.ContainsKey(n)).ToList();
        var missing = fileObjects.Where(n => !beforeByName.ContainsKey(n) && !afterByName.ContainsKey(n)).ToList();
        var conflicts = fileObjects.Where(beforeByName.ContainsKey).ToList();
        var lostConflicts = conflicts.Where(n => !afterByName.ContainsKey(n)).ToList();
        var otherNew = afterByName.Keys.Where(n => !beforeByName.ContainsKey(n) && !inFile.Contains(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var moved = conflicts
            .Where(n => afterByName.TryGetValue(n, out var a) &&
                        !string.Equals(a, beforeByName[n], StringComparison.OrdinalIgnoreCase))
            .Select(n => $"{n} ({beforeByName[n]} -> {afterByName[n]})")
            .ToList();

        List<string> replaced = conflictMode == 2 ? conflicts : [];
        List<string> skipped = conflictMode == 3 ? conflicts : [];
        List<string> renamedTo = conflictMode == 1 ? otherNew : [];

        string? error = null;
        if (fileObjects.Count == 0)
            error = "the file contains no POU, DUT, GVL or INTERFACE (nothing to import).";
        else if (missing.Count > 0)
            error = $"{missing.Count} object(s) from the file are not in the PLC tree after the import: {string.Join(", ", missing)}.";
        else if (lostConflicts.Count > 0)
            error = $"object(s) that existed before are gone after the import: {string.Join(", ", lostConflicts)}.";

        var parts = new List<string> { $"added {added.Count}" };
        if (conflictMode == 2) parts.Add($"replaced {replaced.Count}");
        if (conflictMode == 3) parts.Add($"skipped {skipped.Count} (already existed)");
        if (conflictMode == 1) parts.Add($"renamed {renamedTo.Count}{(renamedTo.Count > 0 ? $" ({string.Join(", ", renamedTo)})" : "")}");
        if (conflictMode == 0 && conflicts.Count > 0) parts.Add($"{conflicts.Count} already existed (conflictMode=None)");
        string summary = string.Join(", ", parts) +
                         (added.Count > 0 ? $" [{string.Join(", ", added)}]" : "") +
                         (conflictMode == 2 && replaced.Count > 0 ? $"; replaced: {string.Join(", ", replaced)} (present after the import; content not compared)" : "") +
                         (conflictMode == 1 && conflicts.Count > renamedTo.Count
                             ? $"; NOTE: {conflicts.Count} object(s) already existed but TwinCAT created only {renamedTo.Count} renamed " +
                               $"cop{(renamedTo.Count == 1 ? "y" : "ies")} — the existing names were imported over"
                             : "") +
                         (moved.Count > 0 ? $"; moved: {string.Join(", ", moved)}" : "");
        bool settled = error is null && (conflictMode != 1 || renamedTo.Count >= conflicts.Count);
        return new Outcome(added, replaced, skipped, renamedTo, missing, summary, error, settled);

        static Dictionary<string, string> ByName(IReadOnlyList<PouEntry> entries)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                if (e.Kind != PouKind.Folder) map.TryAdd(e.Name, e.Path);
            return map;
        }
    }

    internal sealed record Outcome(
        IReadOnlyList<string> Added,
        IReadOnlyList<string> Replaced,
        IReadOnlyList<string> Skipped,
        IReadOnlyList<string> Renamed,
        IReadOnlyList<string> Missing,
        string Summary,
        string? Error,
        // True when nothing more is expected to appear (a Rename may still be creating its copies).
        bool Settled);
}
