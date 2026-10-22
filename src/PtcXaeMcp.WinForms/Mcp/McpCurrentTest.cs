namespace PtcXaeMcp.WinForms.Mcp;

/// <summary>One integration test as the host currently sees it: its method
/// name, its <c>[Trait("Description")]</c> narration, and the 1-based running
/// index (i in "i/N").</summary>
public sealed record RunningTest(string Name, string? Description, int Index);

/// <summary>
/// A test-start boundary in the activity timeline. <see cref="AfterLogCount"/>
/// is the number of XAE-operation entries that had already been recorded when
/// this test started, so the GUI can interleave a separator row at the right
/// position (the separator renders just before XAE operation
/// <see cref="AfterLogCount"/>).
/// </summary>
public sealed record TestBoundary(int AfterLogCount, string Name, string? Description, int Index);

/// <summary>
/// Live "which integration test is running" host state. Updated by the
/// test-only <c>ptc_test_marker</c> path and consumed by the MCP Activity tab
/// to show "▶ Running (i/N): test — description" and per-test separator rows.
/// <para>
/// A test marker is observability only: it drives this state and raises
/// <see cref="Changed"/> but is deliberately NOT recorded in
/// <see cref="McpActivityLog"/> — it is not an "XAE operation" and must never
/// be counted as a tool call. Lives alongside <see cref="McpActivity.Log"/> /
/// <see cref="McpActivity.JsonLog"/> / <see cref="McpActivity.Gate"/>.
/// </para>
/// </summary>
public sealed class McpCurrentTest
{
    private readonly object _gate = new();
    private readonly List<TestBoundary> _boundaries = [];
    private int _index;

    /// <summary>The most recently started test, retained after it ends so the
    /// banner does not flicker in the gap before the next test starts. Null
    /// only before the first test of a run and after a reset.</summary>
    public RunningTest? Current { get; private set; }

    /// <summary>True between a test's start and end markers, false otherwise.</summary>
    public bool Running { get; private set; }

    /// <summary>Raised after any change (start / end / reset). May fire on any
    /// thread; the GUI marshals to the UI thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Records that a test started. Increments the running index, becomes the
    /// <see cref="Current"/> test, and captures a <see cref="TestBoundary"/> at
    /// <paramref name="logCountAtStart"/> (the XAE-operation count at this
    /// instant) so a separator row can be placed correctly.
    /// </summary>
    public void Start(string name, string? description, int logCountAtStart)
    {
        lock (_gate)
        {
            _index++;
            Current = new RunningTest(name, description, _index);
            Running = true;
            _boundaries.Add(new TestBoundary(logCountAtStart, name, description, _index));
        }
        Raise();
    }

    /// <summary>Records that the current test ended. Clears <see cref="Running"/>
    /// but keeps <see cref="Current"/> for display continuity.</summary>
    public void End(string name)
    {
        lock (_gate)
            Running = false;
        Raise();
    }

    /// <summary>Resets to the pre-run state: no current test, index back to 0,
    /// no boundaries. Called from <see cref="McpActivity.StartNewRun"/>.</summary>
    public void ResetForNewRun()
    {
        lock (_gate)
        {
            _index = 0;
            Current = null;
            Running = false;
            _boundaries.Clear();
        }
        Raise();
    }

    /// <summary>Point-in-time copy of all recorded test boundaries, in start order.</summary>
    public IReadOnlyList<TestBoundary> Boundaries()
    {
        lock (_gate)
            return _boundaries.ToArray();
    }

    private void Raise()
    {
        // Observability only — a notification failure must never affect a run.
        try { Changed?.Invoke(); } catch { /* best effort */ }
    }
}
