using Microsoft.Win32;

namespace PtcXaeMcp.WinForms.TwinCat.HostSelection;

/// <summary>
/// The resolved XAE host for a project: which ProgID to attach/launch, which
/// exe to start, and the diagnostic context behind that choice.
/// </summary>
public sealed record XaeHostSelection(
    XaeHostClass HostClass,
    string ProgId,
    string ExePath,
    bool ExeFromRegistry,
    TcVersion? TcVersion);

/// <summary>
/// Single source of truth mapping a project's TwinCAT version (its
/// <see cref="XaeHostClass"/>) to the XAE Shell ProgID and exe path.
/// <para>
/// ProgIDs are the Infosys-242746251-confirmed constants. The exe path is
/// resolved from the registry (<c>HKCR\&lt;ProgID&gt;\CLSID</c> →
/// <c>HKCR\CLSID\{clsid}\LocalServer32</c>); when that entry is absent — as
/// for <c>TcXaeShell.DTE.15.0</c> on the current box — it falls back to the
/// documented well-known install path for that class.
/// </para>
/// <para>
/// <see cref="AttachProbeOrder"/> always puts the resolved ProgID first but
/// keeps the full legacy probe chain after it, so an attach is never worse
/// than today's blind-probe behaviour when no project context is available.
/// </para>
/// </summary>
public sealed class XaeHostResolver
{
    public const string ProgId64 = "TcXaeShell.DTE.17.0";
    public const string ProgId32 = "TcXaeShell.DTE.15.0";

    private const string WellKnownExe64 =
        @"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";
    private const string WellKnownExe32 =
        @"C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";

    // Legacy blind-probe order, preserved verbatim as the fallback chain.
    private static readonly string[] LegacyProbeOrder =
    [
        "TcXaeShell.DTE.17.0",
        "TcXaeShell.DTE.16.0",
        "TcXaeShell.DTE.15.0",
    ];

    private readonly Func<string, string?> _probeExeForProgId;

    /// <param name="probeExeForProgId">
    /// Resolves a ProgID to its registered exe, or null if not registered.
    /// Defaults to the real <c>HKCR</c> LocalServer32 lookup; injected in tests.
    /// </param>
    public XaeHostResolver(Func<string, string?>? probeExeForProgId = null)
    {
        _probeExeForProgId = probeExeForProgId ?? ProbeExeFromRegistry;
    }

    public XaeHostSelection Resolve(XaeHostClass hostClass, TcVersion? tcVersion = null)
    {
        var progId = hostClass == XaeHostClass.Shell64 ? ProgId64 : ProgId32;
        var wellKnown = hostClass == XaeHostClass.Shell64 ? WellKnownExe64 : WellKnownExe32;

        string? registryExe = null;
        try { registryExe = _probeExeForProgId(progId); }
        catch { /* registry hiccup → fall through to well-known */ }

        var fromRegistry = !string.IsNullOrWhiteSpace(registryExe);
        return new XaeHostSelection(
            HostClass: hostClass,
            ProgId: progId,
            ExePath: fromRegistry ? registryExe! : wellKnown,
            ExeFromRegistry: fromRegistry,
            TcVersion: tcVersion);
    }

    /// <summary>
    /// Resolves the host from a <c>.tsproj</c> XML string. On any read/parse
    /// failure returns false with a reason (caller keeps the legacy fallback).
    /// </summary>
    public bool TryResolveFromTsproj(
        string tsprojXml, out XaeHostSelection? selection, out string? error)
    {
        selection = null;
        if (!TsprojReader.TryReadTcVersion(tsprojXml, out var v, out error))
            return false;

        selection = Resolve(v!.Classify(), v);
        return true;
    }

    /// <summary>
    /// Resolves the host from a <c>.tsproj</c> file path. Same failure
    /// contract as <see cref="TryResolveFromTsproj"/>.
    /// </summary>
    public bool TryResolveFromTsprojFile(
        string tsprojPath, out XaeHostSelection? selection, out string? error)
    {
        selection = null;
        if (!TsprojReader.TryReadTcVersionFromFile(tsprojPath, out var v, out error))
            return false;

        selection = Resolve(v!.Classify(), v);
        return true;
    }

    /// <summary>
    /// ProgIDs to try when attaching, resolved class first, then the legacy
    /// chain (deduplicated, order-preserving). Used by the attach sites so a
    /// known project biases the probe without losing the safety net.
    /// </summary>
    public IReadOnlyList<string> AttachProbeOrder(XaeHostClass hostClass)
    {
        var resolved = hostClass == XaeHostClass.Shell64 ? ProgId64 : ProgId32;
        var order = new List<string> { resolved };
        foreach (var p in LegacyProbeOrder)
            if (!order.Contains(p, StringComparer.OrdinalIgnoreCase))
                order.Add(p);
        return order;
    }

    /// <summary>The unbiased legacy probe order (no project context).</summary>
    public static IReadOnlyList<string> LegacyAttachProbeOrder => LegacyProbeOrder;

    private static string? ProbeExeFromRegistry(string progId)
    {
        // HKCR\<ProgID>\CLSID -> {clsid};  HKCR\CLSID\{clsid}\LocalServer32 -> exe
        using var progKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\CLSID");
        if (progKey?.GetValue(null) is not string clsid || string.IsNullOrWhiteSpace(clsid))
            return null;

        using var ls = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\LocalServer32");
        if (ls?.GetValue(null) is not string raw || string.IsNullOrWhiteSpace(raw))
            return null;

        // LocalServer32 may be quoted and/or carry args; take the exe token.
        var path = raw.Trim();
        if (path.StartsWith('"'))
        {
            var end = path.IndexOf('"', 1);
            if (end > 0) path = path[1..end];
        }
        else
        {
            var sp = path.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
            if (sp > 0) path = path[..(sp + 4)];
        }
        return path.Length == 0 ? null : path;
    }
}
