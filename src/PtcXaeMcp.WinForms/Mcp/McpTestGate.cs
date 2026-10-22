using System.Collections.Concurrent;

namespace PtcXaeMcp.WinForms.Mcp;

public enum McpTestGateMode
{
    /// <summary>Default: tool calls are not gated — full speed.</summary>
    Run,

    /// <summary>
    /// Each traced tool call blocks at the gate until the operator releases
    /// it (<see cref="McpTestGate.Continue"/>), so the human can verify the
    /// state of the previous step in the XAE before the next one runs.
    /// </summary>
    SingleStep,
}

/// <summary>
/// Cooperative step / pause / stop gate for traced MCP tool calls. The
/// <c>Trace</c> wrapper awaits <see cref="WaitAsync"/> before executing each
/// tool body; the GUI drives <see cref="Mode"/>, <see cref="Continue"/> and
/// <see cref="Stop"/>. Because the live suite hits the same in-process host
/// over HTTP, gating here pauses the suite without touching the tests.
/// <para>
/// Default <see cref="McpTestGateMode.Run"/> means zero overhead and no
/// behaviour change — "Default: alles testen ohne Halt" (spec).
/// </para>
/// </summary>
public sealed class McpTestGate
{
    private readonly object _gate = new();
    // FIFO of waiters parked in SingleStep mode. Each completes its Tcs when
    // released by Continue(), a switch back to Run, or Stop() (cancelled).
    private readonly ConcurrentQueue<Waiter> _waiters = new();
    private McpTestGateMode _mode = McpTestGateMode.Run;

    /// <summary>Raised after Mode/Stop/Reset changes. May fire on any thread.</summary>
    public event Action? Changed;

    private sealed class Waiter(string toolName)
    {
        public string ToolName { get; } = toolName;
        public TaskCompletionSource Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public McpTestGateMode Mode
    {
        get { lock (_gate) return _mode; }
        set
        {
            bool changed;
            lock (_gate)
            {
                changed = _mode != value;
                _mode = value;
            }
            if (!changed)
                return;

            // Leaving SingleStep (to Run) releases everyone parked.
            if (value == McpTestGateMode.Run)
                DrainAll(cancel: false);

            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Tool name currently parked at the gate awaiting a Continue(), or null.
    /// Drives the GUI hint "verify this step in the XAE, then Continue".
    /// </summary>
    public string? PendingToolName
    {
        get
        {
            return _waiters.TryPeek(out var w) ? w.ToolName : null;
        }
    }

    /// <summary>
    /// Awaited by the Trace wrapper before each tool body. Returns immediately
    /// in <see cref="McpTestGateMode.Run"/>; in SingleStep parks until
    /// <see cref="Continue"/> releases this call. Throws
    /// <see cref="OperationCanceledException"/> if stopped or the caller's
    /// token cancels.
    /// </summary>
    public Task WaitAsync(string toolName, CancellationToken ct)
    {
        lock (_gate)
        {
            // Run is unconditional pass-through. This is the ONLY state a
            // freshly-constructed or post-Stop gate is ever in, so the test
            // gate can never silently cancel ordinary MCP traffic — the
            // failure the live suite caught when a stale gate bricked every call.
            if (_mode == McpTestGateMode.Run)
                return Task.CompletedTask;
        }

        var waiter = new Waiter(toolName);
        _waiters.Enqueue(waiter);

        if (ct.CanBeCanceled)
            ct.Register(() => waiter.Tcs.TrySetCanceled(ct));

        // Mode may have flipped to Run between the check and the enqueue.
        if (Mode == McpTestGateMode.Run)
            DrainAll(cancel: false);

        Changed?.Invoke();
        return waiter.Tcs.Task;
    }

    /// <summary>Releases exactly one parked call (the oldest).</summary>
    public void Continue()
    {
        while (_waiters.TryDequeue(out var w))
        {
            if (w.Tcs.TrySetResult())
            {
                Changed?.Invoke();
                return;
            }
            // Already cancelled — skip and release the next real waiter.
        }
    }

    /// <summary>
    /// Aborts the current single-step run: cancels every parked call, then
    /// <b>disarms back to <see cref="McpTestGateMode.Run"/></b> so the very
    /// next tool call flows normally. Stopping the suite must never brick the
    /// server — a latching "cancel everything until someone clicks Clear"
    /// silently killed an entire live-suite run. The Trace wrapper turns the
    /// cancelled parked calls into "[Error] Cancelled: ..." activity entries.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
            _mode = McpTestGateMode.Run;
        DrainAll(cancel: true);
        Changed?.Invoke();
    }

    /// <summary>
    /// Re-arms the gate for a fresh run: discards stragglers and returns to
    /// pass-through. (<see cref="Stop"/> already disarms; this also covers a
    /// new run started while still parked in single-step.)
    /// </summary>
    public void ResetForNewRun()
    {
        lock (_gate)
            _mode = McpTestGateMode.Run;
        DrainAll(cancel: true); // discard any stragglers
        Changed?.Invoke();
    }

    private void DrainAll(bool cancel)
    {
        while (_waiters.TryDequeue(out var w))
        {
            if (cancel)
                w.Tcs.TrySetCanceled();
            else
                w.Tcs.TrySetResult();
        }
    }
}
