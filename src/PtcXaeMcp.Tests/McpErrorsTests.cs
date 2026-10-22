using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Pure-logic tests for the <see cref="McpErrors"/> result-contract helpers:
/// error detection, the canonical cancellation message, and prefix
/// de-duplication in <see cref="McpErrors.EnsureError"/>.
/// </summary>
public sealed class McpErrorsTests
{
    [Theory]
    [InlineData("[Error] boom", true)]
    [InlineData("[Error] Cancelled: stopped at test gate", true)]
    [InlineData("OK", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsError_MatchesOnlyThePrefix(string? result, bool expected)
        => Assert.Equal(expected, McpErrors.IsError(result));

    [Fact]
    public void IsOk_IsInverseOfIsError()
    {
        Assert.True(McpErrors.IsOk("fine"));
        Assert.False(McpErrors.IsOk("[Error] nope"));
    }

    [Fact]
    public void Cancelled_ProducesErrorPrefixedMessage()
    {
        var msg = McpErrors.Cancelled("stopped at test gate");

        Assert.Equal("[Error] Cancelled: stopped at test gate", msg);
        Assert.True(McpErrors.IsError(msg));
    }

    [Fact]
    public void EnsureError_WrapsBareMessage()
    {
        Assert.Equal("[Error] boom", McpErrors.EnsureError("boom"));
    }

    [Fact]
    public void EnsureError_PassesThroughAlreadyPrefixedMessage()
    {
        Assert.Equal("[Error] boom", McpErrors.EnsureError("[Error] boom"));
    }

    [Fact]
    public void EnsureError_DeduplicatesRepeatedPrefix()
    {
        Assert.Equal("[Error] boom", McpErrors.EnsureError("[Error] [Error] boom"));
        Assert.Equal("[Error] boom", McpErrors.EnsureError("[Error] [Error] [Error] boom"));
    }
}
