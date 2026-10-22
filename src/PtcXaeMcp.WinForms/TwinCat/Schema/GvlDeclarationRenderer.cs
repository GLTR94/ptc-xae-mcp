using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.WinForms.TwinCat.Schema;

/// <summary>
/// Pure GVL declaration-text rendering extracted from NativeTwinCatRunner.RenderGvlText /
/// GetTabPad. No COM, no I/O — just IoPoint data in, ST declaration text out.
/// </summary>
internal static class GvlDeclarationRenderer
{
    internal static string Render(IReadOnlyList<IoPoint> ioPoints)
    {
        var lines = new List<string>
        {
            "{attribute 'qualified_only'}",
            "VAR_GLOBAL",
            string.Empty
        };

        IoPoint[] sorted = ioPoints
            .OrderBy(p => p.TerminalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Channel)
            .ToArray();
        int maxVarLen = sorted.Length == 0 ? 0 : sorted.Max(p => p.VarName?.Length ?? 0);
        int maxTypeLen = sorted.Length == 0 ? 0 : sorted.Max(p => string.IsNullOrWhiteSpace(p.Type) ? 4 : p.Type!.Length);
        string? currentTerminal = null;

        foreach (var point in sorted)
        {
            if (!string.Equals(point.TerminalName, currentTerminal, StringComparison.OrdinalIgnoreCase))
            {
                currentTerminal = point.TerminalName;
                lines.Add($"    // {currentTerminal}");
            }

            string direction = point.Direction.Trim().ToUpperInvariant();
            string at = direction == "I" ? "%I*" : "%Q*";
            string type = string.IsNullOrWhiteSpace(point.Type) ? "BOOL" : point.Type!;
            string comment = $"{point.Group ?? string.Empty} {point.Description ?? string.Empty}".Trim();
            string tabsAfterVar = GetTabPad(point.VarName.Length, maxVarLen + 2);
            string tabsAfterType = GetTabPad(type.Length, maxTypeLen + 2);
            lines.Add($"    {point.VarName}{tabsAfterVar}AT {at}\t: {type};{tabsAfterType}// {comment}");
        }

        lines.Add(string.Empty);
        lines.Add("END_VAR");
        return string.Join(Environment.NewLine, lines);
    }

    internal static string GetTabPad(int currentLength, int targetLength, int tabSize = 4)
    {
        int delta = targetLength - currentLength;
        if (delta < 1)
        {
            return "\t";
        }

        int tabs = (int)Math.Ceiling(delta / (double)tabSize);
        return new string('\t', Math.Max(1, tabs));
    }
}
