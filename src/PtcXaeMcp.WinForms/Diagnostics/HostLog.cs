using System.Text;

namespace PtcXaeMcp.WinForms.Diagnostics;

/// <summary>Injectable clock so <see cref="HostLog"/> is unit-testable without waiting on real time.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Default clock backed by <see cref="DateTime.UtcNow"/>.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Thread-safe, best-effort rolling host log — <c>host-&lt;yyyyMMdd&gt;.log</c>
/// under <see cref="DefaultDirectory"/> (<c>%LOCALAPPDATA%\ptc-xae-mcp</c>),
/// one file per UTC day. Exists so a server crash (issue #147) is diagnosable
/// even when nothing else survives — every write here swallows its own
/// exceptions and NEVER throws into the caller.
/// </summary>
public sealed class HostLog
{
    /// <summary>Default sink dir: <c>%LOCALAPPDATA%\ptc-xae-mcp</c> — same root as the activity log.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ptc-xae-mcp");

    /// <summary>Process-wide instance used by <c>Program.cs</c> and the rest of the host.</summary>
    public static HostLog Shared { get; } = new HostLog();

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly IClock _clock;
    private readonly int? _keepDaysOverride;

    public string Directory => _directory;

    /// <param name="directory">Sink directory. Defaults to <see cref="DefaultDirectory"/>.</param>
    /// <param name="clock">Clock used for the daily file name and retention cutoff. Defaults to <see cref="SystemClock"/>.</param>
    /// <param name="keepDays">Retention override for tests; production reads <see cref="LogRetention.KeepDaysEnvVar"/> lazily.</param>
    public HostLog(string? directory = null, IClock? clock = null, int? keepDays = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        _clock = clock ?? new SystemClock();
        _keepDaysOverride = keepDays;
    }

    /// <summary>Absolute path of today's (UTC) log file.</summary>
    public string CurrentPath => Path.Combine(_directory, $"host-{_clock.UtcNow:yyyyMMdd}.log");

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    /// <summary>Appends one timestamped, leveled line. Never throws.</summary>
    public void Write(string level, string message)
    {
        try
        {
            var line = $"{_clock.UtcNow:O} [{level}] {message}";
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.AppendAllText(CurrentPath, line + Environment.NewLine, Encoding.UTF8);
            }
            PruneOldDays();
        }
        catch
        {
            // Logging must never break a caller — see McpActivityJsonLog for the same contract.
        }
    }

    /// <summary>
    /// Logs a full exception (message, type, stack trace, inner exceptions) plus
    /// the originating thread, exactly once, under a single <paramref name="context"/>
    /// prefix — no duplicated "ERROR"/context text between the header and the
    /// formatted exception body.
    /// </summary>
    public void WriteException(string context, Exception ex, string? threadName = null)
    {
        try
        {
            var thread = string.IsNullOrWhiteSpace(threadName)
                ? (string.IsNullOrWhiteSpace(Thread.CurrentThread.Name)
                    ? $"#{Environment.CurrentManagedThreadId}"
                    : Thread.CurrentThread.Name!)
                : threadName;

            var body = $"{context} (thread={thread}){Environment.NewLine}{ex}";
            Write("ERROR", body);
        }
        catch
        {
            // Never throw from a handler that exists to survive everything else failing.
        }
    }

    /// <summary>Deletes host-*.log files older than the configured retention window.</summary>
    private void PruneOldDays()
    {
        try
        {
            if (!System.IO.Directory.Exists(_directory))
                return;

            int keepDays = _keepDaysOverride ?? LogRetention.ResolveKeepDays(Environment.GetEnvironmentVariable);
            var files = System.IO.Directory.GetFiles(_directory, "host-*.log");
            var withDates = new List<(string Path, DateTime Ts)>(files.Length);
            foreach (var f in files)
            {
                var ts = ParseDayStamp(Path.GetFileNameWithoutExtension(f)) ?? File.GetLastWriteTimeUtc(f);
                withDates.Add((f, ts));
            }

            // Day-based only — no run-count cap for the host log (issue #147 item 5),
            // so pass a keepRuns large enough to never bind.
            var toDelete = LogRetention.SelectForDeletion(
                withDates, x => x.Ts, _clock.UtcNow, keepRuns: int.MaxValue, keepDays: keepDays);

            foreach (var (path, _) in toDelete)
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
        catch
        {
            // Retention is best-effort; never let pruning break logging.
        }
    }

    private static DateTime? ParseDayStamp(string fileNameWithoutExtension)
    {
        const string prefix = "host-";
        if (!fileNameWithoutExtension.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var stamp = fileNameWithoutExtension[prefix.Length..];
        return DateTime.TryParseExact(stamp, "yyyyMMdd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt
            : null;
    }
}
