namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Offline coverage for the polling helpers of issue #157 item 7. No collection / no fixture: these
/// never touch TwinCAT, so they run on a plain <c>dotnet test</c> and never skip.
/// </summary>
public sealed class PollTests
{
    [Trait("Description", "Offline: Poll stops as soon as the condition holds, without an initial wait.")]
    [Fact]
    public async Task UntilAsync_ReturnsAsSoonAsConditionHolds()
    {
        int calls = 0;
        int result = await Poll.UntilAsync(() => Task.FromResult(++calls), n => n >= 3,
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(1));

        Assert.Equal(3, result);
        Assert.Equal(3, calls);
    }

    [Trait("Description", "Offline: Poll returns the last probe result when the condition never holds.")]
    [Fact]
    public async Task UntilAsync_Timeout_ReturnsLastValue()
    {
        int calls = 0;
        int result = await Poll.UntilAsync(() => Task.FromResult(++calls), _ => false,
            TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));

        Assert.True(calls >= 1);
        Assert.Equal(calls, result);
    }

    [Trait("Description", "Offline: a PLC listing counts as ready only with a POU line and no not-ready marker.")]
    [Theory]
    [InlineData("POU TIPC^Plc^Plc Project^POUs^MAIN", true)]
    [InlineData("No POUs, GVLs, or DUTs found", false)]
    [InlineData("[Error] 0x8001010A busy", false)]
    [InlineData("GVL TIPC^Plc^Plc Project^GVLs^GVL_Main", false)]
    public void PlcProjectReadiness_IsReady(string listing, bool expected)
    {
        Assert.Equal(expected, PlcProjectReadiness.IsReady(listing));
    }
}
