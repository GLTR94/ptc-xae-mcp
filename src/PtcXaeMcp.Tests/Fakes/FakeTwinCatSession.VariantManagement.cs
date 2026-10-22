using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- variant-management ----
    public VariantStatusResult FakeVariantStatus { get; set; } =
        new(string.Empty, [], []);
    public Exception? GetVariantStatusThrows { get; set; }
    public Exception? SetActiveVariantThrows { get; set; }
    public string FakeSetActiveVariantResult { get; set; } = "Variant set.";

    public VariantStatusResult GetVariantStatus()
    {
        CallLog.Add("GetVariantStatus()");
        if (GetVariantStatusThrows is not null) throw GetVariantStatusThrows;
        return FakeVariantStatus;
    }

    /// <summary>
    /// What CurrentProjectVariant reads after a set. Null (default) = the requested name
    /// (a working setter); set it to simulate a setter that does not take effect / clears it.
    /// </summary>
    public string? VariantAfterSet { get; set; }
    /// <summary>Applied even when <see cref="SetActiveVariantThrows"/> is set (LIVE-10: a failing set cleared the variant).</summary>
    public string? VariantAfterFailedSet { get; set; }

    public string SetActiveVariant(string variantName)
    {
        CallLog.Add($"SetActiveVariant({variantName})");
        if (SetActiveVariantThrows is not null)
        {
            if (VariantAfterFailedSet is not null)
            {
                FakeVariantStatus = FakeVariantStatus with { ActiveVariant = VariantAfterFailedSet };
                VariantAfterFailedSet = null; // only the first (failing) call corrupts the state
            }
            var ex = SetActiveVariantThrows;
            SetActiveVariantThrows = null; // the restore call succeeds
            throw ex;
        }
        FakeVariantStatus = FakeVariantStatus with { ActiveVariant = VariantAfterSet ?? variantName };
        VariantAfterSet = null;
        return FakeSetActiveVariantResult;
    }
}
