using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class ClearErrorListTests
{
    [Fact]
    public void ClearErrorList_HappyPath_RecordsCallAndReturnsSuccess()
    {
        var fake = new FakeTwinCatSession
        {
            FakeClearErrorListResult = new ErrorListClearResult(true, 5, "Error list cleared."),
        };

        var result = fake.ClearErrorList();

        Assert.Contains("ClearErrorList()", fake.CallLog);
        Assert.True(result.Success);
        Assert.Equal(5, result.ClearedCount);
        Assert.Contains("cleared", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClearErrorList_BestEffortCount_NegativeOneAllowed()
    {
        var fake = new FakeTwinCatSession
        {
            FakeClearErrorListResult = new ErrorListClearResult(true, -1, "Error list cleared."),
        };

        var result = fake.ClearErrorList();

        Assert.True(result.Success);
        Assert.Equal(-1, result.ClearedCount);
    }

    [Fact]
    public void ClearErrorList_Failure_ReturnsFalse()
    {
        var fake = new FakeTwinCatSession
        {
            FakeClearErrorListResult = new ErrorListClearResult(false, -1, "ClearErrorList failed: command not available."),
        };

        var result = fake.ClearErrorList();

        Assert.False(result.Success);
        Assert.Contains("failed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClearErrorList_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            ClearErrorListThrows = new InvalidOperationException("COM server unreachable"),
        };

        Assert.Throws<InvalidOperationException>(() => fake.ClearErrorList());
        Assert.Contains("ClearErrorList()", fake.CallLog);
    }
}
