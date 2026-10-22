using System.Xml;
using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// hmi-ads-tmc #166: which .tmc files of the open
/// solution COULD be attached to an HMI project. TE2000's own "Browse solution for TMC file"
/// dialog (decompiled: <c>AttachTmcFileViewModel</c> / <c>BrowseSolutionForTmcFileWindow</c>)
/// reads <c>TreeItem/PlcProjectDef/RelativeTmcPath</c> from each TIPC child's ProduceXml and
/// combines it with the directory of the .tsproj; this mirrors that, COM-free, so it is unit-testable.
/// Candidates only: nothing here picks one — a guessed path is exactly the error the attach
/// pre-checks exist to prevent.
/// </summary>
internal static class HmiTmcCandidates
{
    internal const string PlcProjectDefElement = "PlcProjectDef";
    internal const string RelativeTmcPathElement = "RelativeTmcPath";

    /// <summary>
    /// <c>TreeItem/PlcProjectDef/RelativeTmcPath</c> of one TIPC child's ProduceXml, or a reason
    /// why there is none. Element names are matched by local name (the XML carries no namespace
    /// today; tolerating one costs nothing).
    /// </summary>
    internal static (string? RelativeTmcPath, string? Error) ParseRelativeTmcPath(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return (null, "the PLC project node returned no XML");

        XElement root;
        try { root = XDocument.Parse(xml).Root ?? throw new XmlException("no root element"); }
        catch (XmlException ex) { return (null, $"the PLC project node's XML is not well-formed ({ex.Message})"); }

        XElement? def = root.Name.LocalName == PlcProjectDefElement
            ? root
            : root.Elements().FirstOrDefault(e => e.Name.LocalName == PlcProjectDefElement);
        if (def is null)
            return (null, $"no <{PlcProjectDefElement}> in the node's XML — not a PLC project node?");

        string? relative = def.Elements()
            .FirstOrDefault(e => e.Name.LocalName == RelativeTmcPathElement)?.Value.Trim();
        return string.IsNullOrEmpty(relative)
            ? (null, $"<{PlcProjectDefElement}> has no {RelativeTmcPathElement} — the project names no TMC file")
            : (relative, null);
    }

    /// <summary>
    /// <c>Path.Combine(&lt;directory of the .tsproj&gt;, relativeTmcPath)</c>, normalized — TE2000's
    /// own resolution. Null (with a reason) when the .tsproj path is unknown or the result is no path.
    /// </summary>
    internal static (string? AbsolutePath, string? Error) Resolve(string? tcProjectFile, string relativeTmcPath)
    {
        string? directory;
        try { directory = string.IsNullOrWhiteSpace(tcProjectFile) ? null : Path.GetDirectoryName(tcProjectFile); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException) { directory = null; }
        if (string.IsNullOrEmpty(directory))
            return (null, "the XAE reported no .tsproj path for this TwinCAT project, so the relative path cannot be resolved");

        try { return (Path.GetFullPath(Path.Combine(directory, relativeTmcPath)), null); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, $"'{relativeTmcPath}' cannot be combined with '{directory}' ({ex.Message})");
        }
    }

    /// <summary>
    /// One candidate per PLC project. <paramref name="refuseRead"/> returns a reason when a file
    /// must not be opened (the same path policy twincat_hmi_attach_tmc_file applies); such a file
    /// still reports exists/lastWriteUtc, just no symbol count.
    /// </summary>
    internal static IReadOnlyList<HmiTmcCandidate> Build(
        IReadOnlyList<PlcProjectXml> projects,
        Func<string?, (TmcFileFacts? Facts, string? Error)> inspector,
        Func<string, string?> refuseRead)
    {
        var candidates = new List<HmiTmcCandidate>(projects.Count);
        foreach (PlcProjectXml project in projects)
            candidates.Add(BuildOne(project, inspector, refuseRead));
        return candidates;
    }

    private static HmiTmcCandidate BuildOne(
        PlcProjectXml project,
        Func<string?, (TmcFileFacts? Facts, string? Error)> inspector,
        Func<string, string?> refuseRead)
    {
        if (project.Xml is null)
            return Missing(project, null, null, project.Error ?? "the PLC project node's XML could not be read");

        var (relative, parseError) = ParseRelativeTmcPath(project.Xml);
        if (relative is null) return Missing(project, null, null, parseError);

        var (absolute, resolveError) = Resolve(project.TcProjectFile, relative);
        if (absolute is null) return Missing(project, relative, null, resolveError);

        FileInfo info;
        try
        {
            info = new FileInfo(absolute);
            if (!info.Exists)
                return Missing(project, relative, absolute,
                    "the file does not exist (yet) — the XAE writes it when the PLC project is built");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Missing(project, relative, absolute, $"the file cannot be checked ({ex.Message})");
        }
        DateTime lastWriteUtc = info.LastWriteTimeUtc;

        if (refuseRead(absolute) is { } refusal)
            return new HmiTmcCandidate(project.TcProject, project.PlcProject, relative, absolute, true, lastWriteUtc,
                null, $"not opened: {refusal}");

        var (facts, inspectError) = inspector(absolute);
        if (facts is null)
            return new HmiTmcCandidate(project.TcProject, project.PlcProject, relative, absolute, true, lastWriteUtc,
                null, inspectError);
        return new HmiTmcCandidate(project.TcProject, project.PlcProject, relative, absolute, true, lastWriteUtc,
            facts.SymbolCount,
            facts.SymbolCount == 0 ? "the file contains no <Symbol> entry — twincat_hmi_attach_tmc_file would refuse it" : null);
    }

    private static HmiTmcCandidate Missing(PlcProjectXml project, string? relative, string? absolute, string? note) =>
        new(project.TcProject, project.PlcProject, relative, absolute, false, null, null, note);
}
