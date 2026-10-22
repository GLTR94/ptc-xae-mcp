namespace PtcXaeMcp.WinForms.TwinCat;

// NC (Motion / TINC) tree-item type discriminators. Captured empirically
// from the live XAE (SampleProject fixture, TC3 4026.22, 2026-05-18) via
// the DumpTincTree diagnostic. The browse-
// never-name hard constraint means every NC tree walk matches on these
// ItemType values, NOT on the (localized, German) node labels.
internal static class NcItemType
{
    public const int MotionRoot = 19;  // 'TINC' root, ItemName "MOTION"
    public const int NcTask     = 1;   // direct child of TINC (ItemSubType 0)
    public const int AxesList   = 21;  // single child of an NC-Task (ItemSubType 1)
    public const int Axis       = 22;  // child of the axes list (ItemSubType 1), carries <NcAxisDef>
    public const int Encoder    = 23;  // axis child (not created by this group)
    public const int Drive      = 24;  // axis child (not created by this group)
    public const int Controller = 25;  // axis child (not created by this group)

    // CreateChild SubType for both NC-Task and axis creation
    // (Infosys 242735499: TINC.CreateChild("NC-Task", 1); axes.CreateChild("Axis 1", 1)).
    public const int CreateSubType = 1;
}
