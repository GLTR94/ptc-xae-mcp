namespace PtcXaeMcp.WinForms.Diagnostics;

/// <summary>
/// Pure retention-selection logic shared by the host log and the per-run
/// activity log (issue #147, item 5). Deliberately has no file-system
/// awareness — callers hand in the items and a timestamp selector so this
/// stays unit-testable without touching disk.
/// </summary>
public static class LogRetention
{
    public const string KeepRunsEnvVar = "PTC_XAE_MCP_LOG_KEEP_RUNS";
    public const string KeepDaysEnvVar = "PTC_XAE_MCP_LOG_KEEP_DAYS";

    public const int DefaultKeepRuns = 50;
    public const int DefaultKeepDays = 30;

    /// <summary>
    /// Reads <see cref="KeepRunsEnvVar"/> via <paramref name="getEnv"/> (e.g.
    /// <see cref="Environment.GetEnvironmentVariable(string)"/>); falls back to
    /// <see cref="DefaultKeepRuns"/> for a missing, blank, non-numeric or
    /// non-positive value.
    /// </summary>
    public static int ResolveKeepRuns(Func<string, string?> getEnv) =>
        ParsePositiveInt(getEnv(KeepRunsEnvVar), DefaultKeepRuns);

    /// <summary>Same as <see cref="ResolveKeepRuns"/> for <see cref="KeepDaysEnvVar"/>.</summary>
    public static int ResolveKeepDays(Func<string, string?> getEnv) =>
        ParsePositiveInt(getEnv(KeepDaysEnvVar), DefaultKeepDays);

    private static int ParsePositiveInt(string? raw, int fallback) =>
        !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var v) && v > 0 ? v : fallback;

    /// <summary>
    /// Selects which of <paramref name="items"/> should be DELETED so the
    /// remaining set satisfies both constraints at once — at most
    /// <paramref name="keepRuns"/> items, all newer-or-equal-to
    /// <paramref name="nowUtc"/> minus <paramref name="keepDays"/> days
    /// ("keep the newest 50 runs or 30 days, whichever is smaller").
    /// An item is kept only if it is BOTH among the <paramref name="keepRuns"/>
    /// most recent AND within the day window; everything else is returned for
    /// deletion. Ties in timestamp are broken by original order (stable sort).
    /// </summary>
    public static IReadOnlyList<T> SelectForDeletion<T>(
        IReadOnlyList<T> items,
        Func<T, DateTime> timestampOf,
        DateTime nowUtc,
        int keepRuns,
        int keepDays)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(timestampOf);

        if (items.Count == 0)
            return Array.Empty<T>();

        var cutoff = nowUtc.AddDays(-keepDays);

        // OrderByDescending is a stable sort in .NET, so equal timestamps keep
        // their original relative order.
        var newestFirst = items
            .Select((item, index) => (item, index))
            .OrderByDescending(pair => timestampOf(pair.item))
            .ToList();

        var keep = new HashSet<int>();
        for (int i = 0; i < newestFirst.Count && i < keepRuns; i++)
        {
            var (item, index) = newestFirst[i];
            if (timestampOf(item) >= cutoff)
                keep.Add(index);
        }

        var toDelete = new List<T>();
        for (int i = 0; i < items.Count; i++)
            if (!keep.Contains(i))
                toDelete.Add(items[i]);
        return toDelete;
    }
}
