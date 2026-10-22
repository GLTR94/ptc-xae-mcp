using PtcXaeMcp.WinForms.Diagnostics;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for <see cref="HostLog"/> — path resolution, day-rolling,
/// exception formatting and retention (issue #147 items 1/2/5/9). Directory
/// and clock are injected so nothing here touches real time or %LOCALAPPDATA%.
/// </summary>
public sealed class HostLogTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hostlog_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    [Fact]
    public void CurrentPath_IsDayStamped_UnderTheGivenDirectory()
    {
        var clock = new FixedClock(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc));
        var log = new HostLog(_dir, clock);

        Assert.Equal(Path.Combine(_dir, "host-20260926.log"), log.CurrentPath);
    }

    [Fact]
    public void DefaultDirectory_IsUnderLocalAppData_PtcXaeMcp()
    {
        Assert.EndsWith(Path.Combine("ptc-xae-mcp"), HostLog.DefaultDirectory);
        Assert.Contains(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            HostLog.DefaultDirectory);
    }

    [Fact]
    public void Write_CreatesTheDirectory_AndAppendsALine()
    {
        var clock = new FixedClock(new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc));
        var log = new HostLog(_dir, clock);

        log.Info("hello");

        var lines = File.ReadAllLines(log.CurrentPath);
        Assert.Single(lines);
        Assert.Contains("[INFO]", lines[0]);
        Assert.Contains("hello", lines[0]);
    }

    [Fact]
    public void Write_NeverThrows_EvenWhenDirectoryIsUnwritable()
    {
        var blocker = Path.Combine(_dir, "blocker");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocker, "x");
        var log = new HostLog(Path.Combine(blocker, "nested"), new FixedClock(DateTime.UtcNow));

        var ex = Record.Exception(() => log.Info("hello"));

        Assert.Null(ex);
    }

    [Fact]
    public void WriteException_ContainsMessageAndStackTrace_AndThreadName()
    {
        var log = new HostLog(_dir, new FixedClock(DateTime.UtcNow));
        Exception thrown;
        try { throw new InvalidOperationException("boom"); }
        catch (Exception ex) { thrown = ex; }

        log.WriteException("Some.Handler", thrown, threadName: "worker-1");

        var content = File.ReadAllText(log.CurrentPath);
        Assert.Contains("Some.Handler", content);
        Assert.Contains("worker-1", content);
        Assert.Contains("boom", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("at ", content); // stack trace present
    }

    [Fact]
    public void WriteException_DoesNotDuplicateTheContextPrefix()
    {
        // The context string must appear exactly once — once as the header,
        // never repeated inside the formatted exception body too.
        var log = new HostLog(_dir, new FixedClock(DateTime.UtcNow));
        var ex = new InvalidOperationException("boom");

        log.WriteException("AppDomain.CurrentDomain.UnhandledException", ex, "main");

        var content = File.ReadAllText(log.CurrentPath);
        int occurrences = CountOccurrences(content, "AppDomain.CurrentDomain.UnhandledException");
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void WriteException_CalledTwiceForTheSameException_WritesTwoIndependentEntries_NeitherDuplicated()
    {
        // Simulates AppDomain.UnhandledException + Application.ThreadException
        // both firing for related failures — each entry's own header must not
        // repeat itself, even though the log as a whole now has two lines.
        var log = new HostLog(_dir, new FixedClock(DateTime.UtcNow));
        var ex = new InvalidOperationException("boom");

        log.WriteException("AppDomain.CurrentDomain.UnhandledException", ex, "main");
        log.WriteException("Application.ThreadException", ex, "main");

        // Each formatted exception's stack trace spans several physical lines, so
        // compare on the whole file content, not File.ReadAllLines() — the point
        // is that each context header appears exactly once, not once per line.
        var content = File.ReadAllText(log.CurrentPath);
        Assert.Equal(1, CountOccurrences(content, "AppDomain.CurrentDomain.UnhandledException"));
        Assert.Equal(1, CountOccurrences(content, "Application.ThreadException"));
    }

    [Fact]
    public void WriteException_NeverThrows_EvenForNullThreadNameFallback()
    {
        var log = new HostLog(_dir, new FixedClock(DateTime.UtcNow));
        var ex = Record.Exception(() =>
            log.WriteException("ctx", new Exception("x"), threadName: null));

        Assert.Null(ex);
    }

    [Fact]
    public void Write_PrunesFilesOlderThanKeepDays()
    {
        Directory.CreateDirectory(_dir);
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        // A stale file dated 40 days ago (default retention is 30 days).
        var stale = Path.Combine(_dir, "host-20260817.log");
        File.WriteAllText(stale, "old");

        var log = new HostLog(_dir, new FixedClock(now), keepDays: 30);
        log.Info("fresh");

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(log.CurrentPath));
    }

    [Fact]
    public void Write_KeepsFilesWithinKeepDays()
    {
        Directory.CreateDirectory(_dir);
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var recent = Path.Combine(_dir, "host-20260925.log");
        File.WriteAllText(recent, "yesterday");

        var log = new HostLog(_dir, new FixedClock(now), keepDays: 30);
        log.Info("fresh");

        Assert.True(File.Exists(recent));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
