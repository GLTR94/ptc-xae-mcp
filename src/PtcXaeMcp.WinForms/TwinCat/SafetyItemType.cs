namespace PtcXaeMcp.WinForms.TwinCat;

// Safety (TISC) tree-item type discriminators, captured empirically by the safety recon spike
// (not documented on Infosys). Mirrors NcItemType: every safety tree walk
// matches on these values, never on the (localized) node labels (issue #153, D-27).
internal static class SafetyItemType
{
    public const int ConfigurationRoot = 505; // 'TISC' / 'SAFETY' root
    public const int SafetyProject     = 800; // ItemSubTypeName "Safety Project", backed by *.splcproj
    public const int TcComInstance     = 48;  // the safety project's TcCOM runtime instance

    // Candidate CreateChild sub types swept by the (diagnostics-only) write probe. 1/2/100 are generic
    // low ids; EtherCatVInfo.BoxItemSubType (9099) is the vInfo-based box sub type of the EtherCAT tree.
    public static readonly int[] ProbeCreateChildSubTypes =
        [1, 2, TcComInstance, 100, ConfigurationRoot, SafetyProject, EtherCatVInfo.BoxItemSubType];
}
