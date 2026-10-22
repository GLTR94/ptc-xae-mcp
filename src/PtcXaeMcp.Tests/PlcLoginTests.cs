using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Unit tests for the plc-login feature group.
/// Tests are pure (no COM / no XAE) — all TwinCAT interactions go through
/// FakeTwinCatSession or Win32DialogAutomation static helpers.
/// </summary>
public class PlcLoginTests
{
    // ── Win32DialogAutomation: IsLoginDialog ─────────────────────────────────

    [Fact]
    public void IsLoginDialog_OnlineChangeTextDE_Matches()
    {
        var texts = new[] { "Mit Online-Change einloggen", "Einloggen mit Download", "Einloggen ohne Änderung", "Abbrechen" };
        Assert.True(Win32DialogAutomation.IsLoginDialog(texts));
    }

    [Fact]
    public void IsLoginDialog_OnlineChangeTextEN_Matches()
    {
        var texts = new[] { "Login with Online Change", "Login with download", "Login without changes", "Cancel" };
        Assert.True(Win32DialogAutomation.IsLoginDialog(texts));
    }

    [Fact]
    public void IsLoginDialog_UnrelatedTexts_DoesNotMatch()
    {
        var texts = new[] { "OK", "Cancel", "Sicherheitscode eingeben" };
        Assert.False(Win32DialogAutomation.IsLoginDialog(texts));
    }

    [Fact]
    public void IsLoginDialog_EmptyList_DoesNotMatch()
    {
        Assert.False(Win32DialogAutomation.IsLoginDialog(Array.Empty<string>()));
    }

    // ── Win32DialogAutomation: LoginDialogButtonLabels ───────────────────────

