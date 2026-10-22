namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Legacy entry point kept for its ~130 call sites; delegates to <see cref="ComCall.Run{T}"/>
/// (Stopwatch deadline, binder-exception busy detection, early exit with the blocking XAE
/// dialog named). Exhaustion throws <see cref="XaeBlockedException"/>, whose HResult is the
/// last busy code.
/// </summary>
internal static class ComBusyRetry
{
    public static T Run<T>(Func<T> action, TimeSpan timeout, TimeSpan? delay = null) =>
        ComCall.Run(action, timeout, delay);

    // Test-only seam (issue #157 item 7): lets a test drive the deadline with a
    // fake clock instead of real wall-clock time, so "exhausts timeout" tests
    // aren't flaky under CI/JIT jitter. Never called from production code.
    internal static T Run<T>(Func<T> action, TimeSpan timeout, TimeSpan? delay, Func<TimeSpan> elapsedOverride) =>
        ComCall.Run(action, timeout, delay, findBlockingDialog: null, operation: null, elapsedOverride);

    public static void Run(Action action, TimeSpan timeout, TimeSpan? delay = null) =>
        ComCall.Run(action, timeout, delay);

    internal static bool IsBusy(int hResult) => ComCall.IsBusyHResult(hResult);
}
