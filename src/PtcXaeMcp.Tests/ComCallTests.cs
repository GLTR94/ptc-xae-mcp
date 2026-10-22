using System.Reflection;
using System.Runtime.InteropServices;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #149: busy classification, ReadProp, deadlines and modal early-exit.</summary>
public class ComCallTests
{
    private const int RPC_E_CALL_REJECTED = unchecked((int)0x80010001);
    private const int RPC_E_SERVERCALL_RETRYLATER = unchecked((int)0x8001010A);
    private const int E_FAIL = unchecked((int)0x80004005);

    private static readonly Func<string?> NoDialog = () => null;

    // ── busy classification ─────────────────────────────────────────────────

    [Theory]
    [InlineData(RPC_E_CALL_REJECTED)]
    [InlineData(RPC_E_SERVERCALL_RETRYLATER)]
    public void IsBusy_ComExceptionWithBusyHResult(int hr) =>
        Assert.True(ComCall.IsBusy(new COMException("busy", hr)));

    [Fact]
    public void IsBusy_OtherComException_IsNotBusy() =>
        Assert.False(ComCall.IsBusy(new COMException("E_FAIL", E_FAIL)));

    [Fact]
    public void IsBusy_RuntimeBinderGetIdsOfNamesRejection_IsBusy()
    {
        // LIVE-1: RETRYLATER during IDispatch::GetIDsOfNames surfaces from the COM binder as a
        // MissingMemberException whose only trace of the HRESULT is the message.
        var ex = new MissingMemberException("Could not get dispatch ID for Build (error: 0x8001010A).");
        Assert.True(ComCall.IsBusy(ex));
    }

    [Fact]
    public void IsBusy_RuntimeBinderExceptionMentioningCallRejected_IsBusy() =>
        Assert.True(ComCall.IsBusy(new Microsoft.CSharp.RuntimeBinder.RuntimeBinderException(
            "Call was rejected by callee. (0x80010001 (RPC_E_CALL_REJECTED))")));

    [Fact]
    public void IsBusy_GenuineMissingMember_IsNotBusy() =>
        // Duck-typing probes rely on this: a node without the member must still take the fallback.
        Assert.False(ComCall.IsBusy(new MissingMemberException("Could not get dispatch ID for DeclarationText (error: 0x80020006).")));

    [Fact]
    public void IsBusy_TargetInvocationExceptionWithBusyInner_IsBusy() =>
        Assert.True(ComCall.IsBusy(new TargetInvocationException(new COMException("busy", RPC_E_SERVERCALL_RETRYLATER))));

    [Fact]
    public void IsBusy_WrappedInInvalidOperation_IsBusy() =>
        // LIVE-2: ReadDeclarationText used to wrap the busy COMException like this.
        Assert.True(ComCall.IsBusy(new InvalidOperationException("DeclarationText failed",
            new COMException("busy", RPC_E_CALL_REJECTED))));

    [Fact]
    public void IsBusy_AggregateWithBusyInner_IsBusy() =>
        Assert.True(ComCall.IsBusy(new AggregateException(new InvalidOperationException("x"),
            new COMException("busy", RPC_E_CALL_REJECTED))));

    [Fact]
    public void IsBusy_XaeBlockedException_IsBusyAndFinal()
    {
        var ex = new XaeBlockedException("blocked", "Gehe zu Zeile", null);
        Assert.True(ComCall.IsBusy(ex));
        Assert.True(ComCall.IsFinal(ex));
        Assert.Equal(RPC_E_SERVERCALL_RETRYLATER, ex.HResult);
    }

    [Fact]
    public void IsBusy_PlainException_IsNotBusy()
    {
        Assert.False(ComCall.IsBusy(new InvalidOperationException("nope")));
        Assert.False(ComCall.IsBusy(null));
    }

    // ── ReadProp / Try ──────────────────────────────────────────────────────

    [Fact]
    public void ReadProp_GenuineFailure_ReturnsFallback() =>
        Assert.Equal(-1, ComCall.ReadProp<int>(() => throw new COMException("E_FAIL", E_FAIL), -1));

    [Fact]
    public void ReadProp_Busy_Rethrows() =>
        Assert.Throws<COMException>(() =>
            ComCall.ReadProp<int>(() => throw new COMException("busy", RPC_E_CALL_REJECTED), -1));

    [Fact]
    public void ReadProp_BinderBusy_Rethrows() =>
        Assert.Throws<MissingMemberException>(() =>
            ComCall.ReadProp<string>(() => throw new MissingMemberException("Could not get dispatch ID for Name (error: 0x8001010A)."), ""));

    [Fact]
    public void ReadProp_Success_ReturnsValue() => Assert.Equal("x", ComCall.ReadProp(() => "x", ""));

