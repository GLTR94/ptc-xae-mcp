using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class BuildCoordinatorTests
{
    // ── BuildSolution ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildSolution_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("BuildSolution", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void BuildSolution_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("BuildSolution", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void BuildSolution_HappyPath_NoErrors_ReturnsSuccess()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeBuildSolutionResult = new BuildResult(true, 0, Array.Empty<BuildError>()),
        };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), $"Expected success but got: {result}");
        Assert.Contains("Build succeeded", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[ERROR]", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSolution_WithCompilerErrors_ReturnsErrorList()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeBuildSolutionResult = new BuildResult(
                false, 1,
                [new BuildError("error", "Undeclared identifier 'x'", "MAIN", 5)]),
        };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.Contains("Build failed", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Undeclared identifier 'x'", result);
        Assert.Contains("BuildSolution()", fake.CallLog);
    }

    [Fact]
    public void BuildSolution_WithWarningsOnly_StillSucceeds()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeBuildSolutionResult = new BuildResult(
                true, 0,
                [new BuildError("warning", "Unused variable 'y'", null, null)]),
        };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), $"Expected success but got: {result}");
        Assert.Contains("Build succeeded", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unused variable 'y'", result);
    }

    [Fact]
    public void BuildSolution_SessionThrows_WrappedAsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            BuildThrows = new InvalidOperationException("Build.Cancel timed out"),
        };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Build.Cancel timed out", result);
    }

    [Fact]
    public void BuildSolution_ComException_SurfacedAsStructuredError()
    {
        var comEx = new System.Runtime.InteropServices.COMException(
            "The RPC server is unavailable. (Exception from HRESULT: 0x800706BA)", unchecked((int)0x800706BA));
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            BuildThrows = comEx,
        };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.StartsWith("[Error]", result);
        Assert.True(result.Length > 10, $"Error response must not be empty: '{result}'");
        Assert.Contains("0x800706BA", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSolution_WithLog_AppendsLoggerOutputSection()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeBuildSolutionResult = new BuildResult(
                true, 0, Array.Empty<BuildError>(),
                "[MSG] TwinCAT start\n[WARN] MAIN:5,2 [PlcProj] Unused variable 'y'"),
        };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.Contains("Build succeeded", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("---- Logger output ----", result);
        Assert.Contains("[MSG] TwinCAT start", result);
        Assert.Contains("[WARN] MAIN:5,2 [PlcProj] Unused variable 'y'", result);
    }

    [Fact]
    public void BuildSolution_EmptyLog_OmitsLoggerOutputSection()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeBuildSolutionResult = new BuildResult(true, 0, Array.Empty<BuildError>()),
        };

        var result = new BuildCoordinator(fake).BuildSolution();

        Assert.DoesNotContain("Logger output", result);
    }

    // ── BuildPlc ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuildPlc_HappyPath_ReturnsSuccess()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeBuildPlcResult = new BuildResult(true, 0, Array.Empty<BuildError>()),
        };

        var result = new BuildCoordinator(fake).BuildPlc(null);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), $"Expected success but got: {result}");
        Assert.Contains("Build succeeded", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlc_WithErrors_ReturnsErrorList()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeBuildPlcResult = new BuildResult(
                false, 1,
                [
                    new BuildError("error", "Type mismatch", "FB_X", 10),
                    new BuildError("error", "Undeclared variable 'z'", "MAIN", 3),
                ]),
        };

        var result = new BuildCoordinator(fake).BuildPlc(null);

        Assert.Contains("Type mismatch", result);
        Assert.Contains("Undeclared variable 'z'", result);
    }

    [Fact]
    public void BuildPlc_ExplicitProjectName_PassedThrough()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        new BuildCoordinator(fake).BuildPlc("MyPlc");

        Assert.Contains("BuildPlc(MyPlc)", fake.CallLog);
    }

    [Fact]
    public void BuildPlc_DefaultProjectName_AutoDetected()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        new BuildCoordinator(fake).BuildPlc(null);

        Assert.Contains(fake.CallLog, entry => entry.StartsWith("BuildPlc(", StringComparison.Ordinal));
    }

    // ── ActivateConfig ─────────────────────────────────────────────────────────

    [Fact]
    public void ActivateConfig_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).ActivateConfig();

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("ActivateConfig", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void ActivateConfig_HappyPath_ReturnsActivated()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeActivateResult = new ActivateResult(true, "Activated successfully."),
        };

        var result = new BuildCoordinator(fake).ActivateConfig();

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), $"Expected success but got: {result}");
        Assert.Contains("Activated", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivateConfig_SessionThrows_WrappedAsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            ActivateThrows = new InvalidOperationException("ActivateConfiguration: license expired"),
        };

        var result = new BuildCoordinator(fake).ActivateConfig();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("license expired", result);
    }

    [Fact]
    public void ActivateConfig_WithFlags_PassesThroughToSession()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        new BuildCoordinator(fake).ActivateConfig(autoConfirmRestart: true, autoGenerateLicenses: true);

        Assert.Contains(fake.CallLog, s => s.StartsWith("ActivateConfig(restart=True, licenses=True", StringComparison.Ordinal));
    }

    [Fact]
    public void ActivateConfig_LoginFlags_PassThroughToSession()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        new BuildCoordinator(fake).ActivateConfig(
            autoConfirmLogin: true, loginMode: LoginMode.Download, updateBootProject: false);

        Assert.Contains(fake.CallLog, s =>
            s.Contains("login=True") && s.Contains("mode=Download") && s.Contains("updateBoot=False"));
    }

    // Issue #195: the wait for Run after the restart is bounded; the tool's runTimeoutSeconds is clamped.
    [Theory]
    [InlineData(null, "runTimeout=60)")]
    [InlineData(0, "runTimeout=60)")]
    [InlineData(2, "runTimeout=5)")]
    [InlineData(60, "runTimeout=60)")]
    [InlineData(900, "runTimeout=300)")]
    public void ActivateConfig_RunTimeout_IsClampedAndPassedToSession(int? seconds, string expected)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };

        new BuildCoordinator(fake).ActivateConfig(autoConfirmRestart: true, runTimeoutSeconds: seconds);

        Assert.Contains(fake.CallLog, s => s.StartsWith("ActivateConfig(restart=True", StringComparison.Ordinal) &&
                                            s.EndsWith(expected, StringComparison.Ordinal));
    }

    // Issue #195: a box the activation closed is an activation failure that carries the box text.
    [Fact]
    public void ActivateConfig_ClosedFatalBox_IsAnActivationFailedError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeActivateResult = new ActivateResult(false,
                "TcXaeShell: Zielsystem meldet fatalen Fehler (the XAE box was closed). Target 127.0.0.1.1.1 (local) last read: Config. " +
                "XAE dialogs answered: none."),
        };

        var result = new BuildCoordinator(fake).ActivateConfig(autoConfirmRestart: true);

        Assert.StartsWith("[Error] Activation failed: TcXaeShell: Zielsystem meldet fatalen Fehler (the XAE box was closed).", result,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Activated:", result, StringComparison.Ordinal);
    }

    // ── GetLicenseInfo ─────────────────────────────────────────────────────

    [Fact]
    public void GetLicenseInfo_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).GetLicenseInfo();

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("GetLicenseInfo", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void GetLicenseInfo_HappyPath_ReturnsFormattedInfo()
    {
        var entries = new[]
        {
            new LicenseEntry("TC1200", "TwinCAT PLC Runtime", LicenseStatus.Trial, new DateTime(2026, 12, 31)),
        };
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeLicenseInfoResult = new LicenseInfoResult(entries, "<xml/>"),
        };

        var result = new BuildCoordinator(fake).GetLicenseInfo();

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("TwinCAT PLC Runtime", result);
        Assert.Contains("Trial", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetLicenseInfo()", fake.CallLog);
    }

    [Fact]
    public void GetLicenseInfo_SessionThrows_WrappedAsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            GetLicenseInfoThrows = new InvalidOperationException("TIRC^License not found"),
        };

        var result = new BuildCoordinator(fake).GetLicenseInfo();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("TIRC^License not found", result);
    }

    // ── DismissSecurityDialog ──────────────────────────────────────────────

    [Fact]
    public void DismissSecurityDialog_HappyPath_ReturnsResult()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeDismissSecurityDialogResult = "Security dialog dismissed.",
        };

        var result = new BuildCoordinator(fake).DismissSecurityDialog();

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("DismissSecurityDialog()", fake.CallLog);
    }

    [Fact]
    public void DismissSecurityDialog_SessionThrows_WrappedAsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            DismissSecurityDialogThrows = new InvalidOperationException("Win32 access denied"),
        };

        var result = new BuildCoordinator(fake).DismissSecurityDialog();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Win32 access denied", result);
    }

    // ── ListRequiredLicenses ───────────────────────────────────────────────

    [Fact]
    public void ListRequiredLicenses_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).ListRequiredLicenses();

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("ListRequiredLicenses", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void ListRequiredLicenses_EmptyList_ReportsNone()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeRequiredLicensesResult = new RequiredLicensesResult(
                Array.Empty<RequiredLicense>(), "<LicenseDef/>"),
        };

        var result = new BuildCoordinator(fake).ListRequiredLicenses();

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("none", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListRequiredLicenses()", fake.CallLog);
    }

    [Fact]
    public void ListRequiredLicenses_WithEntries_FormatsAll()
    {
        var entries = new[]
        {
            new RequiredLicense("{854588BA-345B-43F2-9354-0CF7AFF76EA3}", "SAMPLE-001", "PTC Library OEM"),
        };
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeRequiredLicensesResult = new RequiredLicensesResult(entries, "<LicenseDef/>"),
        };

        var result = new BuildCoordinator(fake).ListRequiredLicenses();

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("854588BA", result);
        Assert.Contains("SAMPLE-001", result);
    }

    [Fact]
    public void ListRequiredLicenses_SessionThrows_WrappedAsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            ListRequiredLicensesThrows = new InvalidOperationException("TIRC^License not found"),
        };

        var result = new BuildCoordinator(fake).ListRequiredLicenses();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("TIRC^License not found", result);
    }

    // ── AddRequiredLicense ─────────────────────────────────────────────────

    [Fact]
    public void AddRequiredLicense_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).AddRequiredLicense("{GUID}", null);

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("AddRequiredLicense", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void AddRequiredLicense_HappyPath_ReturnsSuccessWithList()
    {
        var added = new[] { new RequiredLicense("{854588BA-345B-43F2-9354-0CF7AFF76EA3}", "SAMPLE-001", null) };
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeAddRequiredLicenseResult = new AddRequiredLicenseResult(
                Added: true, WasAlreadyPresent: false,
                RequiredLicenses: added,
                Message: "License added to required list (1 total)."),
        };

        var result = new BuildCoordinator(fake).AddRequiredLicense("{854588BA-345B-43F2-9354-0CF7AFF76EA3}", "SAMPLE-001");

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("added", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SAMPLE-001", result);
        Assert.Contains("AddRequiredLicense(", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void AddRequiredLicense_AlreadyPresent_ReturnsSuccessNoChange()
    {
        var existing = new[] { new RequiredLicense("{854588BA-345B-43F2-9354-0CF7AFF76EA3}", "SAMPLE-001", null) };
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeAddRequiredLicenseResult = new AddRequiredLicenseResult(
                Added: false, WasAlreadyPresent: true,
                RequiredLicenses: existing,
                Message: "License already present in required list (no change)."),
        };

        var result = new BuildCoordinator(fake).AddRequiredLicense("{854588BA-345B-43F2-9354-0CF7AFF76EA3}", null);

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("already", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddRequiredLicense_SessionThrows_WrappedAsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            AddRequiredLicenseThrows = new InvalidOperationException("ConsumeXml failed"),
        };

        var result = new BuildCoordinator(fake).AddRequiredLicense("{854588BA-345B-43F2-9354-0CF7AFF76EA3}", null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("ConsumeXml failed", result);
    }

    // ── RemoveRequiredLicense ──────────────────────────────────────────────

    [Fact]
    public void RemoveRequiredLicense_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };

        var result = new BuildCoordinator(fake).RemoveRequiredLicense("{GUID}", null);

        Assert.StartsWith("[Error]", result);
        Assert.DoesNotContain("RemoveRequiredLicense", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void RemoveRequiredLicense_HappyPath_ReturnsRemovedMessage()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeRemoveRequiredLicenseResult = new RemoveRequiredLicenseResult(
                Removed: true, WasAbsent: false,
                RequiredLicenses: Array.Empty<RequiredLicense>(),
                Message: "License removed from required list (1 removed, 0 remaining)."),
        };

        var result = new BuildCoordinator(fake).RemoveRequiredLicense("{854588BA-345B-43F2-9354-0CF7AFF76EA3}", null);

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("removed", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveRequiredLicense_WasAbsent_ReturnsSuccessNoChange()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeRemoveRequiredLicenseResult = new RemoveRequiredLicenseResult(
                Removed: false, WasAbsent: true,
                RequiredLicenses: Array.Empty<RequiredLicense>(),
                Message: "License not found in required list (no change)."),
        };

        var result = new BuildCoordinator(fake).RemoveRequiredLicense("SAMPLE-001", null);

        Assert.DoesNotContain("[Error]", result);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveRequiredLicense_SessionThrows_WrappedAsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            RemoveRequiredLicenseThrows = new InvalidOperationException("ConsumeXml failed"),
        };

        var result = new BuildCoordinator(fake).RemoveRequiredLicense("{GUID}", null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("ConsumeXml failed", result);
    }
}
