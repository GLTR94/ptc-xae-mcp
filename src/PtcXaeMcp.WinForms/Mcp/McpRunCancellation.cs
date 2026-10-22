namespace PtcXaeMcp.WinForms;

/// <summary>
/// Lets <c>twincat_stop</c> cancel whatever MCP-started long-running operation
/// (currently only <c>twincat_apply_schema</c>) is in progress, without the host
/// exposing its private linked <see cref="CancellationTokenSource"/> directly.
///
/// Before issue #154 (E-1), <c>twincat_stop</c> only cancelled <c>_runCts</c> —
/// the GUI Run/Stop button's own token — so it answered "Stop signal sent."
/// while an MCP-started <c>apply_schema</c> kept running to completion
/// regardless. This registry is the fix: <c>RunApplySchemaAsync</c> tracks its
/// linked CTS here for the duration of the run, and <c>twincat_stop</c> (via
/// ServerHost.RequestStop) cancels whatever is currently
/// tracked, if anything.
///
/// COM-free and side-effect-free beyond the CTS it is handed — unit-testable
/// without a WinForms host.
/// </summary>
internal sealed class McpRunCancellation
{
    // volatile: tracked on an MCP/GUI thread, cancelled from twincat_stop on another.
    private volatile CancellationTokenSource? _current;

    /// <summary>
    /// Registers <paramref name="cts"/> as the run to cancel on
    /// <see cref="CancelCurrent"/> until the returned scope is disposed. Only
    /// one run is ever tracked at a time in practice (apply_schema serialises
    /// via McpDispatcher), but registering a second one simply supersedes the
    /// first for the CancelCurrent purpose — the caller's own `using` disposal
    /// clears its own registration on the way out, harmlessly, even if it is
    /// no longer the "current" one.
    /// </summary>
    public IDisposable Track(CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(cts);
        _current = cts;
        return new Unregister(this, cts);
    }

    /// <summary>Cancels the currently tracked run's token, if any. Returns
    /// true when there was one to cancel (already-disposed CTS counts as
    /// "nothing to do", not an error — the run already finished).</summary>
    public bool CancelCurrent()
    {
        var cts = _current;
        if (cts is null) return false;
        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>True while a run is currently tracked. Diagnostic/test use.</summary>
    public bool HasCurrent => _current is not null;

    private sealed class Unregister(McpRunCancellation owner, CancellationTokenSource cts) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(owner._current, cts))
                owner._current = null;
        }
    }
}
