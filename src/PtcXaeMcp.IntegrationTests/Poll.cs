using System.Diagnostics;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Condition polling for live tests (issue #157 item 7): wait for an observable state instead of a
/// fixed sleep. The probe runs immediately (no initial settle), then every <c>interval</c> until the
/// condition holds or the timeout would be exceeded; the last probe result is returned so the caller
/// can assert on it with a meaningful message.
/// </summary>
internal static class Poll
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    public static async Task<T> UntilAsync<T>(
        Func<Task<T>> probe, Func<T, bool> done, TimeSpan timeout, TimeSpan? interval = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(done);
        TimeSpan gap = interval ?? DefaultInterval;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            T value = await probe();
            if (done(value) || clock.Elapsed + gap > timeout) return value;
            await Task.Delay(gap, ct);
        }
    }
}