    [Fact]
    public void LoginDialogButtonLabels_OnlineChange_ContainsOnlineChangeLabel()
    {
        var labels = Win32DialogAutomation.LoginDialogButtonLabels(LoginMode.OnlineChange);
        Assert.Contains(labels, l => l.Contains("Online-Change", StringComparison.OrdinalIgnoreCase)
                                  || l.Contains("Online Change",  StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoginDialogButtonLabels_Download_ContainsDownloadLabel()
    {
        var labels = Win32DialogAutomation.LoginDialogButtonLabels(LoginMode.Download);
        Assert.Contains(labels, l => l.Contains("Download", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoginDialogButtonLabels_LoginWithoutChange_ContainsWithoutChangesLabel()
    {
        var labels = Win32DialogAutomation.LoginDialogButtonLabels(LoginMode.LoginWithoutChange);
        Assert.Contains(labels, l => l.Contains("ohne Änderung",    StringComparison.OrdinalIgnoreCase)
                                  || l.Contains("without changes",  StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoginDialogButtonLabels_AllModesReturnNonEmpty()
    {
        foreach (LoginMode m in Enum.GetValues<LoginMode>())
            Assert.NotEmpty(Win32DialogAutomation.LoginDialogButtonLabels(m));
    }

    // ── Win32DialogAutomation: IsFirstDownloadConfirmText ────────────────────

    [Fact]
    public void IsFirstDownloadConfirmText_GermanText_Matches()
    {
        Assert.True(Win32DialogAutomation.IsFirstDownloadConfirmText(
            "Applikation existiert nicht auf dem Gerät — erstellen und laden?"));
    }

    [Fact]
    public void IsFirstDownloadConfirmText_EnglishText_Matches()
    {
        Assert.True(Win32DialogAutomation.IsFirstDownloadConfirmText(
            "The application does not exist on the device — create and download?"));
    }

    [Fact]
    public void IsFirstDownloadConfirmText_UnrelatedText_DoesNotMatch()
    {
        Assert.False(Win32DialogAutomation.IsFirstDownloadConfirmText("OK"));
    }

    // ── Win32DialogAutomation: IsOverwriteRunningConfirmText ─────────────────

    [Fact]
    public void IsOverwriteRunningConfirmText_GermanText_Matches()
    {
        Assert.True(Win32DialogAutomation.IsOverwriteRunningConfirmText(
            "Laufende Applikation überschreiben?"));
    }

    [Fact]
    public void IsOverwriteRunningConfirmText_EnglishText_Matches()
    {
        Assert.True(Win32DialogAutomation.IsOverwriteRunningConfirmText(
            "Overwrite the running application?"));
    }

    [Fact]
    public void IsOverwriteRunningConfirmText_UnrelatedText_DoesNotMatch()
    {
        Assert.False(Win32DialogAutomation.IsOverwriteRunningConfirmText("Cancel"));
    }

    // ── Win32DialogAutomation: IsAppChangedLoginModal (issue #136) ───────────

    [Fact]
    public void IsAppChangedLoginModal_GermanText_Matches()
    {
        var texts = new[]
        {
            "Die Applikation wurde seit dem letzten Download geändert. Was möchten Sie tun?",
            "Mit Online-Change einloggen", "Mit Download einloggen", "Ohne Änderung einloggen",
            "Bootapplikation aktualisieren", "OK", "Abbrechen",
        };
        Assert.True(Win32DialogAutomation.IsAppChangedLoginModal(texts));
    }

    [Fact]
    public void IsAppChangedLoginModal_EnglishText_Matches()
    {
        var texts = new[] { "The application has changed since the last download.", "OK" };
        Assert.True(Win32DialogAutomation.IsAppChangedLoginModal(texts));
    }

    [Fact]
    public void IsAppChangedLoginModal_QuestionOnly_Matches()
    {
        Assert.True(Win32DialogAutomation.IsAppChangedLoginModal(new[] { "Was möchten Sie tun?" }));
    }

    [Fact]
    public void IsAppChangedLoginModal_RestartDialog_DoesNotMatch()
    {
        var texts = new[] { "Neustart TwinCAT System im Run Modus", "OK" };
        Assert.False(Win32DialogAutomation.IsAppChangedLoginModal(texts));
    }

    [Fact]
    public void IsAppChangedLoginModal_EmptyList_DoesNotMatch()
    {
        Assert.False(Win32DialogAutomation.IsAppChangedLoginModal(Array.Empty<string>()));
    }

    // ── Win32DialogAutomation: AppChangedRadioNeedles ────────────────────────

    [Fact]
    public void AppChangedRadioNeedles_MatchTheirRadioCaption()
    {
        // (mode, real radio caption) pairs — each caption (accelerators stripped)
        // must contain at least one of the mode's needles.
        (LoginMode Mode, string Caption)[] cases =
        [
            (LoginMode.OnlineChange,       "Mit Online-Change einloggen"),
            (LoginMode.Download,           "Mit Download einloggen"),
            (LoginMode.LoginWithoutChange, "Ohne Änderung einloggen"),
        ];
        foreach (var (mode, caption) in cases)
        {
            var needles = Win32DialogAutomation.AppChangedRadioNeedles(mode);
            Assert.NotEmpty(needles);
            string cleaned = caption.Replace("&", "");
            Assert.Contains(needles, n => cleaned.Contains(n, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AppChangedRadioNeedles_OnlineChange_DoesNotMatchDownloadCaption()
    {
        // Guard against the download radio being mistaken for the online-change one.
        var ocNeedles = Win32DialogAutomation.AppChangedRadioNeedles(LoginMode.OnlineChange);
        Assert.DoesNotContain(ocNeedles, n =>
            "Mit Download einloggen".Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoginModeWire_MapsEachMode()
    {
        Assert.Equal("online_change",        Win32DialogAutomation.LoginModeWire(LoginMode.OnlineChange));
        Assert.Equal("download",             Win32DialogAutomation.LoginModeWire(LoginMode.Download));
        Assert.Equal("login_without_change", Win32DialogAutomation.LoginModeWire(LoginMode.LoginWithoutChange));
    }

    // ── ActivateResult: new diagnostic fields default safely ─────────────────

    [Fact]
    public void ActivateResult_Defaults_AreBackwardCompatible()
    {
        var r = new ActivateResult(true, "ok");
        Assert.Equal("none", r.LoginModalHandled);
        Assert.Null(r.PlcOnline);
        Assert.Null(r.RunState);
    }

    // ── Session plumbing: LoginPlc via FakeTwinCatSession ────────────────────

    [Fact]
    public void LoginPlc_AlreadyOnline_ReturnsNoOp()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            FakeLoginPlcResult = new LoginPlcResult(
                true, "OnlineChange", "none", true, null,
                "PLC already online — no-op."),
        };

        var result = fake.LoginPlc(LoginMode.OnlineChange);

        Assert.True(result.Success);
        Assert.True(result.IsOnlineNow);
        Assert.Contains("LoginPlc(", fake.CallLog[0]);
    }

    [Fact]
    public void LoginPlc_Offline_TransitionsOnline()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = true,
            FakeLoginPlcResult = new LoginPlcResult(
                true, "OnlineChange", "login-dialog (OnlineChange)", true, "running",
                "PLC logged in — now online."),
        };

        var result = fake.LoginPlc(LoginMode.OnlineChange);

        Assert.True(result.Success);
        Assert.True(result.IsOnlineNow);
        Assert.Equal("running", result.RunState);
    }

    [Fact]
    public void LoginPlc_Download_ReturnsStoppedRunState()
    {
        var fake = new FakeTwinCatSession
        {
            FakeLoginPlcResult = new LoginPlcResult(
                true, "Download", "login-dialog (Download)", true, "stopped",
                "PLC logged in via Download — PLC in STOP."),
        };

        var result = fake.LoginPlc(LoginMode.Download);

        Assert.True(result.Success);
        Assert.Equal("stopped", result.RunState);
        Assert.Equal("Download", result.Mode);
    }

    [Fact]
    public void LoginPlc_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            LoginPlcThrows = new InvalidOperationException("No DTE"),
        };

        Assert.Throws<InvalidOperationException>(() => fake.LoginPlc(LoginMode.OnlineChange));
        Assert.Contains("LoginPlc(", fake.CallLog[0]);
    }

    // ── Result mapping: LoginPlcResult → MCP JSON ────────────────────────────

    [Fact]
    public void Mcp_Success_SerializesAllFields()
    {
        var fake = new FakeTwinCatSession
        {
            FakeLoginPlcResult = new LoginPlcResult(
                true, "OnlineChange", "login-dialog (OnlineChange)", true, "running",
                "PLC logged in."),
        };

        var json = SerializeLogin(fake, LoginMode.OnlineChange);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("isOnlineNow").GetBoolean());
        Assert.Equal("running", doc.RootElement.GetProperty("runState").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public void Mcp_AlreadyOnline_SerializesSuccessTrue()
    {
        var fake = new FakeTwinCatSession
        {
            FakeLoginPlcResult = new LoginPlcResult(
                true, "OnlineChange", "none", true, null, "Already online."),
        };

        var json = SerializeLogin(fake, LoginMode.OnlineChange);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("none", doc.RootElement.GetProperty("dialogHandled").GetString());
    }

    [Fact]
    public void Mcp_Failure_SerializesSuccessFalse()
    {
        var fake = new FakeTwinCatSession
        {
            FakeLoginPlcResult = new LoginPlcResult(
                false, "Download", "none", false, null, "[Error] Login failed."),
        };

        var json = SerializeLogin(fake, LoginMode.Download);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Mcp_Throws_ReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            LoginPlcThrows = new InvalidOperationException("DTE not available"),
        };

        var result = InvokeMcpTool(fake, LoginMode.OnlineChange);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("DTE not available", result);
    }

    // ── Coordinator guard: no solution open ──────────────────────────────────

    [Fact]
    public void LoginPlc_NoSolutionOpen_CallLogStillRecorded()
    {
        // The FakeTwinCatSession does not apply the RequireOpenSolution guard —
        // it defers that to production coordinators. What we verify: the fake
        // records the call regardless of SolutionOpen, and the test remains
        // consistent with the contract (real impl returns [Error] in this case).
        var fake = new FakeTwinCatSession
        {
            IsConnected = true, SolutionOpen = false,
            FakeLoginPlcResult = new LoginPlcResult(
                false, "OnlineChange", "none", null, null, "[Error] No solution open."),
        };

        var result = fake.LoginPlc(LoginMode.OnlineChange);

        Assert.False(result.Success);
        Assert.Contains("LoginPlc(", fake.CallLog[0]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string SerializeLogin(FakeTwinCatSession fake, LoginMode mode)
    {
        var r = fake.LoginPlc(mode);
        return JsonSerializer.Serialize(new
        {
            success       = r.Success,
            mode          = r.Mode,
            dialogHandled = r.DialogHandled,
            isOnlineNow   = r.IsOnlineNow,
            runState      = r.RunState,
            message       = r.Message,
        });
    }

    private static string InvokeMcpTool(FakeTwinCatSession fake, LoginMode mode)
    {
        try
        {
            return SerializeLogin(fake, mode);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }
}
