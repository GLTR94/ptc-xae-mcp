using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #150 C-1…C-4, C-7, C-17, C-18: dialog automation matches only XAE windows (never the
/// host's own GUI, which shows "Testlizenzen generieren", ".tclrs" log lines and "Neustart
/// bestätigen"), presses the located button, and never turns online change into a download
/// without an explicit opt-in.
/// </summary>
public class XaeDialogScopeTests
{
    private const uint XaePid = 8768;
    private const uint HostPid = 4304;
    private const uint OtherPid = 12716;
    private static readonly IntPtr XaeMain = (IntPtr)0x30236;
    private static readonly XaeWindowScope Scope = new(new HashSet<uint> { XaePid }, XaeMain, HostPid);

    private const string WinFormsWindow = "WindowsForms10.Window.8.app.0.be6c52_r3_ad1";
    private const string WinFormsButton = "WindowsForms10.BUTTON.app.0.be6c52_r3_ad1";
    private const string WinFormsEdit = "WindowsForms10.EDIT.app.0.be6c52_r3_ad1";

    private static int _next = 0x1000;
    private static XaeControl Ctl(string cls, string text, bool enabled = true) => new((IntPtr)(++_next), cls, text, enabled);
    private static XaeControl Button(string text, bool enabled = true) => Ctl("Button", text, enabled);
    private static XaeControl Static(string text) => Ctl("Static", text);

    private static XaeDialog Win(uint pid, string cls, string title, IntPtr owner, params XaeControl[] controls) =>
        new((IntPtr)(++_next), pid, owner, cls, title, controls);

    // The host's own main window as it looks with the Build tab open (Form1.Designer.cs) and a
    // licence file path in its log box — the exact texts that used to be matched.
    private static XaeDialog HostMainWindow() => Win(HostPid, WinFormsWindow, "Pneutronic XAE MCP", IntPtr.Zero,
        Ctl(WinFormsButton, "Neustart bestätigen"),
        Ctl(WinFormsButton, "Testlizenzen generieren"),
        Ctl(WinFormsButton, "Lizenz-Manager öffnen + Testlizenz anfordern"),
        Ctl(WinFormsButton, "Testlizenz-Flow ausführen"),
        Ctl(WinFormsButton, "Konfiguration aktivieren"),
        Ctl(WinFormsEdit, "7 Tages Lizenzdatei erzeugt 'C:\\Temp\\TrialLicense.tclrs'\r\nSecurity dialog dismissed."));

    // The host's own message box (MessageBox.Show from the GUI) — a #32770 of the host process.
    private static XaeDialog HostMessageBox() => Win(HostPid, "#32770", "Pneutronic XAE MCP", IntPtr.Zero,
        Static("Neustart TwinCAT System im Run Modus? Testlizenz erzeugen? Bestehende Applikation überschreiben?"),
        Button("OK"), Button("Ja"), Button("Nein"));

    private static XaeDialog XaeTrialPrompt() => Win(XaePid, "#32770", "TwinCAT XAE", XaeMain,
        Static("Einige benötigte Laufzeit-Lizenzen fehlen. Sollen Testlizenzen erzeugt werden?"),
        Button("&Ja"), Button("&Nein"));

    private static List<XaeDialog> Desktop(params XaeDialog[] xae) =>
        new[] { HostMainWindow(), HostMessageBox() }.Concat(xae).ToList();

    // ---- scope rule ---------------------------------------------------------------

    [Fact]
    public void Scope_XaeProcessWindow_IsIncluded() => Assert.True(Scope.Includes(XaePid, IntPtr.Zero));

    [Fact]
    public void Scope_ForeignProcessOwnedByXaeMain_IsIncluded() => Assert.True(Scope.Includes(OtherPid, XaeMain));

    [Fact]
    public void Scope_ForeignProcess_IsExcluded() => Assert.False(Scope.Includes(OtherPid, IntPtr.Zero));

    [Fact]
    public void Scope_HostProcess_IsNeverIncluded_EvenWhenOwnedByXaeOrListedAsXae()
    {
        var polluted = new XaeWindowScope(new HashSet<uint> { XaePid, HostPid }, XaeMain, HostPid);
        Assert.False(polluted.Includes(HostPid, XaeMain));
        Assert.False(polluted.Includes(HostPid, IntPtr.Zero));
    }

    [Fact]
    public void Scope_UnknownPid_IsExcluded() => Assert.False(Scope.Includes(0, XaeMain));

    // ---- C-1: trial-licence prompt ------------------------------------------------

