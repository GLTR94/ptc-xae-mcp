using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Host-state tests for <see cref="McpCurrentTest"/> — the live "which test is
/// running" surface that drives the MCP Activity banner + per-test separator
/// rows. A test marker is observability only: it must update this state and
/// raise <see cref="McpCurrentTest.Changed"/>, but it must never appear as a
/// counted XAE operation in <see cref="McpActivityLog"/>.
/// </summary>
public sealed class McpCurrentTestTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "mcpcur_" + Guid.NewGuid().ToString("N"));

    public McpCurrentTestTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private McpActivity NewActivity() =>
        new(new McpActivityLog(capacity: 50), new McpActivityJsonLog(_dir));

    [Fact]
    public void Start_SetsCurrentRunningTest_IncrementsIndex_RaisesChanged()
    {
        var ct = new McpCurrentTest();
        int changed = 0;
        ct.Changed += () => changed++;

        ct.Start("Read_BOOL_GvlSafety", "BOOL über ADS lesen", logCountAtStart: 0);

        Assert.NotNull(ct.Current);
        Assert.Equal("Read_BOOL_GvlSafety", ct.Current!.Name);
        Assert.Equal("BOOL über ADS lesen", ct.Current.Description);
        Assert.Equal(1, ct.Current.Index);
        Assert.True(ct.Running);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Start_Twice_IndexCounts_Up()
    {
        var ct = new McpCurrentTest();

        ct.Start("First", "d1", 0);
        ct.End("First");
        ct.Start("Second", "d2", 3);

        Assert.Equal(2, ct.Current!.Index);
        Assert.Equal("Second", ct.Current.Name);
        Assert.True(ct.Running);
    }

    [Fact]
    public void End_ClosesRun_KeepsCurrentForDisplay_RaisesChanged()
    {
        var ct = new McpCurrentTest();
        ct.Start("OnlyTest", "desc", 0);
        int changed = 0;
        ct.Changed += () => changed++;

        ct.End("OnlyTest");

        Assert.False(ct.Running);
        // Current is retained so the banner does not flicker between tests.
        Assert.NotNull(ct.Current);
        Assert.Equal("OnlyTest", ct.Current!.Name);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Start_RecordsBoundaryAtCapturedLogPosition()
    {
        var ct = new McpCurrentTest();

        ct.Start("A", "da", logCountAtStart: 0);
        ct.End("A");
        ct.Start("B", "db", logCountAtStart: 2);
        ct.End("B");

        var boundaries = ct.Boundaries();
        Assert.Equal(2, boundaries.Count);
        Assert.Equal(0, boundaries[0].AfterLogCount);
        Assert.Equal("A", boundaries[0].Name);
        Assert.Equal(1, boundaries[0].Index);
        Assert.Equal(2, boundaries[1].AfterLogCount);
        Assert.Equal("B", boundaries[1].Name);
        Assert.Equal(2, boundaries[1].Index);
    }

    [Fact]
    public void ResetForNewRun_ClearsCurrentIndexAndBoundaries()
    {
        var ct = new McpCurrentTest();
        ct.Start("A", "da", 0);
        ct.End("A");

        ct.ResetForNewRun();

        Assert.Null(ct.Current);
        Assert.False(ct.Running);
        Assert.Empty(ct.Boundaries());

        // The index restarts at 1 after a reset.
        ct.Start("Fresh", "df", 0);
        Assert.Equal(1, ct.Current!.Index);
    }

    [Fact]
    public void Marker_IsNotCountedAsAnXaeOperation()
    {
        // The whole point: starting/ending a test must not pollute the activity
        // buffer with a normal call row. Driving CurrentTest leaves the Log empty.
        var act = NewActivity();

        act.CurrentTest.Start("SomeTest", "narration", act.Log.Count);
        act.CurrentTest.End("SomeTest");

        Assert.Equal(0, act.Log.Count);
        Assert.Empty(act.Log.Snapshot());
    }

    [Fact]
    public void StartNewRun_AlsoResetsCurrentTest()
    {
        var act = NewActivity();
        act.CurrentTest.Start("Old", "d", 0);

        act.StartNewRun();

        Assert.Null(act.CurrentTest.Current);
        Assert.False(act.CurrentTest.Running);
        Assert.Empty(act.CurrentTest.Boundaries());
    }

    [Fact]
    public void Boundary_CapturesLogPositionAfterRealEntries()
    {
        // A test that issues two XAE operations, then the next test starts:
        // its boundary must sit after those two entries so the GUI renders the
        // separator beneath them.
        var act = NewActivity();
        act.CurrentTest.Start("First", "d1", act.Log.Count);   // boundary @ 0
        act.Trace("twincat_get_status", null, () => "ok");
        act.Trace("twincat_get_status", null, () => "ok");
        act.CurrentTest.End("First");
        act.CurrentTest.Start("Second", "d2", act.Log.Count);  // boundary @ 2

        var b = act.CurrentTest.Boundaries();
        Assert.Equal(0, b[0].AfterLogCount);
        Assert.Equal(2, b[1].AfterLogCount);
    }
}
