using System.Runtime.InteropServices;
using System.Text;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>Snapshot of one top-level window, as far as the blocking-window matcher needs it.</summary>
internal sealed record XaeWindowInfo(
    IntPtr Hwnd,
    uint Pid,
    string ClassName,
    string Title,
    bool Visible,
    bool Enabled,
    IntPtr Owner,
    bool OwnerEnabled,
    IReadOnlyList<string> ChildTexts);

/// <summary>
/// Finds the window that holds a TcXaeShell modal — Win32 only, no COM, so it works exactly
/// when COM does not. Two shapes count (issue #149):
/// <list type="bullet">
/// <item>a visible window <b>of any class</b> of a TcXaeShell process whose owner is disabled —
/// how Win32 modality looks from outside. The orphaned Go-To dialog of #150 is such a window
/// (<c>WindowsForms10.Window…</c>, owner = XAE main window, owner disabled; verified live
/// 2026-09-26) and was invisible to a <c>#32770</c>-only search;</item>
/// <item>a visible ownerless message box (<c>#32770</c>) of a TcXaeShell process.</item>
/// </list>
/// Never matches windows of the current process (the host's own GUI shows XAE-like texts).
/// Detection only: nothing here clicks or closes anything.
/// </summary>
internal static class XaeModalProbe
{
    private const string XaeProcessName = "TcXaeShell";
    private const uint GW_OWNER = 4;
    private const uint WM_GETTEXT = 0x000D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc fn, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc fn, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int n);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wp, StringBuilder lp, uint flags, uint timeoutMs, out IntPtr result);

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lp);

    /// <summary>
    /// The one scope rule every XAE window matcher uses (issues #149/#150): a window belongs to the
    /// XAE when its process is one of <paramref name="xaePids"/> or its owner is the attached DTE
    /// main window <paramref name="xaeMain"/> — and never when it belongs to the current process
    /// (<paramref name="selfPid"/>), whose GUI shows XAE-like texts. Pure — unit-tested.
    /// </summary>
    internal static bool InScope(uint pid, IntPtr owner, IReadOnlySet<uint> xaePids, IntPtr xaeMain, uint selfPid)
    {
        if (pid == 0 || pid == selfPid) return false;
        if (xaePids.Contains(pid)) return true;
        return xaeMain != IntPtr.Zero && owner == xaeMain;
    }

    /// <summary>Pure matcher — unit-tested.</summary>
    internal static bool IsBlocking(XaeWindowInfo w, IReadOnlySet<uint> xaePids, uint selfPid)
    {
        if (!w.Visible || !InScope(w.Pid, IntPtr.Zero, xaePids, IntPtr.Zero, selfPid)) return false;
        if (w.Owner != IntPtr.Zero) return !w.OwnerEnabled;
        return string.Equals(w.ClassName, "#32770", StringComparison.Ordinal);
    }

    /// <summary>
    /// Pure selection among candidates: the enabled (= top-most in a modal chain) blocking window
    /// wins over a disabled parent dialog. Returns its description or null.
    /// </summary>
    internal static string? Select(IEnumerable<XaeWindowInfo> windows, IReadOnlySet<uint> xaePids, uint selfPid)
    {
        var blocking = windows.Where(w => IsBlocking(w, xaePids, selfPid)).ToList();
        var pick = blocking.FirstOrDefault(w => w.Enabled) ?? blocking.FirstOrDefault();
        return pick is null ? null : Describe(pick);
    }

    /// <summary>"Title" or, for message boxes, "Title: text | text".</summary>
    internal static string Describe(XaeWindowInfo w)
    {
        string title = string.IsNullOrWhiteSpace(w.Title) ? $"<untitled {w.ClassName}>" : w.Title.Trim();
        var texts = w.ChildTexts
            .Select(t => string.Join(" ", t.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())))
            .Where(t => t.Length > 0)
            .ToList();
        return texts.Count == 0 ? title : $"{title}: {string.Join(" | ", texts)}";
    }

    /// <summary>The blocking window of a running TcXaeShell, described, or null. Never throws.</summary>
    public static string? FindBlockingDialog()
    {
        try
        {
            var pids = XaePids();
            if (pids.Count == 0) return null;
            uint self = (uint)Environment.ProcessId;
            return Select(Snapshot(pids), pids, self);
        }
        catch (Exception)
        {
            return null; // Win32 enumeration hiccup: report "no dialog known", never mask the caller's error
        }
    }

    /// <summary>Process id of <paramref name="hwnd"/>, 0 when unknown.</summary>
    internal static uint PidOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    internal static HashSet<uint> XaePids()
    {
        var pids = new HashSet<uint>();
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(XaeProcessName))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }
        return pids;
    }

    private static List<XaeWindowInfo> Snapshot(IReadOnlySet<uint> pids)
    {
        var list = new List<XaeWindowInfo>();
        EnumWindows((hwnd, lp) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pids.Contains(pid)) return true;
            IntPtr owner = GetWindow(hwnd, GW_OWNER);
            string cls = ClassOf(hwnd);
            // Child texts only for message boxes: they carry the question; for other dialogs
            // the title is enough and cross-process text reads are not free.
            var texts = cls == "#32770" ? ChildTexts(hwnd) : (IReadOnlyList<string>)Array.Empty<string>();
            list.Add(new XaeWindowInfo(hwnd, pid, cls, TextOf(hwnd), true, IsWindowEnabled(hwnd),
                owner, owner != IntPtr.Zero && IsWindowEnabled(owner), texts));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static List<string> ChildTexts(IntPtr hwnd)
    {
        var texts = new List<string>();
        EnumChildWindows(hwnd, (child, lp) =>
        {
            var sb = new StringBuilder(512);
            // WM_GETTEXT crosses process boundaries for controls; bounded so a hung XAE cannot hang us.
            _ = SendMessageTimeout(child, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 200, out _);
            string t = sb.ToString().Trim();
            if (t.Length > 0) texts.Add(t);
            return texts.Count < 8;
        }, IntPtr.Zero);
        return texts;
    }

    private static string TextOf(IntPtr hwnd) { var sb = new StringBuilder(256); _ = GetWindowText(hwnd, sb, 256); return sb.ToString(); }
    private static string ClassOf(IntPtr hwnd) { var sb = new StringBuilder(256); _ = GetClassName(hwnd, sb, 256); return sb.ToString(); }
}
