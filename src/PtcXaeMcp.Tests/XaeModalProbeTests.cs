using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #149: the blocking-window matcher (owned windows of any class).</summary>
public class XaeModalProbeTests
{
    private const uint Xae = 8768;
    private const uint Self = 192;
    private static readonly IReadOnlySet<uint> XaePids = new HashSet<uint> { Xae };
    private static readonly IntPtr Main = (IntPtr)197174;

    private static XaeWindowInfo Win(
        uint pid = Xae, string cls = "WindowsForms10.Window.8.app.0.18feff0_r14_ad1", string title = "Gehe zu Zeile",
        bool visible = true, bool enabled = true, IntPtr? owner = null, bool ownerEnabled = false,
        params string[] texts) =>
        new((IntPtr)13764284, pid, cls, title, visible, enabled, owner ?? Main, ownerEnabled, texts);

    [Fact]
    public void OrphanedGoToDialog_AnyClassOwnedByDisabledXaeMain_IsBlocking() =>
        // Shape observed live 2026-09-26 (WinForms window, owner = disabled XAE main window).
        Assert.True(XaeModalProbe.IsBlocking(Win(), XaePids, Self));

    [Fact]
    public void OwnedToolWindow_OwnerEnabled_IsNotBlocking() =>
        Assert.False(XaeModalProbe.IsBlocking(Win(ownerEnabled: true), XaePids, Self));

    [Fact]
    public void InvisibleWindow_IsNotBlocking() =>
        Assert.False(XaeModalProbe.IsBlocking(Win(visible: false), XaePids, Self));

    [Fact]
    public void WindowOfAnotherProcess_IsNotBlocking() =>
        Assert.False(XaeModalProbe.IsBlocking(Win(pid: 4242), XaePids, Self));

    [Fact]
    public void WindowOfTheHostItself_IsNeverBlocking()
    {
        // Even if the host's pid were (wrongly) in the XAE set, its own GUI never counts.
        var pids = new HashSet<uint> { Xae, Self };
        Assert.False(XaeModalProbe.IsBlocking(Win(pid: Self), pids, Self));
    }

    [Fact]
    public void OwnerlessMessageBox_IsBlocking() =>
        Assert.True(XaeModalProbe.IsBlocking(Win(cls: "#32770", owner: IntPtr.Zero), XaePids, Self));

    [Fact]
    public void OwnerlessMainWindow_IsNotBlocking() =>
        Assert.False(XaeModalProbe.IsBlocking(Win(cls: "HwndWrapper[DefaultDomain;;x]", title: "Fixture - TcXaeShell",
            owner: IntPtr.Zero), XaePids, Self));

    [Fact]
    public void Select_PrefersTheEnabledTopOfAModalChain()
    {
        var parent = Win(title: "Optionen", enabled: false);
        var child = Win(title: "Bestätigen", cls: "#32770", enabled: true, texts: ["Wirklich?", "&Ja"]);
        Assert.Equal("Bestätigen: Wirklich? | &Ja", XaeModalProbe.Select([parent, child], XaePids, Self));
    }

    [Fact]
    public void Select_NoBlockingWindow_IsNull() =>
        Assert.Null(XaeModalProbe.Select([Win(ownerEnabled: true)], XaePids, Self));

    [Fact]
    public void Describe_UntitledWindow_NamesItsClass() =>
        Assert.Equal("<untitled #32770>", XaeModalProbe.Describe(Win(cls: "#32770", title: "")));
}