    [Fact]
    public void TrialPrompt_HostWindowsWithTestlizenzTexts_AreNeverMatched()
    {
        var hits = XaeDialogs.InScope(Desktop(), Scope, Win32DialogAutomation.IsTrialLicensePrompt).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void TrialPrompt_OnlyTheXaeDialogIsMatched()
    {
        var prompt = XaeTrialPrompt();
        var hits = XaeDialogs.InScope(Desktop(prompt), Scope, Win32DialogAutomation.IsTrialLicensePrompt).ToList();
        Assert.Equal(prompt.Hwnd, Assert.Single(hits).Hwnd);
    }

    [Fact]
    public void TrialPrompt_HostMainWindowWouldMatchTheTextRule_ScopeIsWhatKeepsItOut()
    {
        // Guard: the matcher alone does fire on the host's texts — the scope is the protection.
        Assert.True(Win32DialogAutomation.IsTrialLicensePrompt(HostMessageBox()));
        Assert.False(Scope.Includes(HostMessageBox()));
    }

    [Fact]
    public void TrialPrompt_SecurityCodeDialogIsNotAPrompt()
    {
        var sec = Win(XaePid, "#32770", "Sicherheitscode eingeben", XaeMain,
            Static("Um eine Testlizenz zu aktivieren, geben Sie bitte den folgenden Code ein:"), Button("OK"));
        Assert.False(Win32DialogAutomation.IsTrialLicensePrompt(sec));
        Assert.True(Win32DialogAutomation.IsSecurityCodeDialog(sec));
    }

    // ---- C-2: licence-created popup --------------------------------------------------

    [Fact]
    public void LicenseCreated_HostLogWithTclrsPath_IsNeverMatched()
    {
        var hits = XaeDialogs.InScope(Desktop(), Scope, Win32DialogAutomation.IsLicenseCreatedDialog).ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void LicenseCreated_XaePopup_IsMatched_AndPathExtracted()
    {
        var popup = Win(XaePid, "#32770", "TwinCAT XAE", XaeMain,
            Static("7 Tages Lizenzdatei erzeugt"), Static("'C:\\TwinCAT\\3.1\\Target\\License\\TrialLicense.tclrs'"), Button("OK"));
        var hit = Assert.Single(XaeDialogs.InScope(Desktop(popup), Scope, Win32DialogAutomation.IsLicenseCreatedDialog));
        Assert.Equal("C:\\TwinCAT\\3.1\\Target\\License\\TrialLicense.tclrs", Win32DialogAutomation.ExtractLicenseFilePath(hit.Texts));
    }

    // ---- C-3 / C-4: login confirmations and restart ----------------------------------

    [Fact]
    public void OverwriteConfirm_OfAnotherProcess_IsNeverMatched()
    {
        var foreign = Win(OtherPid, "#32770", "Word", IntPtr.Zero, Static("Möchten Sie die Datei überschreiben?"), Button("Ja"), Button("Nein"));
        Assert.Empty(XaeDialogs.InScope(new[] { foreign, HostMessageBox() }, Scope, Win32DialogAutomation.IsOverwriteRunningConfirm));
    }

    [Fact]
    public void RestartDialog_OnlyXaeMessageBoxes()
    {
        var foreign = Win(OtherPid, "#32770", "Windows Update", IntPtr.Zero, Static("Restart required"), Button("OK"));
        var xae = Win(XaePid, "#32770", "TwinCAT XAE", XaeMain, Static("Neustart TwinCAT System im Run Modus"), Button("OK"), Button("Abbrechen"));
        var hit = Assert.Single(XaeDialogs.InScope(Desktop(foreign, xae), Scope, Win32DialogAutomation.IsRestartDialog));
        Assert.Equal(xae.Hwnd, hit.Hwnd);
    }

    [Fact]
    public void DownloadConfirmation_OnlyForARealDownload()
    {
        (LoginMode Mode, bool AllowFallback, bool Expected)[] cases =
        [
            (LoginMode.Download, false, true),
            (LoginMode.OnlineChange, false, false),
            (LoginMode.OnlineChange, true, true),
            (LoginMode.LoginWithoutChange, false, false),
            (LoginMode.LoginWithoutChange, true, false),
        ];
        foreach (var (mode, allow, expected) in cases)
            Assert.True(expected == Win32DialogAutomation.MayConfirmDownload(mode, allow), $"{mode}, fallback={allow}");
    }

    // ---- C-17: online change never silently becomes a download -----------------------

    [Fact]
    public void OnlineChangeUnavailable_NoFallback_IsRefused() =>
        Assert.Null(Win32DialogAutomation.DecideLoginMode(LoginMode.OnlineChange, onlineChangeAvailable: false, allowDownloadFallback: false));

    [Fact]
    public void OnlineChangeUnavailable_FallbackAllowed_IsDownload() =>
        Assert.Equal(LoginMode.Download, Win32DialogAutomation.DecideLoginMode(LoginMode.OnlineChange, false, true));

    [Fact]
    public void OnlineChangeAvailable_StaysOnlineChange() =>
        Assert.Equal(LoginMode.OnlineChange, Win32DialogAutomation.DecideLoginMode(LoginMode.OnlineChange, true, false));

    [Fact]
    public void OtherModes_AreNeverChanged()
    {
        foreach (var mode in new[] { LoginMode.Download, LoginMode.LoginWithoutChange })
            Assert.Equal(mode, Win32DialogAutomation.DecideLoginMode(mode, onlineChangeAvailable: false, allowDownloadFallback: true));
    }

    // ---- C-7: the located button is clicked ------------------------------------------

    [Fact]
    public void PlanClick_LocatedButton_IsBmClickOnThatControl()
    {
        var ja = Button("&Ja");
        var d = Win(XaePid, "#32770", "TwinCAT XAE", XaeMain, Static("Ja oder Nein?"), ja, Button("&Nein"));
        var plan = XaeDialogs.PlanClick(d, XaeDialogs.IDYES, "Ja", "Yes");
        Assert.True(plan.UsesButton);
        Assert.Equal(ja.Hwnd, plan.Button);
    }

    [Fact]
    public void PlanClick_StaticTextWithTheLabel_IsNotAButton()
    {
        var d = Win(XaePid, "#32770", "TwinCAT XAE", XaeMain, Static("OK"), Button("Abbrechen"));
        var plan = XaeDialogs.PlanClick(d, XaeDialogs.IDOK, "OK");
        Assert.False(plan.UsesButton);
        Assert.Equal(XaeDialogs.IDOK, plan.FallbackCommandId);
    }

    [Fact]
    public void PlanClick_WinFormsButtonClass_IsFound()
    {
        var ok = Ctl(WinFormsButton, "OK");
        var d = Win(XaePid, WinFormsWindow, "TwinCAT PLC Control", XaeMain, ok);
        Assert.Equal(ok.Hwnd, XaeDialogs.PlanClick(d, XaeDialogs.IDOK, "OK").Button);
    }

    [Fact]
    public void PlanClick_NoButton_FallsBackToWmCommand()
    {
        var d = Win(XaePid, "#32770", "TwinCAT XAE", XaeMain, Static("Frage"));
        var plan = XaeDialogs.PlanClick(d, XaeDialogs.IDYES, "Ja", "Yes");
        Assert.False(plan.UsesButton);
        Assert.Equal(XaeDialogs.IDYES, plan.FallbackCommandId);
        Assert.Contains("WM_COMMAND", plan.Description);
    }

    // ---- C-18: security code only from labels ----------------------------------------

    [Fact]
    public void SecurityCode_ButtonCaptionHilfe_IsNotACode()
    {
        var d = Win(XaePid, "#32770", "Sicherheitscode eingeben", XaeMain,
            Static("Bitte Code eingeben:"), Ctl("Edit", ""), Button("Hilfe"), Button("OK"));
        Assert.Null(Win32DialogAutomation.ExtractSecurityCode(d));
    }

    [Fact]
    public void SecurityCode_FromTheDisplayControl_NotFromTheInputField()
    {
        var d = Win(XaePid, "#32770", "Sicherheitscode eingeben", XaeMain,
            Ctl("Edit", "bW5Rm"), Ctl("Edit", "XXXXX"), Button("OK"));
        Assert.Equal("bW5Rm", Win32DialogAutomation.ExtractSecurityCode(d));
    }

    [Fact]
    public void SecurityCode_StoplistedWords_AreNotCodes() =>
        Assert.Null(Win32DialogAutomation.ExtractSecurityCode(new[] { "Hilfe", "Close", "Apply" }));

    [Fact]
    public void SecurityCodeInput_IsTheSecondEdit()
    {
        var first = Ctl("Edit", "bW5Rm");
        var second = Ctl("Edit", "");
        var d = Win(XaePid, "#32770", "Sicherheitscode eingeben", XaeMain, first, second, Button("OK"));
        Assert.Equal(second.Hwnd, Win32DialogAutomation.SecurityCodeInput(d)!.Hwnd);
    }
}
