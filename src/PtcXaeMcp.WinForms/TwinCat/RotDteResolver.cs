namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>One running DTE found in the COM Running Object Table.</summary>
/// <param name="SolutionPath">Its open solution's full path, or null if none.</param>
/// <param name="Dte">The live <c>_DTE</c> COM object.</param>
internal sealed record RotDteCandidate(string? SolutionPath, object Dte);

internal enum RotDteOutcome
{
    /// <summary>No expected solution is known — caller must NOT guess; keep the hard block.</summary>
    NoExpectedPath,
    /// <summary>No running DTE has the expected solution open.</summary>
    None,
    /// <summary>Exactly one running DTE has the expected solution open — bind to it.</summary>
    Unique,
    /// <summary>More than one running DTE has the expected solution open — still ambiguous.</summary>
    Ambiguous,
}

internal sealed record RotDteResolution(RotDteOutcome Outcome, object? Dte, int MatchCount)
{
    public static readonly RotDteResolution NoExpected = new(RotDteOutcome.NoExpectedPath, null, 0);
    public static RotDteResolution NoMatch() => new(RotDteOutcome.None, null, 0);
    public static RotDteResolution UniqueMatch(object dte) => new(RotDteOutcome.Unique, dte, 1);
    public static RotDteResolution AmbiguousMatch(int n) => new(RotDteOutcome.Ambiguous, null, n);
}

/// <summary>
/// Live COM enumeration of running DTE instances. Behind an interface so the
/// pure selection logic in <see cref="RotDteResolver"/> is unit-testable; the
/// real implementation (<see cref="RotDteEnumerator"/>) is COM/ROT and is
/// verified live (live suite), never by a fake-free unit test.
/// </summary>
internal interface IRotDteEnumerator
{
    IReadOnlyList<RotDteCandidate> Enumerate();
}

/// <summary>
/// Q2 root-cause fix. Instead of relaxing the multi-XAE count guard, bind
/// deterministically to the running DTE whose open solution equals the
/// expected active/fixture project. Zero or &gt;1 matches stay ambiguous and
/// keep the hard block — this dissolves the COM <c>GetActiveObject</c>
/// nondeterminism rather than hoping (issue #57).
/// </summary>
internal static class RotDteResolver
{
    public static RotDteResolution Resolve(
        IReadOnlyList<RotDteCandidate> candidates, string? expectedSolutionPath)
    {
        string? expected = SolutionPathMatch.Normalize(expectedSolutionPath);
        if (expected is null) return RotDteResolution.NoExpected;

        var matches = candidates
            .Where(c => SolutionPathMatch.Normalize(c.SolutionPath) == expected)
            .ToList();

        return matches.Count switch
        {
            0 => RotDteResolution.NoMatch(),
            1 => RotDteResolution.UniqueMatch(matches[0].Dte),
            _ => RotDteResolution.AmbiguousMatch(matches.Count),
        };
    }
}
