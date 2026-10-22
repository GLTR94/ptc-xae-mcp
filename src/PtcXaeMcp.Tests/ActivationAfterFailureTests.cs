using System.Runtime.InteropServices;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #230: dialogs the XAE raises after a failed activation are declined, never confirmed — a restart
/// prompt confirmed by hand after E_ACCESSDENIED stopped the local TcSysSrv.
/// </summary>
public class ActivationAfterFailureTests
{
    private static int _next = 0x7000;
    private static XaeControl Ctl(string cls, string text) => new((IntPtr)Interlocked.Increment(ref _next), cls, text);
    private static XaeControl Button(string text) => Ctl("Button", text);
    private static XaeControl Static(string text) => Ctl("Static", text);

    private static XaeDialog Box(string title, params XaeControl[] controls) =>
        new((IntPtr)Interlocked.Increment(ref _next), 4242, (IntPtr)0x30236, "#32770", title, controls);

    [Fact]
    public void RestartPrompt_IsCancelled_NotConfirmed()
    {
        var cancel = Button("Abbrechen");
        var box = Box("TcXaeShell", Static("Neustart TwinCAT System im Run Modus"), Button("OK"), cancel);

        var answer = Win32DialogAutomation.AfterFailureAnswer(box);

        Assert.NotNull(answer);
        Assert.Equal(cancel.Hwnd, answer.Value.Plan.Button);
        Assert.Equal(XaeDialogs.IDCANCEL, answer.Value.Plan.FallbackCommandId);
    }

    [Fact]
    public void TrialLicencePrompt_IsDeclined()
    {
        var no = Button("Nein");
        var box = Box("TcXaeShell", Static("Some required runtime licenses missing. Generate trial licenses?"),
            Button("Ja"), no, Button("Abbrechen"));

        var answer = Win32DialogAutomation.AfterFailureAnswer(box);

        Assert.NotNull(answer);
        Assert.Equal(no.Hwnd, answer.Value.Plan.Button);
    }

    [Fact]
    public void SecurityCodeDialog_IsCancelled()
    {
        var cancel = Button("Abbrechen");
        var box = Box("Sicherheitscode eingeben", Static("Geben Sie die folgenden 5 Zeichen ein"), Button("OK"), cancel);

        var answer = Win32DialogAutomation.AfterFailureAnswer(box);

        Assert.NotNull(answer);
        Assert.Equal(cancel.Hwnd, answer.Value.Plan.Button);
    }

    [Fact]
    public void FatalErrorBox_AndUnrelatedBoxes_AreLeftAlone()
    {
        Assert.Null(Win32DialogAutomation.AfterFailureAnswer(
            Box("TcXaeShell", Static("Zielsystem meldet fatalen Fehler. Neustart erforderlich."), Button("OK"))));
        Assert.Null(Win32DialogAutomation.AfterFailureAnswer(
            Box("TcXaeShell", Static("Sollen die Änderungen gespeichert werden?"), Button("Ja"), Button("Nein"))));
    }

    [Fact]
    public void AccessDenied_GetsAnActionableHint()
    {
        var com = new COMException("Zugriff verweigert", unchecked((int)0x80070005));

        string hint = TwinCatComSession.AccessDeniedHint(com);

        Assert.Contains("administrator", hint, StringComparison.Ordinal);
        Assert.Contains("TcSysSrv", hint, StringComparison.Ordinal);
        Assert.Equal("", TwinCatComSession.AccessDeniedHint(new InvalidOperationException("other")));
    }
}
