using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Runtime probing for the Beckhoff-owned DLLs this project does not
/// redistribute by default (issue #156 item 4): the TcEventLogger
/// ADS-proxy managed interop assembly, its native companion
/// TcEventLoggerAdsProxy.dll, and the TE2000 HMI Automation Interface
/// TcHmiAutomation.dll — plus the Microsoft EnvDTE / EnvDTE80 / stdole /
/// Microsoft.VisualStudio.Interop interop assemblies, which are loaded from the
/// TcXaeShell installation (issue #3) and
/// are never copied to the output. When the project is built with the default
/// <c>BundleBeckhoffDlls=false</c>, none of these files is copied next to
/// the host exe (see the csproj comments above the TcEventLoggerDir and
/// TcHmiAutomationPath properties), so the normal probing paths (app base
/// dir, framework dirs) miss them. These handlers are the fallback that
/// finds them in the local TwinCAT installation instead. When built with
/// <c>-p:BundleBeckhoffDlls=true</c>, the files DO sit next to the exe, the
/// default probing finds them first, and these handlers are simply never
/// consulted.
///
/// The TcHmiAutomation path is covered by <c>BeckhoffAssemblyResolverTests</c>
/// (loads the DLL from the TE2000 installation into a throw-away load
/// context) and by Stage HMI of the live suite, which exercises the real
/// default-context handler. The EventLogger path has NOT yet been exercised
/// against a live TC3 EventLogger with BundleBeckhoffDlls at its default;
/// see SECURITY.md / the handback report for #156 for exactly what a live
/// run must confirm.
/// </summary>
internal static class BeckhoffAssemblyResolver
{
    private const string EventLoggerInteropAssemblyName = "Beckhoff.TwinCAT.TcEventLoggerAdsProxy.Native.Interop";
    private const string HmiAutomationAssemblyName = "TcHmiAutomation";
    private const string NativeLibraryFileName = "TcEventLoggerAdsProxy.dll";

    /// <summary>Microsoft EnvDTE interop assemblies (issue #3): compiled against the
    /// TcXaeShell's own copies and loaded from its PublicAssemblies directory, never
    /// redistributed. Microsoft.VisualStudio.Interop exists only in the 64-bit shell, whose
    /// envdte/envdte80 17.0 are facades over it.</summary>
    private static readonly string[] XaeShellInteropAssemblyNames =
        ["EnvDTE", "EnvDTE80", "stdole", "Microsoft.VisualStudio.Interop"];

    private static bool _registered;

    /// <summary>Idempotent; safe to call once at process startup regardless
    /// of whether this build actually references the Beckhoff assemblies
    /// (a non-TwinCAT build never triggers either handler).</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        AssemblyLoadContext.Default.Resolving += ResolveManagedAssembly;
        NativeLibrary.SetDllImportResolver(typeof(BeckhoffAssemblyResolver).Assembly, ResolveNativeLibrary);
    }

    /// <summary>
    /// Directories of the local TwinCAT installation that may hold the managed
    /// assembly <paramref name="simpleName"/>, or empty when it is not one of
    /// the Beckhoff assemblies this resolver knows about.
    /// </summary>
    internal static IReadOnlyList<string> GetCandidateDirectories(string simpleName)
    {
        if (string.Equals(simpleName, EventLoggerInteropAssemblyName, StringComparison.OrdinalIgnoreCase))
            return TwinCatPaths.GetEventLoggerDirCandidates();
        if (string.Equals(simpleName, HmiAutomationAssemblyName, StringComparison.OrdinalIgnoreCase))
            return TwinCatPaths.GetHmiAutomationDirCandidates();
        if (XaeShellInteropAssemblyNames.Contains(simpleName, StringComparer.OrdinalIgnoreCase))
            return TwinCatPaths.GetXaeShellPublicAssembliesDirCandidates();
        return [];
    }

    /// <summary>
    /// Loads the Beckhoff assembly <paramref name="name"/> into
    /// <paramref name="context"/> from the first candidate directory that
    /// holds it, or returns null when the assembly is not a known Beckhoff
    /// assembly or no candidate exists. Shared by the default-context
    /// handler and the unit test, which uses its own throw-away context so
    /// a copy already loaded next to the test host cannot mask the probe.
    /// </summary>
    internal static System.Reflection.Assembly? LoadFromTwinCatInstallation(AssemblyLoadContext context, System.Reflection.AssemblyName name)
    {
        if (name.Name is null) return null;

        foreach (string dir in GetCandidateDirectories(name.Name))
        {
            string candidate = Path.Combine(dir, name.Name + ".dll");
            if (!File.Exists(candidate)) continue;
            try { return context.LoadFromAssemblyPath(candidate); }
            catch { /* try the next version directory */ }
        }
        return null;
    }

    private static System.Reflection.Assembly? ResolveManagedAssembly(AssemblyLoadContext context, System.Reflection.AssemblyName name)
        => LoadFromTwinCatInstallation(context, name);

    private static IntPtr ResolveNativeLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, NativeLibraryFileName, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        foreach (string dir in TwinCatPaths.GetEventLoggerDirCandidates())
        {
            string candidate = Path.Combine(dir, NativeLibraryFileName);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                return handle;
        }
        return IntPtr.Zero;
    }
}
