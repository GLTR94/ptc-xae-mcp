using System.Text;
using System.Text.Json;
using PtcXaeMcp.WinForms.Diagnostics;
using System.Globalization;

namespace PtcXaeMcp.WinForms.Mcp;

/// <summary>
/// Best-effort JSON-lines sink for traced tool calls. <b>One file per run</b>
/// (per Durchlauf, not per day): <see cref="StartNewRun"/> opens a fresh
/// timestamped file under the log directory; each <see cref="Append"/> writes
/// one compact JSON object on its own line.
/// <para>
/// <b>Robustness contract:</b> a logging failure must NEVER affect a tool
/// call (spec: "a write failure must never affect a tool call").
/// Every I/O path here swallows exceptions. The trace
/// wrapper also stays off the critical path of <c>twincat_shutdown</c>.
/// </para>
/// </summary>
public sealed class McpActivityJsonLog
{
    /// <summary>Default sink dir: <c>%LOCALAPPDATA%\ptc-xae-mcp</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ptc-xae-mcp");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string? _mirrorDirectory;
    private readonly int? _keepRunsOverride;
    private readonly int? _keepDaysOverride;

    /// <summary>Absolute path of the current run's file, or null before the first run.</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>
    /// Absolute path of the current run's mirror copy (same basename, written to
    /// <paramref name="mirrorDirectory"/> for automated analysis from the repo),
    /// or null when no mirror directory was configured / before the first run.
    /// </summary>
    public string? CurrentMirrorPath { get; private set; }

    /// <param name="directory">Primary (canonical) sink directory. Defaults to
    /// <see cref="DefaultDirectory"/>.</param>
    /// <param name="mirrorDirectory">Optional second directory that receives an
    /// identical copy of every line (e.g. the repo's <c>reports/</c> folder so
    /// tooling can read the run from the repo). Best-effort, like the primary.</param>
    /// <param name="keepRuns">Retention override for tests; production reads
    /// <see cref="LogRetention.KeepRunsEnvVar"/> lazily on every
    /// <see cref="StartNewRun"/> when this is null.</param>
    /// <param name="keepDays">Retention override for tests; production reads
    /// <see cref="LogRetention.KeepDaysEnvVar"/> lazily when this is null.</param>
    public McpActivityJsonLog(
        string? directory = null, string? mirrorDirectory = null,
        int? keepRuns = null, int? keepDays = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;
        _mirrorDirectory = string.IsNullOrWhiteSpace(mirrorDirectory) ? null : mirrorDirectory;
        _keepRunsOverride = keepRuns;
        _keepDaysOverride = keepDays;
    }

    /// <summary>
    /// Rolls to a fresh per-run file named
    /// <c>activity-yyyyMMdd-HHmmss-fff.log</c>. Best-effort: a failure to
    /// create the directory leaves <see cref="CurrentPath"/> set but writes
    /// will simply no-op.
    /// </summary>
    public void StartNewRun(DateTimeOffset? nowUtc = null)
    {
        var stamp = (nowUtc ?? DateTimeOffset.UtcNow).ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            // If this stamp collides with the current run (same-millisecond
            // re-roll — rapid Clear clicks or fast unit-test runs), bump to a
            // unique suffix so each logical run always gets its own file.
            var candidate = Path.Combine(_directory, $"activity-{stamp}.log");
            if (candidate == CurrentPath)
            {
                int n = 2;
                string bump;
                do { bump = Path.Combine(_directory, $"activity-{stamp}-{n++}.log"); }
                while (bump == CurrentPath); // paranoia: always exit on first iteration
                candidate = bump;
            }
            CurrentPath = candidate;
            CurrentMirrorPath = _mirrorDirectory is null
                ? null
                : Path.Combine(_mirrorDirectory, Path.GetFileName(candidate));

            try
            {
                Directory.CreateDirectory(_directory);
                // Create the file itself (empty) right away rather than waiting for
                // the first Append — retention pruning (below) inspects what's on
                // disk, and the just-started run must count as one of the kept
                // runs even before its first entry is written.
                if (!File.Exists(candidate))
                    File.Create(candidate).Dispose();
            }
            catch { /* Best effort — Append will no-op if the file can't be written. */ }

            if (_mirrorDirectory is not null)
            {
                try
                {
                    Directory.CreateDirectory(_mirrorDirectory);
                    if (CurrentMirrorPath is not null && !File.Exists(CurrentMirrorPath))
                        File.Create(CurrentMirrorPath).Dispose();
                }
                catch { /* Best effort — the mirror simply won't be written. */ }
            }
        }

