using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #272: the Approvals view lists every HMI write approval. The history is a display —
/// recording must never change the confirmer's answer (fail-closed stays with the caller).
/// </summary>
public sealed class HmiApprovalHistoryTests
{
    private static HmiWriteRequest Request(string symbol = "ADS.PLC1.MAIN.bStart", string? value = "true") =>
        new(HmiWriteKind.WriteSymbol, "HmiProject", @"C:\Projects\Hmi", symbol, "ADS", value, "false", true);

    [Theory]
    [InlineData(nameof(HmiConfirmOutcome.Confirmed))]
    [InlineData(nameof(HmiConfirmOutcome.Rejected))]
    [InlineData(nameof(HmiConfirmOutcome.TimedOut))]
    [InlineData(nameof(HmiConfirmOutcome.AutoConfirmed))]
    [InlineData(nameof(HmiConfirmOutcome.Unavailable))]
    public void RecordingConfirmer_RecordsEachOutcome_AndReturnsItUnchanged(string outcomeName)
    {
        var outcome = Enum.Parse<HmiConfirmOutcome>(outcomeName);
        var history = new HmiApprovalHistory();
        var inner = new FakeHmiConfirmer(outcome);
        var confirmer = new RecordingHmiConfirmer(inner, history);

        var answer = confirmer.Confirm(Request(), TimeSpan.FromSeconds(30));

        answer.Should().Be(outcome);
        inner.Requests.Should().ContainSingle().Which.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        var record = history.Snapshot().Should().ContainSingle().Subject;
        record.Outcome.Should().Be(outcome);
        record.IsPending.Should().BeFalse();
        record.Kind.Should().Be(HmiWriteKind.WriteSymbol);
        record.HmiProject.Should().Be("HmiProject");
        record.Symbol.Should().Be("ADS.PLC1.MAIN.bStart");
        record.NewValue.Should().Be("true");
        record.WritesToPlc.Should().BeTrue();
        record.AnsweredUtc.Should().NotBeNull();
    }

    [Fact]
    public void Entry_IsPending_WhileThePopupIsOpen()
    {
        var history = new HmiApprovalHistory();
        HmiApprovalRecord? seenDuringConfirm = null;
        var inner = new CallbackConfirmer(() =>
        {
            seenDuringConfirm = history.Snapshot().Single();
            return HmiConfirmOutcome.Rejected;
        });

        new RecordingHmiConfirmer(inner, history).Confirm(Request(), TimeSpan.FromSeconds(5));

        seenDuringConfirm!.IsPending.Should().BeTrue();
        history.PendingCount.Should().Be(0);
        history.Snapshot().Single().Outcome.Should().Be(HmiConfirmOutcome.Rejected);
    }

    [Fact]
    public void Changed_IsRaised_ForBeginAndAnswer()
    {
        var history = new HmiApprovalHistory();
        var events = new List<HmiApprovalRecord>();
        history.Changed += events.Add;

        new RecordingHmiConfirmer(new FakeHmiConfirmer(HmiConfirmOutcome.Confirmed), history)
            .Confirm(Request(), TimeSpan.FromSeconds(5));

        events.Should().HaveCount(2);
        events[0].IsPending.Should().BeTrue();
        events[1].Outcome.Should().Be(HmiConfirmOutcome.Confirmed);
        events[1].Id.Should().Be(events[0].Id);
    }

    [Fact]
    public void History_StaysBounded_DroppingTheOldest()
    {
        var history = new HmiApprovalHistory(capacity: 5);
        var confirmer = new RecordingHmiConfirmer(new FakeHmiConfirmer(HmiConfirmOutcome.Confirmed), history);

        for (int i = 0; i < 12; i++)
            confirmer.Confirm(Request(symbol: $"S{i}"), TimeSpan.FromSeconds(1));

        var records = history.Snapshot();
        records.Should().HaveCount(5);
        records.Select(r => r.Symbol).Should().Equal("S7", "S8", "S9", "S10", "S11");
        records.Should().OnlyContain(r => r.Outcome == HmiConfirmOutcome.Confirmed);
    }

    [Fact]
    public void ThrowingSubscriber_DoesNotChangeTheAnswer()
    {
        var history = new HmiApprovalHistory();
        history.Changed += _ => throw new InvalidOperationException("broken view");

        var answer = new RecordingHmiConfirmer(new FakeHmiConfirmer(HmiConfirmOutcome.Rejected), history)
            .Confirm(Request(), TimeSpan.FromSeconds(1));

        answer.Should().Be(HmiConfirmOutcome.Rejected);
        history.Snapshot().Single().Outcome.Should().Be(HmiConfirmOutcome.Rejected);
    }

    [Fact]
    public void ThrowingInnerConfirmer_Propagates_AndIsRecordedAsUnavailable()
    {
        var history = new HmiApprovalHistory();
        var inner = new CallbackConfirmer(() => throw new InvalidOperationException("window gone"));

        var act = () => new RecordingHmiConfirmer(inner, history).Confirm(Request(), TimeSpan.FromSeconds(1));

        act.Should().Throw<InvalidOperationException>().WithMessage("window gone");
        history.Snapshot().Single().Outcome.Should().Be(HmiConfirmOutcome.Unavailable);
    }

    [Fact]
    public void ConcurrentConfirms_AreAllRecorded()
    {
        var history = new HmiApprovalHistory(capacity: 1000);
        var confirmer = new RecordingHmiConfirmer(new CallbackConfirmer(() => HmiConfirmOutcome.Confirmed), history);

        Parallel.For(0, 200, i => confirmer.Confirm(Request(symbol: $"S{i}"), TimeSpan.FromSeconds(1)));

        var records = history.Snapshot();
        records.Should().HaveCount(200);
        records.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        records.Should().OnlyContain(r => r.Outcome == HmiConfirmOutcome.Confirmed);
    }

    [Fact]
    public void ServerHost_RecordsTheFailClosedAnswer_BeforeAUiIsAttached()
    {
        using var host = new ServerHost(new FakeTwinCatSession(), enableTimers: false);

        host.HmiConfirmer.Confirm(Request(), TimeSpan.FromSeconds(1)).Should().Be(HmiConfirmOutcome.Unavailable);

        host.HmiApprovals.Snapshot().Single().Outcome.Should().Be(HmiConfirmOutcome.Unavailable);
    }

    [Fact]
    public void ServerHost_RecordsTheAttachedUisAnswer()
    {
        using var host = new ServerHost(new FakeTwinCatSession(), enableTimers: false);
        host.AttachHmiConfirmUi(new FakeHmiConfirmer(HmiConfirmOutcome.Rejected));

        host.HmiConfirmer.Confirm(Request(), TimeSpan.FromSeconds(1)).Should().Be(HmiConfirmOutcome.Rejected);

        host.HmiApprovals.Snapshot().Single().Outcome.Should().Be(HmiConfirmOutcome.Rejected);
    }

    private sealed class CallbackConfirmer(Func<HmiConfirmOutcome> answer) : IHmiWriteConfirmer
    {
        public HmiConfirmOutcome Confirm(HmiWriteRequest request, TimeSpan timeout) => answer();
    }
}
