using System.Drawing;
using System.Text.Json;
using PtcXaeMcp.WinForms.Dialogs;
using PtcXaeMcp.WinForms.Theme;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #273: connect-a-client text, ui.json, window clamping, sample copy naming.</summary>
public sealed class GuiDialogsTests
{
    private const string SecretToken = "s3cr3t-T0ken-value";

    // ---- snippet / command -------------------------------------------------------------

    [Fact]
    public void McpJsonSnippet_WithoutToken_IsTheDocumentedUrlOnlyForm()
    {
        using var doc = JsonDocument.Parse(ClientConnectInfo.McpJsonSnippet(5010, tokenSet: false));
        var server = doc.RootElement.GetProperty("mcpServers").GetProperty("ptc-xae");
        server.GetProperty("url").GetString().Should().Be("http://localhost:5010/");
        server.TryGetProperty("headers", out _).Should().BeFalse();
    }

    [Fact]
    public void McpJsonSnippet_WithToken_HasBearerHeaderPlaceholderNeverTheToken()
    {
        string text = ClientConnectInfo.McpJsonSnippet(5010, tokenSet: true);
        using var doc = JsonDocument.Parse(text);
        var server = doc.RootElement.GetProperty("mcpServers").GetProperty("ptc-xae");
        server.GetProperty("headers").GetProperty("Authorization").GetString()
            .Should().Be("Bearer " + ClientConnectInfo.TokenPlaceholder);
    }

    [Fact]
    public void ClaudeCommand_WithAndWithoutToken()
    {
        ClientConnectInfo.ClaudeMcpAddCommand(5010, tokenSet: false)
            .Should().Be("claude mcp add --transport http ptc-xae http://localhost:5010/");
        ClientConnectInfo.ClaudeMcpAddCommand(5010, tokenSet: true)
            .Should().StartWith("claude mcp add --transport http ptc-xae http://localhost:5010/ --header")
            .And.Contain("Bearer " + ClientConnectInfo.TokenPlaceholder);
    }

    [Fact]
    public void GeneratedText_NeverContainsTheConfiguredToken()
    {
        // The generators take only "is a token set", and the settings rows get the environment
        // through an injected lookup (no process-wide env var, so no race with other tests).
        string all = ClientConnectInfo.McpJsonSnippet(5010, true) + ClientConnectInfo.ClaudeMcpAddCommand(5010, true)
            + string.Join("|", SettingsInfo.Rows(5010, false, n => n == "PTC_XAE_MCP_TOKEN" ? SecretToken : null)
                .Select(r => r.Name + r.Value + r.HowToChange));
        all.Should().NotContain(SecretToken);
    }

    [Fact]
    public void SettingsRows_ReportTokenAsSetOrNotSet_WithTheEnvVarName()
    {
        var rows = SettingsInfo.Rows(5010, false, n => n == "PTC_XAE_MCP_TOKEN" ? SecretToken : null);
        var token = rows.Single(r => r.Name == "Access token");
        token.Value.Should().Be("set");
        token.HowToChange.Should().Contain("PTC_XAE_MCP_TOKEN");
        SettingsInfo.Rows(5010, false, _ => null).Single(r => r.Name == "Access token").Value.Should().Be("not set");
    }

    [Fact]
    public void SettingsRows_Diagnostics_ShowTheServersRealState()
    {
        // The value comes from McpBridge.DiagnosticsEnabled, not from a copy of Program's rule.
        SettingsInfo.Rows(5010, true, _ => null).Single(r => r.Name == "Diagnostics tools").Value.Should().Be("on");
        SettingsInfo.Rows(5010, false, n => n == "PTC_XAE_MCP_DIAGNOSTICS" ? "1" : null)
            .Single(r => r.Name == "Diagnostics tools").Value.Should().Be("off");
    }

    [Fact]
    public void DescribeHealth_ReadsStatusVersionAuth()
    {
        ClientConnectInfo.DescribeHealth("{\"status\":\"ok\",\"version\":\"1.2.3\",\"authRequired\":true}")
            .Should().Contain("ok").And.Contain("1.2.3").And.Contain("token required: yes");
        ClientConnectInfo.DescribeHealth("not json").Should().Contain("not with the expected");
    }

    // ---- ui.json -----------------------------------------------------------------------

    [Fact]
    public void UiSettings_OldFormatWithOnlyTheme_Loads()
    {
        var s = UiSettings.Parse("{\n  \"theme\": \"dark\"\n}");
        s.DarkTheme.Should().BeTrue();
        s.RememberWindowPosition.Should().BeTrue();
        s.Window.Should().BeNull();
    }

