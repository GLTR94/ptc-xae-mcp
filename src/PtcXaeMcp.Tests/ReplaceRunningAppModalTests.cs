using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #204: the "Applikation läuft gerade … muss ersetzt werden" login box (WinForms "TwinCAT PLC Control")
/// is recognised, answered Ja only for a download, and otherwise Nein with an honest refusal.
/// </summary>
public class ReplaceRunningAppModalTests
{
    private const string WinFormsWindow = "WindowsForms10.Window.8.app.0.be6c52_r3_ad1";
    private const string WinFormsButton = "WindowsForms10.BUTTON.app.0.be6c52_r3_ad1";
    private const string WinFormsStatic = "WindowsForms10.STATIC.app.0.be6c52_r3_ad1";

    private const string GermanText =
        "Warnung: Eine Applikation 'Port_851' läuft gerade auf der SPS. Da es keine passende Übersetzungsinformation gibt, " +
        "muss diese vorhandene Applikation ersetzt werden. Klicken Sie 'Ja' um den neuesten Code herunterzuladen, oder 'Nein' um abzubrechen.";

    private static int _next = 0x9000;
    private static XaeControl Ctl(string cls, string text) => new((IntPtr)Interlocked.Increment(ref _next), cls, text);

    private static XaeDialog PlcControl(string text) =>
        new((IntPtr)Interlocked.Increment(ref _next), 4242, (IntPtr)0x30236, WinFormsWindow, "TwinCAT PLC Control",
        [
            Ctl(WinFormsStatic, text),
            Ctl(WinFormsButton, "Bootapplikation aktualisieren"),
            Ctl(WinFormsButton, "Yes"),
            Ctl(WinFormsButton, "Nein"),
            Ctl(WinFormsButton, "Details..."),
        ]);

    [Theory]
    [InlineData(GermanText)]
    [InlineData("Warning: An application 'Port_851' is currently running on the PLC. As there is no matching compile information, this application must be replaced.")]
    public void Text_GermanAndEnglish_Matches(string text)
    {
        Assert.True(Win32DialogAutomation.IsReplaceRunningAppText(text));
        Assert.True(Win32DialogAutomation.IsReplaceRunningAppModal(PlcControl(text)));
    }

    [Theory]
    [InlineData("Die Applikation wurde seit dem letzten Download geändert. Was möchten Sie tun?")]
    [InlineData("Die SPS läuft gerade.")]
    [InlineData("Zielsystem meldet fatalen Fehler")]
    public void OtherTexts_DoNotMatch(string text)
    {
        Assert.False(Win32DialogAutomation.IsReplaceRunningAppText(text));
    }

    [Fact]
    public void WinFormsWindow_IsRecognised_AlthoughItIsNoMessageBox()
    {
        var d = PlcControl(GermanText);

        Assert.False(d.IsMessageBox);
        Assert.True(Win32DialogAutomation.IsReplaceRunningAppModal(d));
        Assert.False(Win32DialogAutomation.IsAppChangedLoginModal(d));
    }

    [Theory]
    [InlineData("Download", false, true)]
    [InlineData("OnlineChange", true, true)]
    [InlineData("OnlineChange", false, false)]
    [InlineData("LoginWithoutChange", true, false)]
    public void OnlyADownloadMayAnswerJa(string mode, bool fallback, bool expectJa)
    {
        Assert.Equal(expectJa, Win32DialogAutomation.MayConfirmDownload(Enum.Parse<LoginMode>(mode), fallback));
    }

    [Fact]
    public void ActivationWatcher_AnswersIt_OnlyWithAutoConfirmLogin()
    {
        var d = PlcControl(GermanText);
        var login = new Win32DialogAutomation.WatcherConfig(false, false, AutoConfirmLogin: true);
        var none = new Win32DialogAutomation.WatcherConfig(false, false);

        Assert.True(Win32DialogAutomation.Answers(login, d));
        Assert.False(Win32DialogAutomation.Answers(none, d));
    }

    [Fact]
    public void RefusalWireValue_NamesTheWayOut()
    {
        Assert.Contains("full download", Win32DialogAutomation.RefusedReplaceRunningApp, StringComparison.Ordinal);
        Assert.Contains("allowDownloadFallback=false", Win32DialogAutomation.RefusedReplaceRunningApp, StringComparison.Ordinal);
    }
}
