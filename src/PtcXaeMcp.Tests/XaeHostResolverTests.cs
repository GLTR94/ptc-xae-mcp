using PtcXaeMcp.WinForms.TwinCat.HostSelection;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Maps a project's host class to (ProgID, exePath). The registry probe is
/// injected so these stay pure (no dependency on this box's registry).
/// ProgIDs are the Infosys-confirmed constants; exe path comes from the
/// registry probe, falling back to the documented well-known path per class.
/// </summary>
public class XaeHostResolverTests
{
    // Well-known fallbacks (Infosys + empirical, recorded in the spec).
    private const string Exe64 =
        @"C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";
    private const string Exe32 =
        @"C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";

    [Fact]
    public void Shell64_UsesProgId170_AndRegistryExe()
    {
        var resolver = new XaeHostResolver(
            probeExeForProgId: id => id == "TcXaeShell.DTE.17.0" ? @"D:\custom\Tc64.exe" : null);

        var r = resolver.Resolve(XaeHostClass.Shell64);

        Assert.Equal("TcXaeShell.DTE.17.0", r.ProgId);
        Assert.Equal(@"D:\custom\Tc64.exe", r.ExePath);
        Assert.True(r.ExeFromRegistry);
    }

    [Fact]
    public void Shell32_UsesProgId150_AndRegistryExe()
    {
        var resolver = new XaeHostResolver(
            probeExeForProgId: id => id == "TcXaeShell.DTE.15.0" ? @"D:\custom\Tc32.exe" : null);

        var r = resolver.Resolve(XaeHostClass.Shell32);

        Assert.Equal("TcXaeShell.DTE.15.0", r.ProgId);
        Assert.Equal(@"D:\custom\Tc32.exe", r.ExePath);
        Assert.True(r.ExeFromRegistry);
    }

    [Fact]
    public void Shell64_NoRegistryEntry_FallsBackToWellKnown64Path()
    {
        var resolver = new XaeHostResolver(probeExeForProgId: _ => null);

        var r = resolver.Resolve(XaeHostClass.Shell64);

        Assert.Equal("TcXaeShell.DTE.17.0", r.ProgId);
        Assert.Equal(Exe64, r.ExePath);
        Assert.False(r.ExeFromRegistry);
    }

    [Fact]
    public void Shell32_NoRegistryEntry_FallsBackToWellKnown32Path()
    {
        // Mirrors this box: 15.0 class has no LocalServer32 but the exe exists.
        var resolver = new XaeHostResolver(probeExeForProgId: _ => null);

        var r = resolver.Resolve(XaeHostClass.Shell32);

        Assert.Equal("TcXaeShell.DTE.15.0", r.ProgId);
        Assert.Equal(Exe32, r.ExePath);
        Assert.False(r.ExeFromRegistry);
    }

    [Fact]
    public void ResolveFromTsproj_4026_PicksShell64()
    {
        var resolver = new XaeHostResolver(probeExeForProgId: _ => null);

        Assert.True(resolver.TryResolveFromTsproj(
            "<TcSmProject TcVersion=\"3.1.4026.22\"/>", out var r, out var err));
        Assert.Null(err);
        Assert.Equal("TcXaeShell.DTE.17.0", r!.ProgId);
        Assert.Equal("3.1.4026.22", r.TcVersion!.ToString());
        Assert.Equal(XaeHostClass.Shell64, r.HostClass);
    }

    [Fact]
    public void ResolveFromTsproj_4024_PicksShell32()
    {
        var resolver = new XaeHostResolver(probeExeForProgId: _ => null);

        Assert.True(resolver.TryResolveFromTsproj(
            "<TcSmProject TcVersion=\"3.1.4024.55\"/>", out var r, out _));
        Assert.Equal("TcXaeShell.DTE.15.0", r!.ProgId);
        Assert.Equal(XaeHostClass.Shell32, r.HostClass);
    }

    [Fact]
    public void ResolveFromTsproj_Malformed_FailsWithReason()
    {
        var resolver = new XaeHostResolver(probeExeForProgId: _ => null);

        Assert.False(resolver.TryResolveFromTsproj("not xml", out var r, out var err));
        Assert.Null(r);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void FallbackProbeOrder_IsResolvedProgIdFirstThenLegacyChain()
    {
        var resolver = new XaeHostResolver(probeExeForProgId: _ => null);

        // 64-bit project: resolved 17.0 must be tried first, but the legacy
        // probe order is preserved after it so behaviour is never worse than
        // today when attach by the resolved ProgID fails.
        var order = resolver.AttachProbeOrder(XaeHostClass.Shell64);

        Assert.Equal("TcXaeShell.DTE.17.0", order[0]);
        Assert.Contains("TcXaeShell.DTE.16.0", order);
        Assert.Contains("TcXaeShell.DTE.15.0", order);
        Assert.Equal(order.Distinct().Count(), order.Count); // no dupes
    }

    [Fact]
    public void AttachProbeOrder_Shell32_PutsProgId150First()
    {
        var resolver = new XaeHostResolver(probeExeForProgId: _ => null);

        var order = resolver.AttachProbeOrder(XaeHostClass.Shell32);

        Assert.Equal("TcXaeShell.DTE.15.0", order[0]);
        Assert.Equal(order.Distinct().Count(), order.Count);
    }
}
