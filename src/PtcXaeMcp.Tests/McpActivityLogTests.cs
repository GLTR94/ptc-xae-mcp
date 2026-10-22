using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for <see cref="McpActivityLog"/> — the thread-safe,
/// fixed-size ring buffer of traced tool calls. No SDK, no XAE, no GUI.
/// </summary>
public class McpActivityLogTests
{
    private static McpActivityEntry E(string tool) =>
        McpActivityEntry.Ok(tool, "{}", "ok", DateTimeOffset.UnixEpoch, TimeSpan.Zero);

    [Fact]
    public void Add_ThenSnapshot_PreservesInsertionOrder_OldestFirst()
    {
        var log = new McpActivityLog(capacity: 10);

        log.Add(E("a"));
        log.Add(E("b"));
        log.Add(E("c"));

        var snap = log.Snapshot();
        Assert.Equal(["a", "b", "c"], snap.Select(e => e.ToolName));
    }

    [Fact]
    public void Overflow_EvictsOldest_KeepsMostRecentCapacityEntries()
    {
        var log = new McpActivityLog(capacity: 3);

        for (int i = 0; i < 5; i++)
            log.Add(E(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        var snap = log.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal(["2", "3", "4"], snap.Select(e => e.ToolName));
    }

    [Fact]
    public void DefaultCapacity_Is500()
    {
        var log = new McpActivityLog();

        for (int i = 0; i < 600; i++)
            log.Add(E(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(500, log.Snapshot().Count);
    }

    [Fact]
    public void Snapshot_IsImmuneToLaterMutation()
    {
        var log = new McpActivityLog(capacity: 10);
        log.Add(E("a"));

        var snap = log.Snapshot();
        log.Add(E("b"));

        Assert.Single(snap);
        Assert.Equal("a", snap[0].ToolName);
    }

    [Fact]
    public void StartNewRun_ClearsBuffer_AndBumpsRunId()
    {
        var log = new McpActivityLog(capacity: 10);
        log.Add(E("a"));
        var firstRun = log.CurrentRunId;

        log.StartNewRun();

        Assert.Empty(log.Snapshot());
        Assert.NotEqual(firstRun, log.CurrentRunId);
    }

    [Fact]
    public void Changed_FiresOnAdd_AndOnStartNewRun()
    {
        var log = new McpActivityLog(capacity: 10);
        int fires = 0;
        log.Changed += () => Interlocked.Increment(ref fires);

        log.Add(E("a"));
        log.StartNewRun();

        Assert.Equal(2, fires);
    }

    [Fact]
    public void Add_IsThreadSafe_UnderParallelWriters()
    {
        var log = new McpActivityLog(capacity: 10_000);

        Parallel.For(0, 5_000, i => log.Add(E(i.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Equal(5_000, log.Snapshot().Count);
    }

    [Fact]
    public void InvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new McpActivityLog(capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new McpActivityLog(capacity: -1));
    }
}
