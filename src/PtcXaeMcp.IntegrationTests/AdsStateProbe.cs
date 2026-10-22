using TwinCAT.Ads;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Direct ADS ReadState of a runtime's TwinCAT system service (AMS port 10000), through the local AMS router.
/// The runtime stage uses it for one decision only (issue #195): whether the explicit runtime target answers at
/// all, before it may start the named user-mode runtime (<see cref="LiveXae.RuntimeUmrInstanceEnvVar"/>).
/// Read-only. Everything a test asserts still goes through the MCP server.
/// </summary>
internal static class AdsStateProbe
{
    /// <summary>AMS port of the TwinCAT system service.</summary>
    public const int SystemServicePort = 10000;

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The ADS state name ("Run", "Config", …) of <paramref name="amsNetId"/>, or null when it answers no state read.</summary>
    public static string? TryReadState(string amsNetId)
    {
        try
        {
            using var client = new AdsClient { Timeout = (int)ReadTimeout.TotalMilliseconds };
            client.Connect(amsNetId, SystemServicePort);
            return client.TryReadState(out StateInfo state) == AdsErrorCode.NoError
                ? state.AdsState.ToString()
                : null;
        }
        catch
        {
            return null; // no router, no route, bad NetId: the target does not answer
        }
    }

    /// <summary>Polls <see cref="TryReadState"/> until the target answers or <paramref name="timeout"/> elapses.</summary>
    public static async Task<string?> WaitForStateAsync(string amsNetId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            string? state = TryReadState(amsNetId);
            if (state is not null || DateTime.UtcNow >= deadline) return state;
            await Task.Delay(1000);
        }
    }
}