    [Fact]
    public void UiSettings_NewFormat_RoundTrips()
    {
        var original = new UiSettings
        {
            DarkTheme = true,
            RememberWindowPosition = false,
            Window = new WindowPlacement(-1200, 40, 900, 700, true),
        };
        var back = UiSettings.Parse(original.ToJson());
        back.DarkTheme.Should().BeTrue();
        back.RememberWindowPosition.Should().BeFalse();
        back.Window.Should().Be(new WindowPlacement(-1200, 40, 900, 700, true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"theme\":42,\"rememberWindowPosition\":\"yes\",\"window\":{\"x\":1}}")]
    public void UiSettings_CorruptOrOddContent_GivesDefaults(string json)
    {
        var s = UiSettings.Parse(json);
        s.DarkTheme.Should().BeFalse();
        s.RememberWindowPosition.Should().BeTrue();
        s.Window.Should().BeNull();
    }

    [Fact]
    public void UiSettings_FileRoundTrip_AndMissingOrCorruptFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ptc-ui-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "ui.json");
        try
        {
            UiSettings.Load(path).DarkTheme.Should().BeFalse();       // missing
            new UiSettings { DarkTheme = true, Window = new WindowPlacement(1, 2, 300, 400, false) }.Save(path);
            var loaded = UiSettings.Load(path);
            loaded.DarkTheme.Should().BeTrue();
            loaded.Window!.Width.Should().Be(300);
            File.WriteAllText(path, "}{");
            UiSettings.Load(path).DarkTheme.Should().BeFalse();       // corrupt
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // ---- window clamping ---------------------------------------------------------------

    private static readonly Rectangle Primary = new(0, 0, 1920, 1040);
    private static readonly Rectangle Second = new(1920, 0, 1920, 1040);
    private static readonly Size Min = new(850, 680);

    [Fact]
    public void Clamp_VisibleWindow_IsUnchanged()
    {
        UiSettings.ClampToScreens(new Rectangle(100, 100, 1000, 800), [Primary], Min)
            .Should().Be(new Rectangle(100, 100, 1000, 800));
    }

    [Fact]
    public void Clamp_WindowOnSecondMonitor_StaysThere()
    {
        UiSettings.ClampToScreens(new Rectangle(2000, 50, 1000, 800), [Primary, Second], Min)
            .Should().Be(new Rectangle(2000, 50, 1000, 800));
    }

    [Fact]
    public void Clamp_SecondMonitorGone_MovesToPrimary()
    {
        var r = UiSettings.ClampToScreens(new Rectangle(2000, 50, 1000, 800), [Primary], Min)!.Value;
        Primary.Contains(r).Should().BeTrue();
        r.Size.Should().Be(new Size(1000, 800));
    }

    [Fact]
    public void Clamp_MonitorLeftOfPrimaryGone_MovesToPrimary()
    {
        var r = UiSettings.ClampToScreens(new Rectangle(-1800, 100, 900, 700), [Primary], Min)!.Value;
        Primary.Contains(r).Should().BeTrue();
    }

    [Fact]
    public void Clamp_OversizedWindow_IsShrunkToTheScreen()
    {
        UiSettings.ClampToScreens(new Rectangle(-50, -50, 5000, 4000), [Primary], Min)
            .Should().Be(Primary);
    }

    [Fact]
    public void Clamp_BarelyVisibleWindow_IsMovedBackOnScreen()
    {
        // Only 20 px of the window overlap the screen: treated as not visible.
        var r = UiSettings.ClampToScreens(new Rectangle(1900, 1000, 1000, 800), [Primary], Min)!.Value;
        Primary.Contains(r).Should().BeTrue();
    }

    [Fact]
    public void Clamp_TinySavedSize_IsRaisedToTheMinimum()
    {
        UiSettings.ClampToScreens(new Rectangle(10, 10, 100, 100), [Primary], Min)!.Value.Size.Should().Be(Min);
    }

    [Fact]
    public void Clamp_NoScreens_ReturnsNull() =>
        UiSettings.ClampToScreens(new Rectangle(0, 0, 1000, 800), [], Min).Should().BeNull();

    // ---- sample copy -------------------------------------------------------------------

    [Fact]
    public void SampleTarget_UsesDocumentsFolderAndTimestamp()
    {
        SampleProject.TargetDirectory(@"C:\Users\x\Documents", new DateTime(2026, 10, 19, 14, 5, 9))
            .Should().Be(@"C:\Users\x\Documents\Pneutronic XAE MCP\Sample-20261019-140509");
    }

    [Fact]
    public void SampleTarget_Unique_AppendsCounterWhenTheFolderExists()
    {
        var now = new DateTime(2026, 10, 19, 14, 5, 9);
        string first = SampleProject.TargetDirectory("D", now);
        var taken = new HashSet<string> { first, first + "-2" };
        SampleProject.UniqueTargetDirectory("D", now, taken.Contains).Should().Be(first + "-3");
        SampleProject.UniqueTargetDirectory("D", now, _ => false).Should().Be(first);
    }

    [Fact]
    public void SampleShippedDirectory_IsBelowTheAppDirectory() =>
        SampleProject.ShippedDirectory(@"C:\App").Should().Be(@"C:\App\Sample\PtcXaeMcpFixture");

    [Fact]
    public void SampleCopy_CopiesTreeAndReturnsTheSolution()
    {
        string root = Path.Combine(Path.GetTempPath(), "ptc-sample-" + Guid.NewGuid().ToString("N"));
        string src = Path.Combine(root, "src");
        string dst = Path.Combine(root, "dst");
        try
        {
            Directory.CreateDirectory(Path.Combine(src, "Sub"));
            File.WriteAllText(Path.Combine(src, SampleProject.SolutionFileName), "sln");
            File.WriteAllText(Path.Combine(src, "Sub", "a.txt"), "a");
            File.SetAttributes(Path.Combine(src, "Sub", "a.txt"), FileAttributes.ReadOnly);

            string sln = SampleProject.CopyTo(src, dst);

            sln.Should().Be(Path.Combine(dst, SampleProject.SolutionFileName));
            File.ReadAllText(Path.Combine(dst, "Sub", "a.txt")).Should().Be("a");
            (File.GetAttributes(Path.Combine(dst, "Sub", "a.txt")) & FileAttributes.ReadOnly).Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (string f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void SampleCopy_MissingSource_Throws() =>
        Assert.Throws<DirectoryNotFoundException>(() => SampleProject.CopyTo(@"Z:\does\not\exist", Path.GetTempPath()));
}
