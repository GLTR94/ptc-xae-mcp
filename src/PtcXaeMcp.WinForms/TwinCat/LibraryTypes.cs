namespace PtcXaeMcp.WinForms.TwinCat;

internal enum LibrarySource { Xae, Plcproj }

internal sealed record LibraryReference(
    string Name,
    string Version,
    string Publisher,
    string Placeholder,
    string EffectiveResolution,
    LibrarySource Source,
    string? FilePinnedVersion,
    bool? VersionMatchesFile)
{
    // XAE reports an unpinned reference resolved to the newest installed version as e.g.
    // "Tc2_MC2, * (newest) (Beckhoff Automation GmbH)" — Version then carries "* (newest)",
    // not a bare "*". Treat any leading "*" as "any version" so CheckAvailability does not
    // misclassify it as a VersionMismatch (issue #152 comment, PR #167 live finding).
    // TC 4026.22 also reports it as a bare "newest" (e.g. "Tc2_MC2 newest (Beckhoff …)").
    public bool IsAnyVersion => Version == "*" || Version.StartsWith('*')
        || Version.Equals("newest", StringComparison.OrdinalIgnoreCase);
}

internal sealed record InstalledLibrary(
    string Name,
    string Version,
    string Publisher,
    string Title);

// One parameter-list override of a placeholder reference, persisted in the .plcproj as
// <PlaceholderReference><Parameters><Parameter ListName="..."><Key/><Value/></Parameter>...
internal sealed record LibraryParameter(
    string Placeholder,
    string ListName,
    string Key,
    string Value);

internal enum AvailabilityStatus { Installed, Missing, VersionMismatch }

internal sealed record LibraryAvailability(
    LibraryReference Reference,
    AvailabilityStatus Status,
    InstalledLibrary? Match);
