using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for the connected-client registry. No SDK, no XAE.
/// The registry is populated lazily per MCP SessionId (no connect/disconnect
/// event in ModelContextProtocol 1.3.0) and swept by a last-seen TTL.
/// </summary>
public class McpSessionRegistryTests
{
    private static McpClientSession Sample(
        string sessionId, string name = "claude-code", string version = "1.0.0") =>
        new(sessionId, name, version, "2025-06-18",
            ConnectedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public void EmptyRegistry_HasNoSessions()
    {
        var reg = new McpSessionRegistry();

        Assert.Equal(0, reg.Count);
        Assert.Empty(reg.Snapshot());
        Assert.Null(reg.MostRecent());
    }

    [Fact]
    public void Touch_AddsSession_AndIsLookedUpById()
    {
        var reg = new McpSessionRegistry();

        reg.Touch(Sample("s1", "cursor", "0.42"));

        Assert.Equal(1, reg.Count);
        var s = Assert.Single(reg.Snapshot());
        Assert.Equal("s1", s.SessionId);
        Assert.Equal("cursor", s.ClientName);
        Assert.Equal("0.42", s.ClientVersion);
    }

    [Fact]
    public void Touch_SameSessionId_UpdatesInPlace_NoDuplicate()
    {
        var reg = new McpSessionRegistry();
        reg.Touch(Sample("s1", "claude-code", "1.0.0"));

        reg.Touch(Sample("s1", "claude-code", "1.1.0"));

        Assert.Equal(1, reg.Count);
        Assert.Equal("1.1.0", Assert.Single(reg.Snapshot()).ClientVersion);
    }

    [Fact]
    public void Touch_RefreshesLastSeen_OnRepeatCall()
    {
        var reg = new McpSessionRegistry();
        var t0 = DateTimeOffset.UnixEpoch;
        var t1 = t0.AddMinutes(5);

        reg.Touch(Sample("s1") with { }, t0);
        reg.Touch(Sample("s1") with { }, t1);

        Assert.Equal(t1, Assert.Single(reg.Snapshot()).LastSeen);
    }

    [Fact]
    public void Remove_DropsSession()
    {
        var reg = new McpSessionRegistry();
        reg.Touch(Sample("s1"));
        reg.Touch(Sample("s2"));

        Assert.True(reg.Remove("s1"));
        Assert.False(reg.Remove("s1"));
        Assert.Equal(1, reg.Count);
        Assert.Equal("s2", Assert.Single(reg.Snapshot()).SessionId);
    }

    [Fact]
    public void MostRecent_ReturnsLatestConnected()
    {
        var reg = new McpSessionRegistry();
        var early = Sample("s1", "old") with { ConnectedAt = DateTimeOffset.UnixEpoch };
        var late = Sample("s2", "new") with { ConnectedAt = DateTimeOffset.UnixEpoch.AddHours(1) };

        reg.Touch(early);
        reg.Touch(late);

        Assert.Equal("s2", reg.MostRecent()!.SessionId);
        Assert.Equal("new", reg.MostRecent()!.ClientName);
    }

    [Fact]
    public void Sweep_RemovesSessionsOlderThanTtl_KeepsFresh()
    {
        var reg = new McpSessionRegistry();
        var now = DateTimeOffset.UnixEpoch.AddHours(10);

        reg.Touch(Sample("stale"), now - TimeSpan.FromMinutes(30));
        reg.Touch(Sample("fresh"), now - TimeSpan.FromMinutes(1));

        var removed = reg.Sweep(ttl: TimeSpan.FromMinutes(10), now: now);

        Assert.Equal(1, removed);
        Assert.Equal("fresh", Assert.Single(reg.Snapshot()).SessionId);
    }

    [Fact]
    public async Task ConcurrentTouchAndRemove_DoesNotThrow_AndStaysConsistent()
    {
        var reg = new McpSessionRegistry();

        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            var id = "s" + (i % 20);
            reg.Touch(Sample(id));
            if (i % 3 == 0) reg.Remove(id);
            _ = reg.Snapshot();
            _ = reg.MostRecent();
        }));

        await Task.WhenAll(tasks);

        // Surviving set must be a subset of the 20 ids, with no duplicates.
        var ids = reg.Snapshot().Select(s => s.SessionId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(reg.Count <= 20);
    }

    [Fact]
    public void ChangedEvent_FiresOnTouchRemoveAndSweep()
    {
        var reg = new McpSessionRegistry();
        int fired = 0;
        reg.Changed += () => Interlocked.Increment(ref fired);

        reg.Touch(Sample("s1"));
        reg.Touch(Sample("s1"));               // update still notifies
        reg.Remove("s1");
        reg.Touch(Sample("s2", "x", "1"));
        reg.Sweep(TimeSpan.Zero, DateTimeOffset.UnixEpoch.AddYears(1));

        Assert.True(fired >= 4, $"expected >=4 change notifications, got {fired}");
    }
}
