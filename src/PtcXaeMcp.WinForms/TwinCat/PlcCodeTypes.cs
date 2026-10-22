namespace PtcXaeMcp.WinForms.TwinCat;

// Kind of a PLC source-tree item as surfaced by twincat_list_pous. FOLDER is included
// so the list reflects the user-visible tree layout in XAE; tools that act on a single
// node accept POU/GVL/DUT paths only (folders are organisational, not patch targets).
internal static class PouKind
{
    public const string Pou       = "POU";
    public const string Gvl       = "GVL";
    public const string Dut       = "DUT";
    public const string Interface = "INTERFACE";
    public const string Folder    = "FOLDER";
}

// ItemType is the TREEITEMTYPES value of the node (null when not read); the plc_* tools derive the subtype from it.
internal sealed record PouEntry(
    string Path,
    string Name,
    string Kind,
    string? Language,
    int? ItemType = null);

internal sealed record PouBody(
    string Declaration,
    string Implementation,
    string Language);

// Kind of a structured POU member (feature/pou-handling). Mirrors PouKind but
// scoped to the children a POU can carry. GET/SET are PROPERTY accessors; they
// are real tree nodes (Infosys 613/614), not synthetic sub-parts.
internal static class PlcMemberKind
{
    public const string Method   = "METHOD";
    public const string Action   = "ACTION";
    public const string Property = "PROPERTY";
    public const string Get      = "GET";
    public const string Set      = "SET";
    public const string Transition = "TRANSITION";

    // Maps a verified PlcItemType to a member kind, or null for non-members.
    // Both the POU subtypes (609/611/613/614) and the interface subtypes
    // (610/612/654/655, Infosys 242732427) map to the same kinds, so a member
    // surfaces identically whether it lives under an FB or an INTERFACE — and we
    // stay robust to whichever subtype TC3 actually reports when enumerating.
    public static string? FromItemType(int itemType) => itemType switch
    {
        PlcItemType.Method or PlcItemType.InterfaceMethod     => Method,
        PlcItemType.Action                                    => Action,
        PlcItemType.Property or PlcItemType.InterfaceProperty => Property,
        PlcItemType.PropertyGet or PlcItemType.InterfaceGet   => Get,
        PlcItemType.PropertySet or PlcItemType.InterfaceSet   => Set,
        PlcItemType.Transition                                => Transition,
        _                                                     => null,
    };

    // Child nodes the member walk descends into: a PROPERTY (its Get/Set accessors are nested
    // under it) and a FOLDER (601) — a FB can group methods/properties in folders since TC 4024.14
    // (Infosys "PLC-Objekte anlegen"). Without the folder case every member inside such a folder
    // was missing from list_pou_members (fix/pou-members-in-folders).
    public static bool ShouldDescendInto(int itemType) =>
        itemType is PlcItemType.Folder or PlcItemType.Property or PlcItemType.InterfaceProperty;
}

// One structured member of a POU, as surfaced by twincat_list_pou_members.
internal sealed record PouMemberEntry(
    string Path,
    string Name,
    string Kind,
    int? ItemType = null);

// Which accessor(s) add_pou_member creates for kind=PROPERTY. Both was
// previously the only behaviour — on an INTERFACE that forces every
// implementer to write a Set even for a property that should be read-only.
internal enum PropertyAccessors { Get, Set, Both }

internal static class PropertyAccessorsParsing
{
    public static bool TryParse(string? raw, out PropertyAccessors value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) { value = PropertyAccessors.Both; return true; }
        switch (raw.Trim().ToUpperInvariant())
        {
            case "GET": value = PropertyAccessors.Get; return true;
            case "SET": value = PropertyAccessors.Set; return true;
            case "BOTH": value = PropertyAccessors.Both; return true;
            default:
                value = PropertyAccessors.Both;
                error = $"[Error] Unknown accessors '{raw}'. Expected GET, SET, or BOTH.";
                return false;
        }
    }
}

// Result of probing a PLC tree path before a mutating tool acts on it (issue #148):
// Exists=false → the path does not resolve; ItemType=null → the node exists but its
// ItemType could not be read (treated as "unknown kind", never as "deletable").
// ChildCount is the number of direct children (null when it could not be read; "unknown" is never "empty").
internal sealed record PlcNodeInfo(bool Exists, int? ItemType, int? ChildCount = null)
{
    public static readonly PlcNodeInfo Missing = new(false, null);
}
