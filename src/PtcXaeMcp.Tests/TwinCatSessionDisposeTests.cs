using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public sealed class TwinCatSessionDisposeTests
{
    [Fact]
    public void FakeTwinCatSession_ImplementsIDisposable()
    {
        // Compile-time assertion: ITwinCatSession extends IDisposable,
        // so FakeTwinCatSession must implement it too.
        using ITwinCatSession session = new FakeTwinCatSession();
        Assert.NotNull(session);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice_NoException()
    {
        var session = new FakeTwinCatSession();
        session.Dispose();
        session.Dispose(); // must not throw
    }
}
