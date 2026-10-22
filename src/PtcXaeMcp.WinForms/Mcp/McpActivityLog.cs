namespace PtcXaeMcp.WinForms.Mcp;

/// <summary>
/// Thread-safe, fixed-size ring buffer of traced MCP tool calls
/// (<see cref="McpActivityEntry"/>). The oldest entry is evicted once the
/// buffer is full. A "run" groups all calls of one test/suite execution:
/// <see cref="StartNewRun"/> clears the buffer and bumps
/// <see cref="CurrentRunId"/> so the GUI and the JSON log can roll to a
/// fresh per-run file (one JSON file per Durchlauf, not per day).
/// <para>
/// Mirrors the lazy-populate / snapshot shape of
/// <see cref="McpSessionRegistry"/>: every mutation raises
/// <see cref="Changed"/> (on any thread); the GUI marshals to the UI thread.
/// </para>
/// </summary>
public sealed class McpActivityLog
{
    public const int DefaultCapacity = 500;

    private readonly object _gate = new();
    private readonly Queue<McpActivityEntry> _entries;
    private readonly int _capacity;

    /// <summary>
    /// Identifies the current run. Changes on every <see cref="StartNewRun"/>
    /// so consumers can name a fresh per-run artefact (e.g. the JSON log file).
    /// </summary>
    public Guid CurrentRunId { get; private set; } = Guid.NewGuid();

    /// <summary>Raised after any mutation (add / start-new-run). May fire on any thread.</summary>
    public event Action? Changed;

    public McpActivityLog(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _entries = new Queue<McpActivityEntry>(capacity);
    }

    /// <summary>Appends one entry, evicting the oldest if the buffer is full.</summary>
    public void Add(McpActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            if (_entries.Count >= _capacity)
                _entries.Dequeue();
            _entries.Enqueue(entry);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Begins a new run: clears the buffer and assigns a fresh
    /// <see cref="CurrentRunId"/>. Called when a test suite starts (spec:
    /// "cleared first when the test starts") and from the GUI Clear.
    /// </summary>
    public void StartNewRun()
    {
        lock (_gate)
        {
            _entries.Clear();
            CurrentRunId = Guid.NewGuid();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Current number of buffered entries. Cheaper than <see cref="Snapshot"/>
    /// when only the count is needed (e.g. capturing a test-start boundary).
    /// </summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>Point-in-time copy of all buffered entries, oldest first.</summary>
    public IReadOnlyList<McpActivityEntry> Snapshot()
    {
        lock (_gate)
            return _entries.ToArray();
    }
}
