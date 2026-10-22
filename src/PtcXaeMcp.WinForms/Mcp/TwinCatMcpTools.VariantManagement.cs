using ModelContextProtocol.Server;
using PtcXaeMcp.WinForms.TwinCat;
using System.ComponentModel;

namespace PtcXaeMcp.WinForms;

internal sealed partial class TwinCatMcpTools
{
    [McpServerTool(Name = "twincat_get_variant_status", ReadOnly = true)]
    [Description("Returns the active variant and all available variants/groups for the open TwinCAT project. " +
                 "Returns JSON { activeVariant, availableVariants, groups } or [Error] string. " +
                 "Returns empty lists (not an error) when the project has no Variant Management configured.")]
    public string GetVariantStatus(CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_get_variant_status", null, () =>
            bridge.Dispatcher.Run(() =>
                new VariantManagementCoordinator(bridge.Dispatcher.Session).GetVariantStatus()), cancellationToken);

    [McpServerTool(Name = "twincat_set_active_variant", ReadOnly = false, Destructive = true)]
    [Description("Sets the active project variant by name. Optionally rebuilds the solution immediately after switching. " +
                 "Returns a status message (and build log when rebuildAfter=true) or [Error] string. " +
                 "The name is checked against the project's variants/groups first (unknown -> [Error], nothing changed); " +
                 "the switch is read back and a failed switch restores the previous variant. " +
                 "Pass rebuildAfter=false to switch without building — a rebuild will still be required for changes to take effect.")]
    public string SetActiveVariant(
        [Description("Variant name as defined in the project (e.g. SIMULATION, PRODUCTION). " +
                     "Use [GroupName] syntax to activate a variant group.")] string variantName,
        [Description("Run twincat_build_solution after switching (default true). " +
                     "Pass false to switch without building.")] bool rebuildAfter = true,
        CancellationToken cancellationToken = default)
        => bridge.Activity.Trace("twincat_set_active_variant", new { variantName, rebuildAfter }, () =>
            bridge.Dispatcher.Run(() =>
                new VariantManagementCoordinator(bridge.Dispatcher.Session)
                    .SetActiveVariant(variantName, rebuildAfter)), cancellationToken);
}