    [Fact]
    public void Try_GenuineFailureFalse_BusyRethrows()
    {
        Assert.True(ComCall.Try(() => { }));
        Assert.False(ComCall.Try(() => throw new MissingMemberException("no such member")));
        Assert.Throws<COMException>(() => ComCall.Try(() => throw new COMException("busy", RPC_E_SERVERCALL_RETRYLATER)));
    }

    // ── Run: deadlines and modal early-exit ─────────────────────────────────

    [Fact]
    public void Run_RetriesBinderBusy_ThenSucceeds()
    {
        int calls = 0;
        int result = ComCall.Run(() =>
        {
            if (++calls < 3) throw new MissingMemberException("Could not get dispatch ID for Build (error: 0x8001010A).");
            return 7;
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1), NoDialog);
        Assert.Equal(7, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Run_BlockingDialog_GivesUpAfterGraceNamingTheDialog()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = Assert.Throws<XaeBlockedException>(() =>
            ComCall.Run<int>(() => throw new COMException("busy", RPC_E_SERVERCALL_RETRYLATER),
                TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(20), () => "Gehe zu Zeile"));
        sw.Stop();

        Assert.StartsWith("XAE is blocked by dialog 'Gehe zu Zeile'", ex.Message);
        Assert.Equal("Gehe zu Zeile", ex.Dialog);
        Assert.Equal(RPC_E_SERVERCALL_RETRYLATER, ex.HResult);
        Assert.True(sw.Elapsed >= ComCall.ModalGrace, $"gave up before the grace period: {sw.Elapsed}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"burned the budget: {sw.Elapsed}");
    }

    [Fact]
    public void Run_NoDialog_DeadlineIsHonoured()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = Assert.Throws<XaeBlockedException>(() =>
            ComCall.Run<int>(() => throw new COMException("busy", RPC_E_CALL_REJECTED),
                TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(10), NoDialog));
        sw.Stop();

        Assert.StartsWith("XAE is busy", ex.Message);
        Assert.Contains("RPC_E_CALL_REJECTED", ex.Message);
        Assert.Null(ex.Dialog);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"deadline not honoured: {sw.Elapsed}");
    }

    [Fact]
    public void Run_FinalBlockedFromNestedScope_IsNotRetried()
    {
        int calls = 0;
        Assert.Throws<XaeBlockedException>(() => ComCall.Run<int>(() =>
        {
            calls++;
            throw new XaeBlockedException("blocked", "X", new COMException("busy", RPC_E_CALL_REJECTED));
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1), NoDialog));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Run_NonBusyException_PropagatesOnFirstAttempt()
    {
        int calls = 0;
        Assert.Throws<MissingMemberException>(() => ComCall.Run<int>(() =>
        {
            calls++;
            throw new MissingMemberException("no such member");
        }, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1), NoDialog));
        Assert.Equal(1, calls);
    }

    // ── Describe (tool boundary) ────────────────────────────────────────────

    [Fact]
    public void Describe_RawBusyWithDialog_NamesTheDialog()
    {
        string text = ComCall.Describe(new COMException("Anwendung ausgelastet", RPC_E_SERVERCALL_RETRYLATER), () => "Gehe zu Zeile");
        Assert.StartsWith("XAE is blocked by dialog 'Gehe zu Zeile'", text);
    }

    [Fact]
    public void Describe_RawBusyWithoutDialog_SaysBusy() =>
        Assert.StartsWith("XAE is busy and rejected the call (RPC_E_SERVERCALL_RETRYLATER)",
            ComCall.Describe(new COMException("x", RPC_E_SERVERCALL_RETRYLATER), NoDialog));

    [Fact]
    public void Describe_WrappedBlocked_UsesTheBlockedMessage() =>
        Assert.Equal("blocked!", ComCall.Describe(
            new InvalidOperationException("outer", new XaeBlockedException("blocked!", "D", null)), NoDialog));

    [Fact]
    public void Describe_NonBusy_IsTheMessage() =>
        Assert.Equal("boom", ComCall.Describe(new InvalidOperationException("boom"), () => "never asked"));

    // #155 E-12: the runner's best-effort catches let neither busy nor cancellation through.
    [Fact]
    public void Recoverable_GenuineFailure_IsRecoverable() =>
        Assert.True(ComCall.Recoverable(new COMException("E_FAIL", unchecked((int)0x80004005))));

    [Fact]
    public void Recoverable_Busy_IsNot() =>
        Assert.False(ComCall.Recoverable(new COMException("busy", ComCall.RPC_E_SERVERCALL_RETRYLATER)));

    [Fact]
    public void Recoverable_Cancellation_IsNot()
    {
        Assert.False(ComCall.Recoverable(new OperationCanceledException()));
        Assert.False(ComCall.Recoverable(new TaskCanceledException()));
    }
}
