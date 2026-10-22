namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// One HMI write approval as the Approvals view lists it (issue #272): what was asked and how
/// it ended. <see cref="Outcome"/> is null while the popup is still open (pending).
/// </summary>
internal sealed record HmiApprovalRecord(
    long Id,
    DateTime RequestedUtc,
    HmiWriteKind Kind,
    string HmiProject,
    string Symbol,
    string? NewValue,
    bool WritesToPlc,
    HmiConfirmOutcome? Outcome,
    DateTime? AnsweredUtc)
{
    public bool IsPending => Outcome is null;
}

/// <summary>
/// Thread-safe, bounded record of HMI write approvals, owned by <see cref="ServerHost"/>.
/// The MCP thread adds entries (through <see cref="RecordingHmiConfirmer"/>), the GUI
/// subscribes to <see cref="Changed"/> and reads <see cref="Snapshot"/>. Display only: nothing
/// here decides whether a write happens.
/// </summary>
internal sealed class HmiApprovalHistory(int capacity = HmiApprovalHistory.DefaultCapacity, Func<DateTime>? clock = null)
{
    public const int DefaultCapacity = 200;

    private readonly object _gate = new();
    private readonly List<HmiApprovalRecord> _records = [];
    private readonly int _capacity = capacity > 0 ? capacity : DefaultCapacity;
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private long _nextId;

    /// <summary>Raised (on the recording thread, outside the lock) after an entry was added
    /// or answered, with that entry. A throwing subscriber is ignored.</summary>
    public event Action<HmiApprovalRecord>? Changed;

    public int Capacity => _capacity;

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<HmiApprovalRecord> Snapshot()
    {
        lock (_gate) return _records.ToArray();
    }

    public int PendingCount
    {
        get { lock (_gate) return _records.Count(r => r.IsPending); }
    }

    /// <summary>Adds a pending entry for a popup that is about to open; returns its id.</summary>
    public long Begin(HmiWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        HmiApprovalRecord added;
        lock (_gate)
        {
            added = new HmiApprovalRecord(++_nextId, _clock(), request.Kind, request.HmiProject, request.Symbol,
                request.NewValue, request.WritesToPlc, Outcome: null, AnsweredUtc: null);
            _records.Add(added);
            Trim();
        }
        Raise(added);
        return added.Id;
    }

    /// <summary>Sets the outcome of the entry <paramref name="id"/>. An entry that was already
    /// trimmed away (more than <see cref="Capacity"/> newer ones) is re-added so no answer is lost.</summary>
    public void Complete(long id, HmiWriteRequest request, HmiConfirmOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(request);
        HmiApprovalRecord answered;
        lock (_gate)
        {
            int index = _records.FindIndex(r => r.Id == id);
            if (index >= 0)
            {
                answered = _records[index] with { Outcome = outcome, AnsweredUtc = _clock() };
                _records[index] = answered;
            }
            else
            {
                DateTime now = _clock();
                answered = new HmiApprovalRecord(id, now, request.Kind, request.HmiProject, request.Symbol,
                    request.NewValue, request.WritesToPlc, outcome, now);
                _records.Add(answered);
                Trim();
            }
        }
        Raise(answered);
    }

    /// <summary>Each subscriber is isolated: one that throws neither stops the others nor
    /// reaches the recording (MCP) thread.</summary>
    private void Raise(HmiApprovalRecord record)
    {
        if (Changed is not { } handlers) return;
        foreach (Action<HmiApprovalRecord> handler in handlers.GetInvocationList().Cast<Action<HmiApprovalRecord>>())
        {
            try { handler(record); }
            catch { /* display only — see the class summary */ }
        }
    }

    /// <summary>Drops the oldest entries beyond the capacity. Caller holds the lock.</summary>
    private void Trim()
    {
        if (_records.Count > _capacity)
            _records.RemoveRange(0, _records.Count - _capacity);
    }
}

/// <summary>
/// Decorator that records every <see cref="IHmiWriteConfirmer.Confirm"/> in a
/// <see cref="HmiApprovalHistory"/> (issue #272). It never changes the answer: the inner
/// confirmer's outcome is returned unchanged, an exception from it propagates unchanged
/// (recorded as <see cref="HmiConfirmOutcome.Unavailable"/>), and a failure while recording
/// is swallowed — the history is a display, the fail-closed decision stays with the caller.
/// </summary>
internal sealed class RecordingHmiConfirmer(IHmiWriteConfirmer inner, HmiApprovalHistory history) : IHmiWriteConfirmer
{
    public HmiConfirmOutcome Confirm(HmiWriteRequest request, TimeSpan timeout)
    {
        long? id = TryRecord(() => history.Begin(request));
        HmiConfirmOutcome outcome;
        try
        {
            outcome = inner.Confirm(request, timeout);
        }
        catch
        {
            if (id is { } failed) TryRecord(() => { history.Complete(failed, request, HmiConfirmOutcome.Unavailable); return 0L; });
            throw;
        }
        if (id is { } done) TryRecord(() => { history.Complete(done, request, outcome); return 0L; });
        return outcome;
    }

    private static long? TryRecord(Func<long> record)
    {
        try { return record(); }
        catch { return null; } // a broken subscriber must never decide an HMI write
    }
}
