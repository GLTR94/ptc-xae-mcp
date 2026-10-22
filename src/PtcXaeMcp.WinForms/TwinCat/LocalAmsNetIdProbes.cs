using Microsoft.Win32;
#if TWINCAT
using TwinCAT.Ads;
#endif

namespace PtcXaeMcp.WinForms.TwinCat;

// Issue #202: the real sources behind LocalAmsNetIdResolver. Both only read: nothing is written to the router or the
// registry.

/// <summary>
/// The real sources of <see cref="LocalAmsNetIdResolver"/>: an ADS port opened on the local AMS router, and the
/// TwinCAT registry key.
/// </summary>
internal sealed class LocalAmsNetIdProbes : ILocalAmsNetIdSources
{
    /// <summary>The TwinCAT registry keys that carry AmsNetId. TwinCAT writes WOW6432Node on x64.</summary>
    private static readonly string[] RegistryKeys =
    [
        @"SOFTWARE\WOW6432Node\Beckhoff\TwinCAT3\System",
        @"SOFTWARE\Beckhoff\TwinCAT3\System",
    ];

#if TWINCAT
    /// <summary>ADS timeout of the probe's client. Opening the port does not wait on it; it bounds a stray request.</summary>
    private static readonly TimeSpan RouterTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Opens an ADS port on the local AMS router (towards its system service, AMS port 10000) and returns the NetId the
    /// router gave that port (<see cref="AdsClient.SourceAddress"/>). The router assigns it, so it is the router's own
    /// NetId even when the registry names another. Read-only probe on the dev VM (TC 4026): the same NetId as the
    /// connection's target <see cref="AdsClient.Address"/> and <see cref="AmsNetId.Local"/>, and the one ReadState
    /// answered on. <see cref="AmsNetId.Local"/> is not used: it is documented to prefer configuration and environment
    /// variables (AmsNetId, LocalNetId) over the router, to fall back to the registry by itself, and it is kept in a
    /// static field.
    /// </summary>
    public string? ReadRouterNetId()
    {
        using var client = new AdsClient { Timeout = (int)RouterTimeout.TotalMilliseconds };
        client.Connect(AmsPort.SystemService);
        if (!client.IsConnected)
            throw new InvalidOperationException("no ADS port could be opened on the local AMS router");
        return client.SourceAddress?.NetId?.ToString();
    }
#else
    public string? ReadRouterNetId() => throw TwinCatNotBuilt.Exception();
#endif

    public string? ReadRegistryNetId()
    {
        foreach (string sub in RegistryKeys)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(sub);
                string? netId = AmsNetIdText.FromRegistryValue(key?.GetValue("AmsNetId"));
                if (netId is not null) return netId;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // An unreadable key: try the next one.
            }
        }
        return null;
    }
}
