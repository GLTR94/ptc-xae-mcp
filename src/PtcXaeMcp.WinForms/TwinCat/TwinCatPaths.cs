namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Single source of truth (issue #157 item 9) for locating the local TwinCAT
/// 3.1 installation: registry → <c>%TwinCAT3Dir%</c> → the conventional
/// "Program Files (x86)\Beckhoff\TwinCAT\3.1" default. Originally written for
/// runtime probing of the Beckhoff-owned DLLs this project does not
/// redistribute (issue #156 item 4: the TcEventLogger ADS-proxy managed
/// interop assembly and its native companion — <see cref="GetEventLoggerDirCandidates"/>
/// mirrors the build-time <c>$(TcEventLoggerDir)</c> search in
/// PtcXaeMcp.WinForms.csproj so the runtime probe looks in the same places
/// the build did); every other place in this project that used to duplicate
/// the registry/env-var/default resolution (<see cref="TwinCatComSession"/>'s
/// XAE exe path, <see cref="UmrProcessManager"/>'s runtime roots,
/// <see cref="IoddImportCoordinator"/>'s IODD candidates, the Scope project
/// template path) now calls <see cref="GetInstallDir"/> instead.
/// </summary>
internal static class TwinCatPaths
{
    private const string RegistryKeyPath = @"SOFTWARE\WOW6432Node\Beckhoff\TwinCAT3";
    private const string RegistryValueName = "InstallDir";

    private static readonly string[] EventLoggerVersionDirs = ["v170", "v160", "v150"];

    /// <summary>
    /// Root TwinCAT 3.1 install directory, or null if none of the three
    /// sources — registry, <c>%TwinCAT3Dir%</c>, the default
    /// "Program Files (x86)\Beckhoff\TwinCAT\3.1" path — resolves to a
    /// directory that actually exists.
    /// </summary>
    public static string? GetInstallDir()
    {
        string? fromRegistry = TryReadRegistryInstallDir();
        if (IsExistingDir(fromRegistry)) return fromRegistry;

        string? fromEnv = Environment.GetEnvironmentVariable("TwinCAT3Dir");
        if (IsExistingDir(fromEnv)) return fromEnv;

        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string defaultDir = Path.Combine(programFilesX86, "Beckhoff", "TwinCAT", "3.1");
        return IsExistingDir(defaultDir) ? defaultDir : null;
    }

    /// <summary>
    /// TcEventloggerV2 component directories under the resolved install dir
    /// that actually exist, newest version first (v170, v160, v150 — same
    /// order the csproj searches). Empty when no TwinCAT install was found.
    /// </summary>
    public static IReadOnlyList<string> GetEventLoggerDirCandidates()
    {
        string? installDir = GetInstallDir();
        if (installDir is null) return [];

        string componentsRoot = Path.Combine(installDir, "Components", "TcEventloggerV2");
        return EventLoggerVersionDirs
            .Select(v => Path.Combine(componentsRoot, v))
            .Where(Directory.Exists)
            .ToArray();
    }

    /// <summary>
    /// Directories that may hold <c>TcHmiAutomation.dll</c> (the TE2000 HMI
    /// Engineering Automation Interface) and actually exist: the TE2000
    /// <c>bin</c> folder under <see cref="GetTwinCatDir"/>, the same place
    /// the build-time <c>$(TcHmiAutomationPath)</c> in PtcXaeMcp.WinForms.csproj
    /// points to. Empty when neither TwinCAT nor TE2000 is installed. The DLL
    /// is proprietary Beckhoff software and is not redistributed (#156 item
    /// 4), so <see cref="BeckhoffAssemblyResolver"/> loads it from here.
    /// </summary>
    public static IReadOnlyList<string> GetHmiAutomationDirCandidates()
    {
        string? twinCatDir = GetTwinCatDir();
        if (twinCatDir is null) return [];

        string bin = Path.Combine(twinCatDir, "Functions", "TE2000-HMI-Engineering", "bin");
        return Directory.Exists(bin) ? [bin] : [];
    }

    /// <summary>
    /// The <c>…\Beckhoff\TwinCAT</c> directory (parent of <see cref="GetInstallDir"/>'s
    /// <c>3.1</c>), or null if the install dir can't be resolved. Several other
    /// Beckhoff products (TcXaeShell, the TE130x Measurement/Scope templates)
    /// install as siblings of <c>3.1</c> under this same directory.
    /// </summary>
    public static string? GetTwinCatDir()
    {
        string? installDir = GetInstallDir();
        // The registry value carries a trailing separator ("…\TwinCAT\3.1\"); without trimming
        // it, GetDirectoryName would return "…\TwinCAT\3.1" instead of the parent "…\TwinCAT".
        return installDir is null ? null : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(installDir));
    }

    /// <summary>Fallback used by <see cref="GetXaeShellExePath"/> when the install dir cannot be resolved.</summary>
    private const string DefaultXaeShellExePath =
        @"C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";

    /// <summary>
    /// Full path to <c>TcXaeShell.exe</c>. TcXaeShell installs as a sibling of
    /// <c>TwinCAT\3.1</c> under the same Beckhoff Program Files root, so this
    /// derives it from <see cref="GetInstallDir"/> (registry → <c>%TwinCAT3Dir%</c>
    /// → default) rather than hardcoding a second path; falls back to the
    /// conventional default location if that derivation doesn't exist.
    /// </summary>
    public static string GetXaeShellExePath()
    {
        string? beckhoffRoot = GetTwinCatDir() is { } twinCatDir ? Path.GetDirectoryName(twinCatDir) : null; // …\Beckhoff
        if (beckhoffRoot is not null)
        {
            string candidate = Path.Combine(beckhoffRoot, "TcXaeShell", "Common7", "IDE", "TcXaeShell.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return DefaultXaeShellExePath;
    }

    /// <summary>
    /// The TcXaeShell <c>Common7\IDE\PublicAssemblies</c> directories that exist, which hold
    /// the Microsoft EnvDTE interop assemblies this project compiles against but does not
    /// redistribute (issue #3). The 64-bit shell (<c>Program Files\Beckhoff\TcXaeShell</c>)
    /// comes first: it ships envdte/envdte80/stdole/Microsoft.VisualStudio.Interop 17.0, and
    /// the highest version satisfies every reference (the build may have compiled against the
    /// 32-bit shell's 8.0 PIAs, and TcHmiAutomation.dll references EnvDTE 8.0.0.0). The 32-bit
    /// shell (derived via <see cref="GetXaeShellExePath"/>) ships envdte/envdte80 8.0 and stdole
    /// 7.0 only. Empty when neither shell is installed.
    /// </summary>
    public static IReadOnlyList<string> GetXaeShellPublicAssembliesDirCandidates()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string? ideDir32 = Path.GetDirectoryName(GetXaeShellExePath());

        string?[] candidates =
        [
            string.IsNullOrEmpty(programFiles) ? null
                : Path.Combine(programFiles, "Beckhoff", "TcXaeShell", "Common7", "IDE", "PublicAssemblies"),
            ideDir32 is null ? null : Path.Combine(ideDir32, "PublicAssemblies"),
        ];
        return candidates
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();
    }

    /// <summary>Fallback used by <see cref="GetScopeTemplatePath"/> when the install dir cannot be resolved.</summary>
    private const string DefaultScopeTemplatePath =
        @"C:\Program Files (x86)\Beckhoff\TwinCAT\Functions\TwinCAT Measurement\Templates\Projects\YT Scope Project.tcmproj";

    /// <summary>
    /// Full path to the TE130x "YT Scope Project" template, used to create a
    /// new Scope project. The <c>Functions</c> tree lives as a sibling of
    /// <c>3.1</c> under <c>…\Beckhoff\TwinCAT</c>, same derivation as
    /// <see cref="GetXaeShellExePath"/>.
    /// </summary>
    public static string GetScopeTemplatePath()
    {
        if (GetTwinCatDir() is { } twinCatDir)
        {
            string candidate = Path.Combine(twinCatDir, "Functions", "TwinCAT Measurement",
                "Templates", "Projects", "YT Scope Project.tcmproj");
            if (File.Exists(candidate)) return candidate;
        }
        return DefaultScopeTemplatePath;
    }

    private static string? TryReadRegistryInstallDir()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(RegistryValueName) as string;
        }
        catch
        {
            // Registry access can fail under restricted policies or a 32-bit
            // process without WOW6432Node redirection — the caller falls
            // back to %TwinCAT3Dir% / the default path either way.
            return null;
        }
    }

    private static bool IsExistingDir(string? path) =>
        !string.IsNullOrEmpty(path) && Directory.Exists(path);
}
