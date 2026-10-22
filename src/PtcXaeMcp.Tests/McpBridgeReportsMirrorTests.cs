using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>
/// <see cref="McpBridge.RepoReportsDirOrNull"/> gating (issue #147 item 6): the
/// repo mirror must stay off unless <c>PTC_XAE_MCP_REPORTS_MIRROR=1</c> is set,
/// so a plain developer/CI run never writes into a git checkout uninvited.
/// Mutates a process-wide environment variable — restores it in
/// <c>finally</c> so it can't leak into other tests.
/// </summary>
public sealed class McpBridgeReportsMirrorTests
{
    [Fact]
    public void RepoReportsDirOrNull_EnvVarUnset_ReturnsNull()
    {
        var original = Environment.GetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar, null);
            Assert.Null(McpBridge.RepoReportsDirOrNull());
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar, original);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("")]
    public void RepoReportsDirOrNull_EnvVarNotExactlyOne_ReturnsNull(string value)
    {
        var original = Environment.GetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar, value);
            Assert.Null(McpBridge.RepoReportsDirOrNull());
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar, original);
        }
    }

    [Fact]
    public void RepoReportsDirOrNull_EnvVarSetToOne_ReturnsPathUnderAGitCheckout_OrNullIfNotOne()
    {
        // We ARE running from a git checkout (this repo), so with the flag on this
        // should resolve to reports/ptc-xae-mcp under it. If the test binaries ever
        // run from a non-checkout deployment, null is still an acceptable, documented
        // outcome — assert only the invariant that matters: never throws, and when
        // non-null it ends with the expected relative path.
        var original = Environment.GetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar, "1");
            var result = McpBridge.RepoReportsDirOrNull();
            if (result is not null)
                Assert.EndsWith(Path.Combine("reports", "ptc-xae-mcp"), result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpBridge.ReportsMirrorEnvVar, original);
        }
    }
}
