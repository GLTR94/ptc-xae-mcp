using PtcXaeMcp.WinForms.Mcp;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Tests for <see cref="McpActivityHtmlReport"/> — the human-readable HTML view
/// of a run that the MCP Activity tab links to. Pure rendering, no I/O.
/// </summary>
public sealed class McpActivityHtmlReportTests
{
    private static McpActivityEntry Op(string tool, bool ok, string? args = null, string? resp = null)
        => new(DateTimeOffset.UtcNow, tool, args ?? "", resp ?? "", TimeSpan.FromMilliseconds(12), ok);

    [Fact]
    public void Render_Empty_ProducesValidHtml_WithNoActivityNotice()
    {
        var html = McpActivityHtmlReport.Render([], [], totalTests: 0, DateTimeOffset.UtcNow);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("</html>", html);
        Assert.Contains("No activity in this run yet", html);
    }

    [Fact]
    public void Render_GroupsOperationsUnderTheirTest()
    {
        var entries = new List<McpActivityEntry>
        {
            Op("twincat_open_project", ok: true),
            Op("twincat_build_plc", ok: true),
            Op("twincat_close_project", ok: false),
        };
        // First test owns ops 0..1, second test owns op 2.
        var boundaries = new List<TestBoundary>
        {
            new(AfterLogCount: 0, Name: "Build_Succeeds", Description: "PLC baut grün", Index: 1),
            new(AfterLogCount: 2, Name: "Close_Fails", Description: "kein Projekt offen", Index: 2),
        };

        var html = McpActivityHtmlReport.Render(entries, boundaries, totalTests: 51, DateTimeOffset.UtcNow);

        // Both test sections render with their narration.
        Assert.Contains("Build_Succeeds", html);
        Assert.Contains("PLC baut grün", html);
        Assert.Contains("Close_Fails", html);
        // All three tools appear.
        Assert.Contains("twincat_open_project", html);
        Assert.Contains("twincat_build_plc", html);
        Assert.Contains("twincat_close_project", html);
        // The failing op gets the error marker; successes the ok marker.
        Assert.Contains("st-err", html);
        Assert.Contains("st-ok", html);
        // The first test's section heading must appear before the second's.
        Assert.True(html.IndexOf("Build_Succeeds", StringComparison.Ordinal)
                  < html.IndexOf("Close_Fails", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_HtmlEncodesArgumentsAndResponse_NoInjection()
    {
        var entries = new List<McpActivityEntry>
        {
            Op("twincat_write_pou_body", ok: true,
               args: "<script>alert('x')</script>",
               resp: "a & b < c > d"),
        };

        var html = McpActivityHtmlReport.Render(entries, [], totalTests: 0, DateTimeOffset.UtcNow);

        // The raw tag must not survive; it must be escaped.
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("a &amp; b &lt; c &gt; d", html);
    }

    [Fact]
    public void Render_OperationsBeforeAnyTest_GoIntoAPreTestSection()
    {
        var entries = new List<McpActivityEntry> { Op("twincat_get_status", ok: true) };

        var html = McpActivityHtmlReport.Render(entries, [], totalTests: 0, DateTimeOffset.UtcNow);

        Assert.Contains("Before the first test", html);
        Assert.Contains("twincat_get_status", html);
    }
}
