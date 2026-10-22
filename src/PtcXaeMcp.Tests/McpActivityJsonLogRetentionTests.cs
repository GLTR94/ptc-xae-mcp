using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Retention behaviour of <see cref="McpActivityJsonLog"/> (issue #147 item 5):
/// each <c>StartNewRun</c> prunes runs beyond the configured keep-count /
/// keep-days window, in both the primary and mirror directories.
/// </summary>
public sealed class McpActivityJsonLogRetentionTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "mcpactlog_ret_" + Guid.NewGuid().ToString("N"));

    public McpActivityJsonLogRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static McpActivityEntry E() =>
        McpActivityEntry.Ok("twincat_get_status", "{}", "done", DateTimeOffset.UnixEpoch, TimeSpan.FromMilliseconds(1));

    /// <summary>Rolls to a new run AND materializes its file on disk (StartNewRun alone
    /// only reserves the path; the file is created lazily on the first Append —
    /// same as production traffic).</summary>
    private static string StartAndWrite(McpActivityJsonLog log, DateTimeOffset at)
    {
        log.StartNewRun(at);
        log.Append(E());
        return log.CurrentPath!;
    }

    [Fact]
    public void StartNewRun_DeletesRunsBeyondKeepRuns()
    {
        var log = new McpActivityJsonLog(_dir, keepRuns: 2, keepDays: 365);
        var t0 = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        // 3 runs, one second apart each, well within the day window.
        var first = StartAndWrite(log, t0);
        var second = StartAndWrite(log, t0.AddSeconds(1));
        var third = StartAndWrite(log, t0.AddSeconds(2));

        // keepRuns=2 must have pruned the oldest (first) once the third run started.
        Assert.False(File.Exists(first), "oldest run beyond keepRuns should be pruned");
        Assert.True(File.Exists(second));
        Assert.True(File.Exists(third));
    }

    [Fact]
    public void StartNewRun_DeletesRunsBeyondKeepDays()
    {
        var log = new McpActivityJsonLog(_dir, keepRuns: 50, keepDays: 1);
        var t0 = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var old = StartAndWrite(log, t0);
        // A run 3 days later — the old one is now outside the 1-day window.
        var recent = StartAndWrite(log, t0.AddDays(3));

        Assert.False(File.Exists(old), "run older than keepDays should be pruned");
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void StartNewRun_WithinRetentionWindow_KeepsAllRuns()
    {
        var log = new McpActivityJsonLog(_dir, keepRuns: 50, keepDays: 30);
        var t0 = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var first = StartAndWrite(log, t0);
        var second = StartAndWrite(log, t0.AddSeconds(1));

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void StartNewRun_PrunesTheMirrorDirectoryToo()
    {
        var mirror = Path.Combine(Path.GetTempPath(), "mcpactlog_ret_mir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mirror);
        try
        {
            var log = new McpActivityJsonLog(_dir, mirror, keepRuns: 1, keepDays: 365);
            var t0 = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

            log.StartNewRun(t0);
            log.Append(E());
            var oldMirror = log.CurrentMirrorPath!;
            Assert.True(File.Exists(oldMirror), "sanity: the first run's mirror file must exist before pruning");

            log.StartNewRun(t0.AddSeconds(1));
            log.Append(E());

            Assert.False(File.Exists(oldMirror), "mirror copy of the pruned run should also be deleted");
        }
        finally
        {
            try { Directory.Delete(mirror, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void StartNewRun_PruneNeverThrows_WhenDirectoryDisappearsBetweenCalls()
    {
        var log = new McpActivityJsonLog(_dir, keepRuns: 1, keepDays: 1);
        log.StartNewRun(DateTimeOffset.UtcNow);
        log.Append(E());

        try { Directory.Delete(_dir, recursive: true); } catch { /* setup */ }

        var ex = Record.Exception(() => log.StartNewRun(DateTimeOffset.UtcNow.AddSeconds(1)));

        Assert.Null(ex);
    }
}
