using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// The decisions of the ADS write/batch paths (issue #152 D-2/D-3), kept outside the
/// <c>#if TWINCAT</c> region and free of ADS types so they are unit-testable without a runtime.
/// </summary>
internal static class AdsReadBack
{
    /// <summary>
    /// D-3: a primitive write is confirmed only when a read of the same symbol returns the written
    /// value. Null when equal; otherwise the "wrote …, read back …" text for the [Error].
    /// </summary>
    public static string? PrimitiveMismatch(object written, object? readBack)
    {
        // Same .NET type both ways (ReadAny with the type WriteAny used); float/double NaN equal
        // themselves under Equals, which is what "the bits landed" means here.
        if (Equals(written, readBack)) return null;
        return $"wrote {Format(written)}, read back {Format(readBack)}";
    }

    private static string Format(object? value) => value switch
    {
        null => "<nothing>",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// D-2: runs <paramref name="readOne"/> for every symbol path, isolating failures per item —
    /// a symbol whose handle cannot be created (or whose read throws) becomes an [Error] item
    /// instead of ending the whole batch. Cancellation still propagates.
    /// </summary>
    public static List<AdsBatchReadItem> CollectBatch(IEnumerable<string?> symbolPaths, Func<string, AdsBatchReadItem> readOne)
    {
        var items = new List<AdsBatchReadItem>();
        foreach (string? rawPath in symbolPaths)
        {
            string symbolPath = (rawPath ?? string.Empty).Trim();
            if (symbolPath.Length == 0)
            {
                items.Add(new(rawPath ?? string.Empty, null, null, false, "[Error] empty symbol path."));
                continue;
            }
            try { items.Add(readOne(symbolPath)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                items.Add(new(symbolPath, null, null, false, $"[Error] read failed: {ex.Message}"));
            }
        }
        return items;
    }
}
