using PtcXaeMcp.WinForms.Diagnostics;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for <see cref="LogRetention"/> — the shared "keep the
/// newest N runs or M days, whichever is smaller" selection used by both the
/// host log and the per-run activity log (issue #147 item 5/9). No disk, no
/// SDK, no XAE.
/// </summary>
public sealed class LogRetentionTests
{
    private static DateTime Day(int offsetFromNow, DateTime nowUtc) => nowUtc.AddDays(-offsetFromNow);

    [Fact]
    public void SelectForDeletion_KeepsNewestRuns_WhenCountIsTheBindingConstraint()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        // 5 runs, all within the day window, but keepRuns=2 is the tighter bound.
        var items = Enumerable.Range(0, 5).Select(i => Day(i, now)).ToList();

        var toDelete = LogRetention.SelectForDeletion(items, t => t, now, keepRuns: 2, keepDays: 30);

        Assert.Equal(3, toDelete.Count);
        // The two most recent (offset 0 and 1) must be kept, i.e. NOT in the delete set.
        Assert.DoesNotContain(Day(0, now), toDelete);
        Assert.DoesNotContain(Day(1, now), toDelete);
    }

    [Fact]
    public void SelectForDeletion_KeepsWithinDayWindow_WhenAgeIsTheBindingConstraint()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        // 5 runs, keepRuns=50 (never binds), keepDays=2 is the tighter bound.
        var items = Enumerable.Range(0, 5).Select(i => Day(i, now)).ToList();

        var toDelete = LogRetention.SelectForDeletion(items, t => t, now, keepRuns: 50, keepDays: 2);

        // Only items strictly within the last 2 days (offset 0, 1, 2 given AddDays(-2) cutoff) survive.
        Assert.Equal(2, toDelete.Count);
        Assert.Contains(Day(3, now), toDelete);
        Assert.Contains(Day(4, now), toDelete);
    }

    [Fact]
    public void SelectForDeletion_EmptyInput_ReturnsEmpty()
    {
        var now = DateTime.UtcNow;
        var toDelete = LogRetention.SelectForDeletion(Array.Empty<DateTime>(), t => t, now, 50, 30);
        Assert.Empty(toDelete);
    }

    [Fact]
    public void SelectForDeletion_FewerItemsThanKeepRuns_AndAllWithinWindow_DeletesNothing()
    {
        var now = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var items = new List<DateTime> { Day(0, now), Day(1, now), Day(2, now) };

        var toDelete = LogRetention.SelectForDeletion(items, t => t, now, keepRuns: 50, keepDays: 30);

        Assert.Empty(toDelete);
    }

    [Fact]
    public void ResolveKeepRuns_MissingEnvVar_ReturnsDefault()
    {
        Assert.Equal(LogRetention.DefaultKeepRuns, LogRetention.ResolveKeepRuns(_ => null));
    }

    [Fact]
    public void ResolveKeepDays_MissingEnvVar_ReturnsDefault()
    {
        Assert.Equal(LogRetention.DefaultKeepDays, LogRetention.ResolveKeepDays(_ => null));
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("1", 1)]
    public void ResolveKeepRuns_ValidPositiveInteger_IsUsed(string raw, int expected)
    {
        Assert.Equal(expected, LogRetention.ResolveKeepRuns(name => name == LogRetention.KeepRunsEnvVar ? raw : null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-5")]
    public void ResolveKeepRuns_InvalidOrNonPositiveValue_FallsBackToDefault(string raw)
    {
        Assert.Equal(
            LogRetention.DefaultKeepRuns,
            LogRetention.ResolveKeepRuns(name => name == LogRetention.KeepRunsEnvVar ? raw : null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-1")]
    public void ResolveKeepDays_InvalidOrNonPositiveValue_FallsBackToDefault(string raw)
    {
        Assert.Equal(
            LogRetention.DefaultKeepDays,
            LogRetention.ResolveKeepDays(name => name == LogRetention.KeepDaysEnvVar ? raw : null));
    }

    [Fact]
    public void ResolveKeepDays_ValidPositiveInteger_IsUsed()
    {
        Assert.Equal(7, LogRetention.ResolveKeepDays(name => name == LogRetention.KeepDaysEnvVar ? "7" : null));
    }
}
