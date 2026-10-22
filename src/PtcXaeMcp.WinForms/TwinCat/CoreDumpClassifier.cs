using System.Text.RegularExpressions;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Classifies a .core file path into a <see cref="CoreDumpKind"/> and extracts the ADS port number.
/// Pure static — no file I/O.
/// </summary>
internal static class CoreDumpClassifier
{
    // Port_851_CallStackDump_MyName.A1B2C3D4.core
    private static readonly Regex CallStackPattern =
        new(@"^Port_\d+_CallStackDump_.+\.[0-9A-Fa-f]+\.core$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Path component: …\CoreDump\Port_851.001\… (auto/exception nested layout)
    private static readonly Regex AutoExceptionDirPattern =
        new(@"[/\\]CoreDump[/\\]Port_\d+\.\d+[/\\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PortInFileName =
        new(@"^Port_(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PortInDir =
        new(@"Port_(\d+)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static CoreDumpKind ClassifyPath(string corePath)
    {
        var fileName = Path.GetFileName(corePath);

        if (CallStackPattern.IsMatch(fileName))
            return CoreDumpKind.CallStackOnly;

        if (AutoExceptionDirPattern.IsMatch(corePath))
            return CoreDumpKind.AutoException;

        // Manual: <Name>.<GUID>.core  — last dotted segment of name-without-extension is a GUID
        var stem = Path.GetFileNameWithoutExtension(corePath);
        var lastDot = stem.LastIndexOf('.');
        if (lastDot >= 0 && Guid.TryParse(stem[(lastDot + 1)..], out _))
            return CoreDumpKind.Manual;

        return CoreDumpKind.Unknown;
    }

    public static int? ExtractPort(string corePath)
    {
        var fileName = Path.GetFileName(corePath);
        var fm = PortInFileName.Match(fileName);
        if (fm.Success && int.TryParse(fm.Groups[1].Value, out int p1))
            return p1;

        var dm = AutoExceptionDirPattern.Match(corePath);
        if (dm.Success)
        {
            var pm = PortInDir.Match(dm.Value);
            if (pm.Success && int.TryParse(pm.Groups[1].Value, out int p2))
                return p2;
        }

        return null;
    }
}
