using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using PtcXaeMcp.WinForms.TwinCat;
using Xunit;

namespace PtcXaeMcp.Tests;

/// <summary>
/// The Beckhoff DLLs this project does not redistribute (#156 item 4) must be found in
/// the local TwinCAT installation at runtime. These tests probe the real installation on
/// the machine and skip where it is absent (CI runners have no TwinCAT).
/// </summary>
public class BeckhoffAssemblyResolverTests
{
    [Fact]
    public void Unknown_assembly_names_have_no_candidate_directories_and_are_not_loaded()
    {
        BeckhoffAssemblyResolver.GetCandidateDirectories("Newtonsoft.Json").Should().BeEmpty();

        using var context = new TestLoadContext();
        BeckhoffAssemblyResolver.LoadFromTwinCatInstallation(context, new AssemblyName("Newtonsoft.Json"))
            .Should().BeNull();
    }

    [Fact]
    public void Known_names_map_to_their_twincat_directories()
    {
        BeckhoffAssemblyResolver.GetCandidateDirectories("TcHmiAutomation")
            .Should().BeEquivalentTo(TwinCatPaths.GetHmiAutomationDirCandidates());
        BeckhoffAssemblyResolver.GetCandidateDirectories("Beckhoff.TwinCAT.TcEventLoggerAdsProxy.Native.Interop")
            .Should().BeEquivalentTo(TwinCatPaths.GetEventLoggerDirCandidates());
    }

    [SkippableFact]
    public void TcHmiAutomation_is_loaded_from_the_TE2000_installation()
    {
        // Independent of TwinCatPaths: skip only when TE2000 is not at its default location, so a
        // broken path derivation shows up as a failure, not as a skip (the registry InstallDir
        // value ends with a separator, which once made GetTwinCatDir return the wrong parent).
        string defaultDll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Beckhoff", "TwinCAT", "Functions", "TE2000-HMI-Engineering", "bin", "TcHmiAutomation.dll");
        Skip.IfNot(File.Exists(defaultDll), "TE2000 HMI Engineering is not installed at its default location.");

        IReadOnlyList<string> dirs = TwinCatPaths.GetHmiAutomationDirCandidates();
        dirs.Should().ContainSingle().Which.Should().Be(Path.GetDirectoryName(defaultDll));

        // A throw-away context: a stale TcHmiAutomation.dll next to the test host (from a build
        // with -p:BundleBeckhoffDlls=true) would otherwise satisfy the default context first.
        using var context = new TestLoadContext();
        Assembly? loaded = BeckhoffAssemblyResolver.LoadFromTwinCatInstallation(context, new AssemblyName("TcHmiAutomation"));

        loaded.Should().NotBeNull();
        loaded!.GetName().Name.Should().Be("TcHmiAutomation");
        Path.GetDirectoryName(loaded.Location).Should().Be(dirs[0]);
    }

    [Theory]
    [InlineData("EnvDTE")]
    [InlineData("EnvDTE80")]
    [InlineData("stdole")]
    [InlineData("Microsoft.VisualStudio.Interop")]
    public void Microsoft_interop_names_map_to_the_XaeShell_PublicAssemblies_directories(string simpleName)
    {
        BeckhoffAssemblyResolver.GetCandidateDirectories(simpleName)
            .Should().Equal(TwinCatPaths.GetXaeShellPublicAssembliesDirCandidates());
    }

    private static string DefaultPublicAssembliesDir(Environment.SpecialFolder programFiles) =>
        Path.Combine(Environment.GetFolderPath(programFiles), "Beckhoff", "TcXaeShell", "Common7", "IDE", "PublicAssemblies");

    [SkippableFact]
    public void The_64bit_shell_is_probed_before_the_32bit_XAE()
    {
        // The 64-bit shell's 17.0 copies satisfy a build compiled against the 32-bit XAE's 8.0
        // PIAs; the reverse does not hold, so the order matters.
        string dir64 = DefaultPublicAssembliesDir(Environment.SpecialFolder.ProgramFiles);
        string dir32 = DefaultPublicAssembliesDir(Environment.SpecialFolder.ProgramFilesX86);
        Skip.IfNot(Directory.Exists(dir64) && Directory.Exists(dir32), "Needs both the 64-bit TcXaeShell and the 32-bit XAE.");

        TwinCatPaths.GetXaeShellPublicAssembliesDirCandidates().Should().Equal(dir64, dir32);
    }

    [SkippableTheory]
    [InlineData(Environment.SpecialFolder.ProgramFiles, 17)]
    [InlineData(Environment.SpecialFolder.ProgramFilesX86, 8)]
    public void EnvDTE_and_EnvDTE80_load_from_each_installed_shell(Environment.SpecialFolder programFiles, int expectedMajor)
    {
        // Issue #3: the Microsoft interop assemblies are not redistributed; each shell's own
        // copies must load in this net10 process (the 32-bit XAE's 8.0 PIAs target mscorlib
        // 1.0.3300, the 64-bit shell's 17.0 envdte forwards to Microsoft.VisualStudio.Interop).
        string dir = DefaultPublicAssembliesDir(programFiles);
        Skip.IfNot(File.Exists(Path.Combine(dir, "envdte80.dll")), $"No TcXaeShell at {dir}.");
        TwinCatPaths.GetXaeShellPublicAssembliesDirCandidates().Should().Contain(dir);

        // Load the dependencies first, so EnvDTE80's references bind to this shell's copies and
        // not to an envdte.dll next to the test host (the NuGet fallback build without TcXaeShell).
        using var context = new TestLoadContext();
        string interopPath = Path.Combine(dir, "Microsoft.VisualStudio.Interop.dll");
        Assembly? vsInterop = File.Exists(interopPath) ? context.LoadFromAssemblyPath(interopPath) : null;
        Assembly envDte = context.LoadFromAssemblyPath(Path.Combine(dir, "envdte.dll"));
        Assembly envDte80 = context.LoadFromAssemblyPath(Path.Combine(dir, "envdte80.dll"));

        envDte.GetName().Version!.Major.Should().Be(expectedMajor);
        Type dte2 = envDte80.GetType("EnvDTE80.DTE2", throwOnError: true)!;
        dte2.GetInterfaces().Should().Contain(i => i.FullName == "EnvDTE._DTE" && i.Assembly == (vsInterop ?? envDte));
    }

    [SkippableFact]
    public void The_resolver_loads_EnvDTE_from_the_first_installed_shell()
    {
        IReadOnlyList<string> dirs = TwinCatPaths.GetXaeShellPublicAssembliesDirCandidates();
        Skip.If(dirs.Count == 0, "No TcXaeShell installed.");

        using var context = new TestLoadContext();
        Assembly? envDte = BeckhoffAssemblyResolver.LoadFromTwinCatInstallation(context, new AssemblyName("EnvDTE"));

        envDte.Should().NotBeNull();
        Path.GetDirectoryName(envDte!.Location).Should().Be(dirs[0]);
        envDte.GetName().Version.Should().BeGreaterThanOrEqualTo(new Version(8, 0, 0, 0), "TcHmiAutomation.dll references EnvDTE 8.0.0.0");
    }

    private sealed class TestLoadContext : AssemblyLoadContext, IDisposable
    {
        public TestLoadContext() : base("BeckhoffAssemblyResolverTests", isCollectible: true) { }
        protected override Assembly? Load(AssemblyName assemblyName) => null;
        public void Dispose() => Unload();
    }
}
