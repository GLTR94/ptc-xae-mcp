using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class EventLoggerTests
{
    private static EventLogEntry SampleEvent() => new(
        Type: "Alarm",
        Severity: "Error",
        Source: "PLC.MAIN",
        SourceId: 7,
        EventClass: "{11111111-2222-3333-4444-555555555555}",
        EventId: 42,
        Text: "Motor overtemp",
        TimeRaised: "2026-05-17T05:00:00Z",
        TimeConfirmed: null,
        TimeCleared: null,
        WithConfirmation: true,
        ConfirmationState: "WaitForConfirmation",
        IsActive: true);

    // ---- ReadEventLogger (session plumbing) ----------------------------------

    [Fact]
    public void ReadEventLogger_HappyPath_RecordsCallAndReturnsResult()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadEventLoggerResult = new EventLoggerReadResult(
                true, 1, 1, new[] { SampleEvent() }, new[] { SampleEvent() },
                "EventLogger read: 1 events, 1 active alarms."),
        };

        var result = fake.ReadEventLogger(100, null, null);

        Assert.Contains("ReadEventLogger(max=100", fake.CallLog[0]);
        Assert.True(result.Success);
        Assert.Equal(1, result.LoggedCount);
        Assert.Single(result.ActiveAlarms);
    }

    [Fact]
    public void ReadEventLogger_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            ReadEventLoggerThrows = new InvalidOperationException("ADS proxy unreachable"),
        };

        Assert.Throws<InvalidOperationException>(() => fake.ReadEventLogger(10, null, null));
        Assert.Contains("ReadEventLogger", fake.CallLog[0]);
    }

    // ---- ConfirmEventLogger (session plumbing) -------------------------------

    [Fact]
    public void ConfirmEventLogger_HappyPath_RecordsModeAndReturnsResult()
    {
        var fake = new FakeTwinCatSession
        {
            FakeConfirmEventLoggerResult = new EventLoggerActionResult(
                true, 3, 0, "Confirmed all alarms. 3 → 0 active."),
        };

        var result = fake.ConfirmEventLogger("confirm", null);

        Assert.Contains("ConfirmEventLogger(mode=confirm", fake.CallLog[0]);
        Assert.True(result.Success);
        Assert.Equal(3, result.ActiveAlarmsBefore);
        Assert.Equal(0, result.ActiveAlarmsAfter);
    }

    [Fact]
    public void ConfirmEventLogger_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            ConfirmEventLoggerThrows = new InvalidOperationException("not connected"),
        };

        Assert.Throws<InvalidOperationException>(() => fake.ConfirmEventLogger("all", null));
    }

    // ---- EventLoggerCoordinator (JSON shaping + validation) ------------------

    [Fact]
    public void Coordinator_Read_SerializesEventsAsJson()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadEventLoggerResult = new EventLoggerReadResult(
                true, 1, 1, new[] { SampleEvent() }, new[] { SampleEvent() },
                "EventLogger read: 1 events, 1 active alarms."),
        };

        var json = new EventLoggerCoordinator(fake).ReadEventLogger(0, null, null);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("loggedCount").GetInt32());
        Assert.Equal(1, root.GetProperty("activeAlarmCount").GetInt32());
        var ev = root.GetProperty("events")[0];
        Assert.Equal("Alarm", ev.GetProperty("type").GetString());
        Assert.Equal("Motor overtemp", ev.GetProperty("text").GetString());
        Assert.Equal("WaitForConfirmation", ev.GetProperty("confirmationState").GetString());
    }

    [Fact]
    public void Coordinator_Read_DefaultsMaxEventsWhenZero()
    {
        var fake = new FakeTwinCatSession();

        _ = new EventLoggerCoordinator(fake).ReadEventLogger(0, null, null);

        // 0 must not be passed through as "return zero events"; coordinator
        // substitutes a sane default.
        Assert.DoesNotContain("ReadEventLogger(max=0", fake.CallLog[0]);
    }

    [Fact]
    public void Coordinator_Read_FailureReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadEventLoggerResult = new EventLoggerReadResult(
                false, -1, -1, Array.Empty<EventLogEntry>(), Array.Empty<EventLogEntry>(),
                "Not connected to the TC3 EventLogger."),
        };

        var text = new EventLoggerCoordinator(fake).ReadEventLogger(50, null, null);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("Not connected", text);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("clear-alarms")]
    [InlineData("clear-events")]
    [InlineData("all")]
    public void Coordinator_Confirm_AcceptsValidModes(string mode)
    {
        var fake = new FakeTwinCatSession
        {
            FakeConfirmEventLoggerResult = new EventLoggerActionResult(true, 2, 0, "ok"),
        };

        var json = new EventLoggerCoordinator(fake).ConfirmEventLogger(mode, null);

        Assert.DoesNotContain("[Error]", json);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Coordinator_Confirm_InvalidMode_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = new FakeTwinCatSession();

        var text = new EventLoggerCoordinator(fake).ConfirmEventLogger("nuke", null);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("mode", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("ConfirmEventLogger", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_Confirm_FailureReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            FakeConfirmEventLoggerResult = new EventLoggerActionResult(
                false, -1, -1, "ConfirmEventLogger failed: not connected."),
        };

        var text = new EventLoggerCoordinator(fake).ConfirmEventLogger("confirm", null);

        Assert.StartsWith("[Error]", text);
    }
}
