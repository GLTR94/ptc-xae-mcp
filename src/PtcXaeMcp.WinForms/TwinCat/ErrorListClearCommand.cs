namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// The XAE Error List "clear" context-menu command (issue #150, C-16). Its name is localized
/// ("OtherContextMenus.ErrorList.Löschen" on a German XAE, "…Clear" on an English one); its
/// identity — command-set GUID + ID — is not (read from dte.Commands on TcXaeShell 4026.22).
/// </summary>
internal static class ErrorListClearCommand
{
    public const string Guid = "{3B11520B-7E70-4008-A6CF-B60AE84E12B1}";
    public const int Id = 24608;

    public const string GermanName = "OtherContextMenus.ErrorList.Löschen";
    public const string EnglishName = "OtherContextMenus.ErrorList.Clear";

    /// <summary>
    /// Names to try, in order: the one the running shell reports for the GUID/ID (whatever its
    /// language), then the known German and English names. Distinct, never empty.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? resolvedName)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(resolvedName)) list.Add(resolvedName.Trim());
        foreach (var n in new[] { GermanName, EnglishName })
            if (!list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
        return list;
    }
}
