namespace PtcXaeMcp.WinForms.TwinCat;

// Issue #148 (LIVE-8 / B-4, P0): twincat_delete_plc_object used a BLACKLIST (folders +
// single-segment paths) and treated an unreadable ItemType (-1) as deletable. Deleting
// "TIPC^<plc>^<plc> Project" removed the whole PLC project and reported "Deleted".
// This is the whitelist that replaces it: only source objects and their members may be
// deleted; the PLC project root, the PLC instance, References, folders, visualisations,
// anything unknown and anything whose ItemType cannot be read are refused.
internal static class PlcDeletePolicy
{
    // Infosys TREEITEMTYPES (see PlcItemType). 616 = PLCTRANSITION (SFC transition object).
    public const int Transition = PlcItemType.Transition;

    private static readonly Dictionary<int, string> DeletableKinds = new()
    {
        [PlcItemType.PouProgram]        = "PROGRAM",
        [PlcItemType.PouFunction]       = "FUNCTION",
        [PlcItemType.PouFunctionBlock]  = "FUNCTION_BLOCK",
        [PlcItemType.Dut]               = "DUT (ENUM)",
        [PlcItemType.DutStruct]         = "DUT (STRUCT)",
        [PlcItemType.DutUnion]          = "DUT (UNION)",
        [PlcItemType.DutAlias]          = "DUT (ALIAS)",
        [PlcItemType.Gvl]               = "GVL",
        [PlcItemType.GvlParamList]      = "GVL (parameter list)",
        [PlcItemType.Interface]         = "INTERFACE",
        [PlcItemType.Action]            = "ACTION",
        [PlcItemType.Method]            = "METHOD",
        [PlcItemType.InterfaceMethod]   = "METHOD (interface)",
        [PlcItemType.Property]          = "PROPERTY",
        [PlcItemType.InterfaceProperty] = "PROPERTY (interface)",
        [PlcItemType.PropertyGet]       = "PROPERTY Get",
        [PlcItemType.PropertySet]       = "PROPERTY Set",
        [PlcItemType.InterfaceGet]      = "PROPERTY Get (interface)",
        [PlcItemType.InterfaceSet]      = "PROPERTY Set (interface)",
        [Transition]                    = "TRANSITION",
        // Created, renamed and deleted live 2026-10-07 (feature/plc-objects R4); their content is not reachable.
        [PlcItemType.TextList]          = "TEXTLIST",
        [PlcItemType.ImagePool]         = "IMAGEPOOL",
        [PlcItemType.VisObj]            = "VISU (visualization)",
        [PlcItemType.VisMan]            = "VISU (manager)",
        [PlcItemType.TargetVisu]        = "VISU (target)",
        [PlcItemType.WebVisu]           = "VISU (web)",
        [PlcItemType.UmlClassDiagram]   = "UML class diagram",
        [PlcItemType.RecipeManager]     = "RECIPE (manager)",
        [PlcItemType.RecipeDefinition]  = "RECIPE (definition)",
        [PlcItemType.ProgRef]           = "TASKREF (program)",
    };

    // Kinds refused with a specific explanation (everything else not whitelisted gets
    // the generic "not a deletable PLC source object" message).
    private static string? Describe(int itemType) => itemType switch
    {
        PlcItemType.PlcApp => "the PLC project root",
        PlcItemType.Folder => "a folder",
        617                => "the References (library manager) node",
        _                  => null,
    };

    public const string AllowedList =
        "POU (PROGRAM/FUNCTION/FUNCTION_BLOCK), DUT, GVL, INTERFACE, METHOD, ACTION, PROPERTY, Get/Set, TRANSITION, " +
        "TEXTLIST, IMAGEPOOL, VISU, RECIPE, UML, TASKREF (program)";

    public static bool IsDeletable(int itemType) => DeletableKinds.ContainsKey(itemType);

    /// <summary>Null when <paramref name="node"/> may be deleted, otherwise an "[Error] ..." string.</summary>
    public static string? Check(string treePath, PlcNodeInfo node)
    {
        if (!node.Exists)
            return $"[Error] PLC tree path not found: {treePath}";
        if (node.ItemType is not int it)
            return $"[Error] Refusing to delete '{treePath}': its ItemType could not be read, so it " +
                   $"cannot be proven to be a PLC source object. Deletable: {AllowedList}.";
        if (IsDeletable(it))
            return null;
        string what = Describe(it) ?? $"not a deletable PLC source object (ItemType {it})";
        return $"[Error] Refusing to delete '{treePath}': it is {what}. " +
               $"This tool deletes only {AllowedList}.";
    }

    /// <summary>Null when <paramref name="node"/> is an existing folder, otherwise an "[Error] ..." string.</summary>
    public static string? CheckFolder(string treePath, PlcNodeInfo node)
    {
        if (!node.Exists)
            return $"[Error] PLC tree path not found: {treePath}";
        if (node.ItemType != PlcItemType.Folder)
            return $"[Error] Refusing to delete '{treePath}' as a folder: " +
                   (node.ItemType is int it ? $"its ItemType is {it}, not a folder." : "its ItemType could not be read.");
        return null;
    }

    public static string KindName(int itemType) =>
        DeletableKinds.TryGetValue(itemType, out var k) ? k : $"ItemType {itemType}";
}
