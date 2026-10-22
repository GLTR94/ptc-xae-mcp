using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Service;
using OpenQA.Selenium.Appium.Windows;

namespace PtcXaeMcp.AppiumTests;

/// <summary>
/// Prerequisites:
///   1. npm install -g appium
///   2. appium driver install windows
///   3. appium driver run windows install-wad   (extracts WinAppDriver to user folder)
///   4. Enable Developer Mode (Settings → Privacy & security → For developers)
///      OR: reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
///              /t REG_DWORD /d 1 /v AllowDevelopmentWithoutDevLicense /f
///   5. Build the app: dotnet build src/PtcXaeMcp.WinForms/PtcXaeMcp.WinForms.csproj
///
/// The fixture auto-starts Appium (free port) and lets appium-windows-driver
/// launch WinAppDriver on its own. No manual server management needed.
///
/// Optional env vars:
///   PTC_XAE_MCP_EXE   — path to ptc-xae-mcp.exe (default: newest build under src/PtcXaeMcp.WinForms/bin)
///   APPIUM_SERVER_URL  — use an already-running Appium server instead of auto-starting
///   APPIUM_WAD_PATH    — full path to WinAppDriver.exe (auto-detected if absent)
/// </summary>
public sealed class AppiumFixture : IDisposable
{
    // WinAppDriver extracted here by: appium driver run windows install-wad
    private static readonly string ExtractedWad = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinAppDriver", "Windows Application Driver", "WinAppDriver.exe");

    private static readonly string[] SystemWadPaths =
    [
        @"C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe",
        @"C:\Program Files\Windows Application Driver\WinAppDriver.exe",
    ];

    private readonly AppiumLocalService? _service;
    public WindowsDriver Driver { get; }

    public AppiumFixture()
    {
        EnsureWadPathEnvVar();

        var exePath   = Environment.GetEnvironmentVariable("PTC_XAE_MCP_EXE") ?? FindHostExe()
            ?? throw new InvalidOperationException(
                "ptc-xae-mcp.exe not found in this checkout — build src/PtcXaeMcp.WinForms or set PTC_XAE_MCP_EXE.");
        var serverUrl = Environment.GetEnvironmentVariable("APPIUM_SERVER_URL");

        Uri uri;
        if (serverUrl != null)
        {
            uri = new Uri(serverUrl);
        }
        else
        {
            _service = new AppiumServiceBuilder()
                .UsingAnyFreePort()
                .Build();
            _service.Start();
            uri = _service.ServiceUrl;
        }

        var opts = new AppiumOptions();
        opts.App            = exePath;
        opts.PlatformName   = "Windows";
        opts.AutomationName = "windows";

        Driver = new WindowsDriver(uri, opts);
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
    }

    // If APPIUM_WAD_PATH is not set and WinAppDriver is not in any system path,
    // point the env var at the user-local extracted copy so appium-windows-driver finds it.
    private static void EnsureWadPathEnvVar()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIUM_WAD_PATH")))
            return;
        if (SystemWadPaths.Any(File.Exists))
            return;
        if (File.Exists(ExtractedWad))
            Environment.SetEnvironmentVariable("APPIUM_WAD_PATH", ExtractedWad);
    }

    /// <summary>
    /// Newest ptc-xae-mcp.exe under src/PtcXaeMcp.WinForms/bin/, located relative to
    /// this test assembly (no absolute developer paths). Null when nothing is built.
    /// </summary>
    private static string? FindHostExe()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string bin = Path.Combine(dir.FullName, "PtcXaeMcp.WinForms", "bin");
            if (!Directory.Exists(bin)) continue;
            return Directory.EnumerateFiles(bin, "ptc-xae-mcp.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        return null;
    }

    public void Dispose()
    {
        try { Driver.Quit(); } catch { /* ignore if app already closed */ }
        _service?.Dispose();
    }
}

[CollectionDefinition("Appium")]
public class AppiumCollection : ICollectionFixture<AppiumFixture> { }
