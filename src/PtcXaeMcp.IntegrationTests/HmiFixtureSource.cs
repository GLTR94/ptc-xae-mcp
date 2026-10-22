namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Decides which HMI project Stage HMI copies to %TEMP% (issue #163). Pure — no MCP, no
/// XAE, no environment reads — so it is unit-tested in PtcXaeMcp.Tests (the file is linked
/// there).
/// <list type="number">
/// <item><see cref="SourceEnvVar"/> set: that folder, always (it is also the opt-in).</item>
/// <item>Otherwise, only when the caller demanded a live run (<c>PTC_XAE_MCP_REQUIRE_LIVE=1</c>):
/// the committed neutral fixture <c>tests/Fixtures/PtcXaeMcpHmiFixture</c>, found by walking up
/// from the test assembly. It is created by <c>tests/Fixtures/create_hmi_fixture.py</c> — keep
/// the names here in sync with that script.</item>
/// <item>Otherwise: no source, the stage skips. Opening the fixture CLOSES the solution open in
/// the XAE without saving, so a plain <c>dotnet test</c> must never do that on its own.</item>
/// </list>
/// </summary>
public static class HmiFixtureSource
{
    public const string SourceEnvVar = "PTC_XAE_MCP_HMI_FIXTURE_SOURCE";

    /// <summary>Fixture folder and .sln name.</summary>
    public const string FixtureName = "PtcXaeMcpHmiFixture";

    /// <summary>The HMI project inside the fixture (folder + .hmiproj name).</summary>
    public const string ProjectName = "PtcXaeMcpHmi";

    /// <summary>Fixture folder relative to the repository root.</summary>
    public static readonly string RepoRelativeDirectory = Path.Combine("tests", "Fixtures", FixtureName);

    /// <summary>Where the source came from.</summary>
    public enum Origin
    {
        /// <summary>No source: the stage skips with <see cref="Resolution.Reason"/>.</summary>
        None,
        /// <summary><see cref="SourceEnvVar"/> named the folder.</summary>
        Environment,
        /// <summary>The committed neutral fixture.</summary>
        RepoFixture,
    }

    /// <param name="Directory">The folder to copy (holds the .hmiproj, maybe in a sub-folder), or null.</param>
    /// <param name="Reason">Why there is no source; empty when <paramref name="Directory"/> is set.</param>
    public sealed record Resolution(string? Directory, Origin Origin, string Reason);

    /// <summary>
    /// Resolves the source folder. <paramref name="envValue"/> is the raw value of
    /// <see cref="SourceEnvVar"/>; <paramref name="startDirectory"/> is where the walk-up for the
    /// repository starts (the test assembly folder). Existence of an env-var folder is checked by
    /// the caller, so a wrong path still reports which variable named it.
    /// </summary>
    public static Resolution Resolve(string? envValue, bool requireLive, string startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(envValue))
            return new Resolution(envValue.Trim(), Origin.Environment, "");

        if (!requireLive)
            return new Resolution(null, Origin.None,
                $"Stage HMI is opt-in: opening its fixture closes the solution open in the XAE without saving. " +
                $"Set PTC_XAE_MCP_REQUIRE_LIVE=1 to run it against the neutral fixture {RepoRelativeDirectory} " +
                $"(copied to %TEMP%, never opened in place), or {SourceEnvVar} to the root folder of another " +
                "neutral HMI project — never a customer project.");

        string? repoFixture = FindRepoFixture(startDirectory);
        if (repoFixture is not null)
            return new Resolution(repoFixture, Origin.RepoFixture, "");

        return new Resolution(null, Origin.None,
            $"The neutral HMI fixture {RepoRelativeDirectory} was not found above '{startDirectory}' (the tests do " +
            $"not run from a source checkout). Set {SourceEnvVar} to the root folder of a neutral HMI project, or " +
            "recreate the fixture with `python tests/Fixtures/create_hmi_fixture.py`.");
    }

    /// <summary>
    /// The committed fixture folder, found by walking up from <paramref name="startDirectory"/>;
    /// null when no ancestor holds <c>tests/Fixtures/PtcXaeMcpHmiFixture/PtcXaeMcpHmi/PtcXaeMcpHmi.hmiproj</c>.
    /// </summary>
    public static string? FindRepoFixture(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, RepoRelativeDirectory);
            if (File.Exists(Path.Combine(candidate, ProjectName, ProjectName + ".hmiproj")))
                return candidate;
        }
        return null;
    }
}
