using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.WinForms;

/// <summary>
/// Serializes concurrent MCP tool invocations via a single SemaphoreSlim(1,1).
/// Owned by ServerHost; tools call Run() or RunAsync() directly.
/// </summary>
internal sealed class McpDispatcher : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Number of bodies currently executing under the lock (0 or 1 by
    /// construction — the lock only ever admits one). Guards
    /// <see cref="Dispose"/> (issue #154, A-12/F-30): McpBridge.Stop() used to
    /// dispose the dispatcher (and its SemaphoreSlim) immediately on
    /// twincat_shutdown/host teardown, with no regard for a tool call still
    /// executing under the lock — that call's own <c>finally</c> block then
    /// throws ObjectDisposedException out of Release(), on whatever thread was
    /// running the tool, instead of the tool simply finishing.
    /// </summary>
    private int _inFlight;
    private volatile bool _disposed;

    public ITwinCatSession Session { get; }

    public McpDispatcher(ITwinCatSession session)
    {
        Session = session;
    }

    /// <summary>
    /// Acquires the single-operation lock and runs <paramref name="body"/>.
    /// Throws <see cref="InvalidOperationException"/> immediately if another operation is in progress.
    /// Invokes <paramref name="afterSuccess"/> when body returns a non-[Error] string.
    /// </summary>
    public string Run(Func<string> body, Action? afterSuccess = null)
    {
        if (!TryEnter())
            throw Busy();
        try
        {
            string result = body();
            if (afterSuccess is not null && McpErrors.IsOk(result))
                afterSuccess();
            return result;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>
    /// Async variant for operations that await while holding the lock (e.g. OpenProject).
    /// Throws <see cref="InvalidOperationException"/> immediately if another operation is in progress.
    /// </summary>
    public async Task<string> RunAsync(Func<Task<string>> body, Action? afterSuccess = null)
    {
        if (!TryEnter())
            throw Busy();
        try
        {
            string result = await body();
            if (afterSuccess is not null && McpErrors.IsOk(result))
                afterSuccess();
            return result;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Wait(0) on the lock, treating an already-disposed dispatcher
    /// (host shutting down) the same as "busy" rather than letting
    /// ObjectDisposedException escape to the tool caller.</summary>
    private bool TryEnter()
    {
        if (_disposed) return false;
        try
        {
            if (!_lock.Wait(0)) return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        Interlocked.Increment(ref _inFlight);
        return true;
    }

    private void Exit()
    {
        Interlocked.Decrement(ref _inFlight);
        // Dispose() may have run while this body was executing (host
        // shutdown mid-tool-call) — Release() on an already-disposed
        // SemaphoreSlim throws ObjectDisposedException; there is nothing
        // meaningful left to release in that case, so swallow it instead of
        // faulting whatever thread ran the tool body.
        try { _lock.Release(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// "An operation is already in progress", plus the XAE window the running call waits for when
    /// there is one (a message box, or any window owned by the disabled XAE main window — issue
    /// #149). Detection only.
    /// </summary>
    internal static InvalidOperationException Busy(Func<string?>? findXaeMessageBox = null)
    {
        string? box;
        try { box = (findXaeMessageBox ?? Win32DialogAutomation.TryFindXaeMessageBox)(); }
        catch (Exception) { box = null; }
        return new InvalidOperationException("An operation is already in progress." + (box is null ? "" :
            $" The XAE shows a dialog the running call is waiting for — answer or close it in the XAE: '{box}'."));
    }

    /// <summary>
    /// Low-level acquire for complex async callers (e.g. apply_schema) that cannot use RunAsync.
    /// Must be paired with <see cref="Release"/> in a finally block.
    /// </summary>
    internal bool TryAcquire() => TryEnter();

    /// <summary>Releases the lock acquired via <see cref="TryAcquire"/>.</summary>
    internal void Release() => Exit();

    /// <summary>How long <see cref="Dispose"/> waits for an in-flight tool call
    /// (issue #154, A-12/F-30) to finish before disposing the lock out from
    /// under it anyway. Bounded — host shutdown must not hang forever on a
    /// stuck COM call.</summary>
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(3);

    public void Dispose()
    {
        _disposed = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref _inFlight) > 0 && sw.Elapsed < DisposeDrainTimeout)
            Thread.Sleep(20);
        _lock.Dispose();
    }
}
