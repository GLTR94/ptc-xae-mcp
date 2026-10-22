using System.Text.Json;
using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for <see cref="McpActivityJsonLog"/> — the best-effort
/// per-run JSON-lines sink. No SDK, no XAE, no GUI.
/// </summary>
public sealed class McpActivityJsonLogTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "mcpactlog_" + Guid.NewGuid().ToString("N"));

    public McpActivityJsonLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static McpActivityEntry E(string tool, bool ok = true) => ok
        ? McpActivityEntry.Ok(tool, "{\"a\":1}", "done", DateTimeOffset.UnixEpoch, TimeSpan.FromMilliseconds(5))
        : McpActivityEntry.Failure(tool, "{}", "boom", DateTimeOffset.UnixEpoch, TimeSpan.FromMilliseconds(7));

    [Fact]
    public void StartNewRun_CreatesAFreshTimestampedFile_PerRun()
    {
        var log = new McpActivityJsonLog(_dir);

        log.StartNewRun(DateTimeOffset.UnixEpoch);
        var first = log.CurrentPath;
        log.StartNewRun(DateTimeOffset.UnixEpoch.AddSeconds(1));
        var second = log.CurrentPath;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.EndsWith(".log", second);
    }

    [Fact]
    public void Append_WritesOneJsonLinePerEntry_RoundTrips()
    {
        var log = new McpActivityJsonLog(_dir);
        log.StartNewRun(DateTimeOffset.UnixEpoch);

        log.Append(E("twincat_get_status"));
        log.Append(E("twincat_open_project", ok: false));

        var lines = File.ReadAllLines(log.CurrentPath!);
        Assert.Equal(2, lines.Length);

        var first = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        Assert.Equal("twincat_get_status", first.GetProperty("tool").GetString());
        Assert.True(first.GetProperty("ok").GetBoolean());

        var second = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        Assert.False(second.GetProperty("ok").GetBoolean());
        Assert.Equal("boom", second.GetProperty("response").GetString());
    }

    [Fact]
    public void Append_WithMirrorDirectory_WritesIdenticalCopyToBoth()
    {
        var mirror = Path.Combine(Path.GetTempPath(), "mcpactlog_mir_" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new McpActivityJsonLog(_dir, mirror);
            log.StartNewRun(DateTimeOffset.UnixEpoch);

            log.Append(E("twincat_get_status"));
            log.Append(E("twincat_open_project", ok: false));

            Assert.NotNull(log.CurrentPath);
            Assert.NotNull(log.CurrentMirrorPath);
            // Same basename in both directories, different folders.
            Assert.Equal(Path.GetFileName(log.CurrentPath!), Path.GetFileName(log.CurrentMirrorPath!));
            Assert.Equal(mirror, Path.GetDirectoryName(log.CurrentMirrorPath!));
            // Byte-identical content in the repo mirror — ready for automated analysis.
            Assert.True(File.Exists(log.CurrentMirrorPath!));
            Assert.Equal(File.ReadAllText(log.CurrentPath!), File.ReadAllText(log.CurrentMirrorPath!));
        }
        finally
        {
            try { Directory.Delete(mirror, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void NoMirrorDirectory_LeavesMirrorPathNull()
    {
        var log = new McpActivityJsonLog(_dir);
        log.StartNewRun(DateTimeOffset.UnixEpoch);
        log.Append(E("twincat_get_status"));

        Assert.NotNull(log.CurrentPath);
        Assert.Null(log.CurrentMirrorPath);
    }

    [Fact]
    public void Append_WhenMirrorUnwritable_StillWritesPrimary_NeverThrows()
    {
        // A blocked mirror dir must not stop the canonical sink or throw.
        var blocker = Path.Combine(_dir, "mirror_blocker");
        File.WriteAllText(blocker, "x");
        var log = new McpActivityJsonLog(_dir, Path.Combine(blocker, "nested"));
        log.StartNewRun(DateTimeOffset.UnixEpoch);

        var ex = Record.Exception(() => log.Append(E("twincat_get_status")));

        Assert.Null(ex);
        Assert.True(File.Exists(log.CurrentPath!));
        Assert.Single(File.ReadAllLines(log.CurrentPath!));
    }

    [Fact]
    public void Append_BeforeStartNewRun_DoesNotThrow_AndWritesNothing()
    {
        var log = new McpActivityJsonLog(_dir);

        var ex = Record.Exception(() => log.Append(E("t")));

        Assert.Null(ex);
        Assert.Null(log.CurrentPath);
    }

    [Fact]
    public void Append_WhenFilePathIsUnwritable_IsSwallowed_NeverThrows()
    {
        // Point the sink at a path that cannot be created (a file where a
        // directory is expected) — a write failure must never reach the caller.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var log = new McpActivityJsonLog(Path.Combine(blocker, "nested"));
        log.StartNewRun(DateTimeOffset.UnixEpoch);

        var ex = Record.Exception(() => log.Append(E("t")));

        Assert.Null(ex);
    }

    [Fact]
    public void StartNewRun_WhenDirUnwritable_IsSwallowed_NeverThrows()
    {
        var blocker = Path.Combine(_dir, "blocker2");
        File.WriteAllText(blocker, "x");
        var log = new McpActivityJsonLog(Path.Combine(blocker, "nested"));

        var ex = Record.Exception(() => log.StartNewRun(DateTimeOffset.UnixEpoch));

        Assert.Null(ex);
    }
}
