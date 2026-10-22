namespace PtcXaeMcp.WinForms.TwinCat;

// ---- variant-management ------------------------------------------------------

internal sealed record VariantStatusResult(
    string ActiveVariant,
    IReadOnlyList<string> AvailableVariants,
    IReadOnlyList<VariantGroupEntry> Groups,
    string? RawConfigXml = null);

internal sealed record VariantGroupEntry(string Name, IReadOnlyList<string> Members);
