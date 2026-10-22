namespace PtcXaeMcp.WinForms.TwinCat;

// Pure, COM-free classification helpers shared by the PLC-code tools. Extracted
// so the two decisions that actually matter — which CreateChild nSubType a member
// needs, and how a tree node's ItemType maps to a list kind — are unit-testable
// without a live XAE (the COM session just delegates here).
internal static class PlcCodeClassification
{
    // The CreateChild nSubType for a member of the given kind. Interface members
    // (parent ItemType 618) use DISTINCT subtypes from POU members
    // (Infosys 242732427): METHOD 610 vs 609, PROPERTY 612 vs 611, GET 654 vs 613,
    // SET 655 vs 614. ACTION (608) is a POU-only concept and has no interface form.
    public static int MemberSubType(bool parentIsInterface, string kind) => kind switch
    {
        PlcMemberKind.Method   => parentIsInterface ? PlcItemType.InterfaceMethod   : PlcItemType.Method,
        PlcMemberKind.Property => parentIsInterface ? PlcItemType.InterfaceProperty : PlcItemType.Property,
        PlcMemberKind.Get      => parentIsInterface ? PlcItemType.InterfaceGet      : PlcItemType.PropertyGet,
        PlcMemberKind.Set      => parentIsInterface ? PlcItemType.InterfaceSet      : PlcItemType.PropertySet,
        PlcMemberKind.Action   => PlcItemType.Action,
        _ => throw new InvalidOperationException(
            $"Unsupported member kind '{kind}'. Expected METHOD, ACTION, PROPERTY, GET, or SET."),
    };

    // An INTERFACE declares only METHODs and PROPERTYs (with GET/SET accessors).
    // ACTION is not a valid interface child — reject it before the CreateChild
    // call so the caller gets a clear message, not a raw COM failure.
    public static bool MemberKindValidOnInterface(string kind) =>
        kind is PlcMemberKind.Method or PlcMemberKind.Property
             or PlcMemberKind.Get or PlcMemberKind.Set;

    // Map a PLC tree node's ItemType to the kind surfaced by twincat_list_pous,
    // or null for an item type the list does not represent (skipped during the
    // walk rather than guessed). INTERFACE (618) is its own kind so it is no
    // longer silently dropped from the listing.
    public static string? PouKindOf(int itemType) => itemType switch
    {
        PlcItemType.Folder => PouKind.Folder,
        PlcItemType.PouProgram or PlcItemType.PouFunction or PlcItemType.PouFunctionBlock => PouKind.Pou,
        PlcItemType.Gvl or PlcItemType.GvlParamList => PouKind.Gvl,
        PlcItemType.Dut or PlcItemType.DutStruct or PlcItemType.DutUnion or PlcItemType.DutAlias => PouKind.Dut,
        PlcItemType.Interface => PouKind.Interface,
        _ => null,
    };

    // INTERFACE objects are declaration-only: they expose ITcPlcDeclaration but no
    // ITcPlcImplementation, so read_pou must not attempt to read an implementation
    // body for them (it would throw). True ⇒ read declaration only, empty impl.
    public static bool IsDeclarationOnly(int itemType) =>
        itemType == PlcItemType.Interface;
}
