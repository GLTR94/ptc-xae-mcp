using System.Text.RegularExpressions;
using PtcXaeMcp.IntegrationTests;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #163: Stage HMI's source picker (<see cref="HmiFixtureSource"/>, linked from the
/// integration-test project) and the committed neutral fixture it defaults to.
/// </summary>
public sealed class HmiFixtureSourceTests : IDisposable
{
    private readonly string _dir;

    public HmiFixtureSourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ptc-hmi-fixture-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A fake checkout with the fixture's marker .hmiproj; returns a deep start folder.</summary>
    private string FakeCheckout()
    {
        string project = Path.Combine(_dir, HmiFixtureSource.RepoRelativeDirectory, HmiFixtureSource.ProjectName);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, HmiFixtureSource.ProjectName + ".hmiproj"), "<Project />");
        string start = Path.Combine(_dir, "src", "PtcXaeMcp.IntegrationTests", "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(start);
        return start;
    }

    [Fact]
    public void EnvVar_wins_even_without_require_live_and_is_trimmed()
    {
        string start = FakeCheckout();

        var r = HmiFixtureSource.Resolve("  D:\\Some\\Hmi  ", requireLive: false, start);

        r.Directory.Should().Be("D:\\Some\\Hmi");
        r.Origin.Should().Be(HmiFixtureSource.Origin.Environment);
        r.Reason.Should().BeEmpty();
    }

    [Fact]
    public void EnvVar_wins_over_the_repo_fixture_under_require_live()
    {
        string start = FakeCheckout();

        var r = HmiFixtureSource.Resolve("D:\\Other", requireLive: true, start);

        r.Directory.Should().Be("D:\\Other");
        r.Origin.Should().Be(HmiFixtureSource.Origin.Environment);
    }

    [Fact]
    public void Require_live_without_env_var_defaults_to_the_committed_fixture()
    {
        string start = FakeCheckout();

        var r = HmiFixtureSource.Resolve(null, requireLive: true, start);

        r.Origin.Should().Be(HmiFixtureSource.Origin.RepoFixture);
        r.Directory.Should().Be(Path.Combine(_dir, HmiFixtureSource.RepoRelativeDirectory));
        r.Reason.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_opt_in_there_is_no_source_so_the_open_solution_is_never_closed(string? env)
    {
        string start = FakeCheckout();

        var r = HmiFixtureSource.Resolve(env, requireLive: false, start);

        r.Directory.Should().BeNull();
        r.Origin.Should().Be(HmiFixtureSource.Origin.None);
        r.Reason.Should().Contain("opt-in").And.Contain("PTC_XAE_MCP_REQUIRE_LIVE=1").And.Contain(HmiFixtureSource.SourceEnvVar);
    }

    [Fact]
    public void Require_live_outside_a_checkout_reports_the_missing_fixture()
    {
        // _dir holds no tests/Fixtures/… — and neither does any ancestor of the temp folder.
        var r = HmiFixtureSource.Resolve(null, requireLive: true, _dir);

        r.Directory.Should().BeNull();
        r.Origin.Should().Be(HmiFixtureSource.Origin.None);
        r.Reason.Should().Contain("create_hmi_fixture.py").And.Contain(HmiFixtureSource.SourceEnvVar);
    }

    [Fact]
    public void A_fixture_folder_without_the_hmiproj_does_not_count()
    {
        Directory.CreateDirectory(Path.Combine(_dir, HmiFixtureSource.RepoRelativeDirectory, HmiFixtureSource.ProjectName));

        HmiFixtureSource.FindRepoFixture(_dir).Should().BeNull();
    }

    // ── the committed fixture itself (tests/Fixtures/PtcXaeMcpHmiFixture) ──────

    private static string RepoFixture() =>
        HmiFixtureSource.FindRepoFixture(AppContext.BaseDirectory)
        ?? throw new DirectoryNotFoundException(
            $"{HmiFixtureSource.RepoRelativeDirectory} not found above {AppContext.BaseDirectory} — " +
            "run `python tests/Fixtures/create_hmi_fixture.py`.");

    [Fact]
    public void Committed_fixture_has_a_solution_that_references_the_project_with_its_guid()
    {
        string root = RepoFixture();
        string hmiproj = File.ReadAllText(Path.Combine(root, HmiFixtureSource.ProjectName, HmiFixtureSource.ProjectName + ".hmiproj"));
        string sln = File.ReadAllText(Path.Combine(root, HmiFixtureSource.FixtureName + ".sln"));

        Match guid = Regex.Match(hmiproj, @"<ProjectGuid>(\{[0-9A-F-]{36}\})</ProjectGuid>");
        guid.Success.Should().BeTrue("the template's $guid1$ must be substituted");
        sln.Should().Contain($"\"{HmiFixtureSource.ProjectName}\\{HmiFixtureSource.ProjectName}.hmiproj\", \"{guid.Groups[1].Value}\"");
        hmiproj.Should().Contain($"<HmiTitle>{HmiFixtureSource.ProjectName}</HmiTitle>");
    }

    [Fact]
    public void Committed_fixture_has_no_template_placeholders_machine_paths_or_restored_packages()
    {
        string root = RepoFixture();
        var placeholder = new Regex(@"\$[A-Za-z_][A-Za-z0-9_.]*\$");
        string[] textExtensions = [".hmiproj", ".sln", ".json", ".config", ".theme", ".localization", ".view", ".tpl", ".css"];

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(f => textExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            string text = File.ReadAllText(file);
            placeholder.Matches(text).Should().BeEmpty($"{file} must have every template placeholder substituted");
            text.Should().NotContainAny([@"C:\Users", @"C:\Repos", @"C:\ProgramData"], $"{file} must not carry machine paths");
        }

        // Restored or generated output is never part of the committed sources (it may exist
        // locally after someone opened the repo copy, so only check git's view: the ignore file).
        File.ReadAllText(Path.Combine(root, ".gitignore")).Should().Contain("packages/").And.Contain(".engineering_servers/");
    }
}
