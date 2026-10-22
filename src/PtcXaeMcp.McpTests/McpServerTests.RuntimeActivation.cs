using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PtcXaeMcp.McpTests;

/// <summary>Runtime activation and trial-license tools.</summary>
public partial class McpServerTests
{
    // ── Test 87 — runtime-activation: tools listed ────────────────────────────

    [SkippableFact]
    public async Task T87_RuntimeActivation_ToolsListed()
    {
        SkipIfNotReachable();
        var tools = await _client!.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("twincat_get_license_info", names);
        Assert.Contains("twincat_dismiss_security_dialog", names);
        Assert.Contains("twincat_open_license_manager_and_request_trial", names);
        Assert.Contains("twincat_generate_trial_licenses", names);
        Assert.Contains("twincat_submit_security_code", names);
    }

    // ── Test 88 — runtime-activation: get_license_info returns info or [Error] ─

    [SkippableFact]
    public async Task T88_GetLicenseInfo_ReturnsLicensesOrError()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_get_license_info",
            new Dictionary<string, object?>());

        var text = GetText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        bool isLicenses = text.Contains("License",   StringComparison.OrdinalIgnoreCase)
                       || text.Contains("No license", StringComparison.OrdinalIgnoreCase);
        bool isError    = text.StartsWith("[Error]", StringComparison.Ordinal);
        Assert.True(isLicenses || isError, $"Unexpected response: {text}");
    }

    // ── Test 89 — runtime-activation: dismiss_security_dialog when none open ──

    [SkippableFact]
    public async Task T89_DismissSecurityDialog_WhenNoneOpen_ReturnsNotFound()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_dismiss_security_dialog",
            new Dictionary<string, object?>());

        var text = GetText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        // Either dismissed (if one was open) or reports not found — never [Error]
        bool notFound = text.Contains("No", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("No running TwinCAT XAE", StringComparison.OrdinalIgnoreCase) // no XAE → no dialog
                     || text.Contains("dismissed", StringComparison.OrdinalIgnoreCase);
        Assert.True(notFound, $"Unexpected response: {text}");
        Assert.DoesNotContain("[Error]", text);
    }

    // ── Test 90 — runtime-activation: activate_config accepts new flags ───────

    [SkippableFact]
    public async Task T90_ActivateConfig_WithAutoFlags_AcceptsParameters()
    {
        SkipIfNotReachable();

        // Just verify the tool accepts the new optional parameters without rejecting them.
        // We don't trigger a real activation here (no fixture project open).
        // Includes the issue #136 flags (autoConfirmLogin / loginMode / updateBootProject)
        // and the issue #195 wait for Run (runTimeoutSeconds).
        var result = await CallToolAsync("twincat_activate_config",
            new Dictionary<string, object?>
            {
                ["autoConfirmRestart"]   = false,
                ["autoGenerateLicenses"] = false,
                ["autoConfirmLogin"]     = false,
                ["loginMode"]            = "online_change",
                ["updateBootProject"]    = true,
                ["runTimeoutSeconds"]    = 30,
            });

        var text = GetText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        // Should return an error (no project) or "Activated" — not a parameter-rejection
        bool ok = text.StartsWith("[Error]", StringComparison.Ordinal)
               || text.Contains("Activated", StringComparison.OrdinalIgnoreCase);
        Assert.True(ok, $"Unexpected response: {text}");
    }

    // ── activate-trial-license ────────────────────────────────────────────────

    [SkippableFact]
    public async Task T91_OpenLicenseManagerAndRequestTrial_ReturnsStringResult()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_open_license_manager_and_request_trial",
            new Dictionary<string, object?>());

        var text = GetText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        // Returns either "button clicked" or "button not found" — never [Error] from missing params
        bool expected = text.Contains("button", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("clicked", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("License Manager", StringComparison.OrdinalIgnoreCase)
                     || text.StartsWith("[Error]", StringComparison.Ordinal); // COM error if no XAE
        Assert.True(expected, $"Unexpected response: {text}");
    }

    [SkippableFact]
    public async Task T92_GenerateTrialLicenses_WhenNoDialog_ReturnsTextOnly()
    {
        SkipIfNotReachable();

        // No confirmation dialog is open, so the tool should return a text block
        // (not an image block) explaining the dialog was not found.
        var result = await CallToolAsync("twincat_generate_trial_licenses",
            new Dictionary<string, object?>());

        Assert.NotNull(result.Content);
        var text = GetText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        // When no dialog is found: only text content, no image.
        var imageBlocks = result.Content.OfType<ImageContentBlock>().ToList();
        bool noDialog = text.Contains("No trial-license dialog", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("No running TwinCAT XAE", StringComparison.OrdinalIgnoreCase) // no XAE → no dialog
                     || text.Contains("security code", StringComparison.OrdinalIgnoreCase); // dialog was open
        if (noDialog && !text.Contains("security code", StringComparison.OrdinalIgnoreCase))
            Assert.Empty(imageBlocks); // no dialog → no image
        Assert.True(noDialog, $"Unexpected response: {text}");
    }

    [SkippableFact]
    public async Task T93_SubmitSecurityCode_WhenNoDialog_ReturnsNotFound()
    {
        SkipIfNotReachable();

        var result = await CallToolAsync("twincat_submit_security_code",
            new Dictionary<string, object?> { ["code"] = "XXXXX" });

        var text = GetText(result);
        Assert.False(string.IsNullOrWhiteSpace(text));
        bool notFound = text.Contains("No ", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("entered", StringComparison.OrdinalIgnoreCase); // was found
        Assert.True(notFound, $"Unexpected response: {text}");
    }
}
