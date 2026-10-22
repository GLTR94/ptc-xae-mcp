namespace PtcXaeMcp.WinForms.TwinCat;

// Name check for PLC FOLDER nodes. Unlike a POU, a folder is not an IEC identifier: "01_Application" and
// "Members" are both common. Only what the project tree cannot store is refused — an empty name, the tree
// path separator '^' and characters a file system rejects (a folder becomes a directory in the project).
internal static class PlcFolderName
{
    private static readonly char[] Forbidden = ['^', '\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>Null when <paramref name="name"/> is usable as a folder name, otherwise an "[Error] ..." string.</summary>
    public static string? Validate(string? name, string what)
    {
        if (string.IsNullOrWhiteSpace(name))
            return $"[Error] {what} is required.";
        string n = name.Trim();
        int bad = n.IndexOfAny(Forbidden);
        if (bad >= 0)
            return $"[Error] {what} '{n}' contains '{n[bad]}', which a PLC folder name cannot contain.";
        if (n.Any(char.IsControl))
            return $"[Error] {what} '{n}' contains a control character.";
        if (n.EndsWith('.'))
            return $"[Error] {what} '{n}' must not end with a dot.";
        return null;
    }
}