        PruneOldRuns(nowUtc ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Retention (issue #147 item 5): keep the newest N runs or the last M
    /// days, whichever is smaller — env-configurable via
    /// <see cref="LogRetention.KeepRunsEnvVar"/> / <see cref="LogRetention.KeepDaysEnvVar"/>,
    /// or the constructor overrides for tests. Applied independently to the
    /// primary directory and the mirror directory (if any). Best-effort: any
    /// failure here must never affect logging.
    /// </summary>
    private void PruneOldRuns(DateTimeOffset nowUtc)
    {
        try
        {
            int keepRuns = _keepRunsOverride ?? LogRetention.ResolveKeepRuns(Environment.GetEnvironmentVariable);
            int keepDays = _keepDaysOverride ?? LogRetention.ResolveKeepDays(Environment.GetEnvironmentVariable);

            PruneDirectory(_directory, nowUtc.UtcDateTime, keepRuns, keepDays);
            if (_mirrorDirectory is not null)
                PruneDirectory(_mirrorDirectory, nowUtc.UtcDateTime, keepRuns, keepDays);
        }
        catch { /* best effort */ }
    }

    private static void PruneDirectory(string dir, DateTime nowUtc, int keepRuns, int keepDays)
    {
        try
        {
            if (!Directory.Exists(dir))
                return;

            var files = Directory.GetFiles(dir, "activity-*.log");
            if (files.Length == 0)
                return;

            var withTimes = new List<(string Path, DateTime Ts)>(files.Length);
            foreach (var f in files)
            {
                var ts = ParseRunTimestamp(Path.GetFileNameWithoutExtension(f)) ?? File.GetLastWriteTimeUtc(f);
                withTimes.Add((f, ts));
            }

            var toDelete = LogRetention.SelectForDeletion(withTimes, x => x.Ts, nowUtc, keepRuns, keepDays);
            foreach (var (path, _) in toDelete)
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>Parses the "activity-yyyyMMdd-HHmmss-fff[-n]" stamp back to a UTC timestamp.</summary>
    private static DateTime? ParseRunTimestamp(string fileNameWithoutExtension)
    {
        const string prefix = "activity-";
        if (!fileNameWithoutExtension.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var rest = fileNameWithoutExtension[prefix.Length..];
        // Base stamp is always the first 19 chars: yyyyMMdd-HHmmss-fff
        // (8 + 1 + 6 + 1 + 3). A same-millisecond re-roll appends "-n";
        // ignore that suffix here.
        const int stampLength = 19;
        if (rest.Length < stampLength)
            return null;
        var stamp = rest[..stampLength];

        return DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmss-fff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt)
            ? dt
            : null;
    }

    /// <summary>
    /// Appends one entry as a single JSON line. No-op before the first
    /// <see cref="StartNewRun"/>. Never throws.
    /// </summary>
    public void Append(McpActivityEntry entry)
    {
        if (entry is null)
            return;

        string? path, mirror;
        lock (_gate)
        {
            path = CurrentPath;
            mirror = CurrentMirrorPath;
        }
        if (path is null)
            return;

        string line;
        try
        {
            line = JsonSerializer.Serialize(new
            {
                ts = entry.TimestampUtc.UtcDateTime.ToString("o"),
                tool = entry.ToolName,
                ok = entry.Success,
                ms = entry.Duration.TotalMilliseconds,
                args = entry.Arguments,
                response = entry.Response,
            }, JsonOpts);
        }
        catch
        {
            return; // can't serialise → nothing to write
        }

        // Each sink is independently best-effort: a failure on one (or that one
        // logging must never break a tool call) must not stop the other.
        AppendLine(path, line);
        if (mirror is not null)
            AppendLine(mirror, line);
    }

    private void AppendLine(string path, string line)
    {
        try
        {
            lock (_gate)
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Logging must never break a tool call. Swallow everything.
        }
    }
}
