using System.Text.Json;
using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// ibn-live: twincat_read_ads_variables_batch — session plumbing + JSON shaping.
public class AdsBatchReadTests
{
    private static AdsBatchReadResult TwoItems() => new(
        true,
        new[]
        {
            new AdsBatchReadItem("GVL_IBN.stStatus.nStep", "INT", 3, true, "OK"),
            new AdsBatchReadItem("GVL_IO.bCyl1Extended", "BOOL", true, true, "OK"),
        },
        "192.168.250.20.1.1", 851, "OK");

    // ---- session plumbing ----------------------------------------------------

    [Fact]
    public void ReadBatch_HappyPath_RecordsCallAndReturnsResult()
    {
        var fake = new FakeTwinCatSession { FakeReadAdsVariablesBatchResult = TwoItems() };

        var result = fake.ReadAdsVariablesBatch(
            new[] { "GVL_IBN.stStatus.nStep", "GVL_IO.bCyl1Extended" }, null, 851);

        Assert.Contains("ReadAdsVariablesBatch(n=2", fake.CallLog[0]);
        Assert.True(result.Success);
        Assert.Equal(2, result.Results.Count);
    }

    [Fact]
    public void ReadBatch_Throws_Propagates()
    {
        var fake = new FakeTwinCatSession
        {
            ReadAdsVariablesBatchThrows = new InvalidOperationException("ADS server unreachable"),
        };

        Assert.Throws<InvalidOperationException>(() =>
            fake.ReadAdsVariablesBatch(new[] { "MAIN.x" }, null, 851));
    }

    // ---- coordinator: JSON shaping + validation ------------------------------

    [Fact]
    public void Coordinator_Batch_SerializesResultsAsJson()
    {
        var fake = new FakeTwinCatSession { FakeReadAdsVariablesBatchResult = TwoItems() };

        var json = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(
            new[] { "GVL_IBN.stStatus.nStep", "GVL_IO.bCyl1Extended" }, null, null);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("count").GetInt32());
        var items = root.GetProperty("results");
        Assert.Equal("GVL_IBN.stStatus.nStep", items[0].GetProperty("symbol").GetString());
        Assert.Equal("INT", items[0].GetProperty("plcType").GetString());
        Assert.Equal(3, items[0].GetProperty("value").GetInt32());
        Assert.True(items[0].GetProperty("ok").GetBoolean());
        Assert.True(items[1].GetProperty("value").GetBoolean());
    }

    [Fact]
    public void Coordinator_Batch_NullOrEmpty_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = new FakeTwinCatSession();

        var nullText = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(null, null, null);
        var emptyText = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(Array.Empty<string>(), null, null);

        Assert.StartsWith("[Error]", nullText);
        Assert.StartsWith("[Error]", emptyText);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("ReadAdsVariablesBatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_Batch_TrimsAndDropsBlankSymbols()
    {
        var fake = new FakeTwinCatSession { FakeReadAdsVariablesBatchResult = TwoItems() };

        _ = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(
            new[] { "  GVL_IBN.stStatus.nStep  ", "", "   " }, null, null);

        // Only the one non-blank, trimmed symbol reaches the session.
        Assert.Contains("ReadAdsVariablesBatch(n=1, syms=[GVL_IBN.stStatus.nStep]", fake.CallLog[0]);
    }

    [Fact]
    public void Coordinator_Batch_AllBlank_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = new FakeTwinCatSession();

        var text = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(
            new[] { "", "   " }, null, null);

        Assert.StartsWith("[Error]", text);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("ReadAdsVariablesBatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_Batch_PerSymbolFailureIsItemNotWholeError()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadAdsVariablesBatchResult = new AdsBatchReadResult(
                true,
                new[]
                {
                    new AdsBatchReadItem("GVL_IO.bGood", "BOOL", true, true, "OK"),
                    new AdsBatchReadItem("NOPE.bBad", null, null, false,
                        "[Error] Symbol 'NOPE.bBad' not found in the PLC symbol table."),
                },
                "127.0.0.1.1.1", 851, "OK"),
        };

        var json = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(
            new[] { "GVL_IO.bGood", "NOPE.bBad" }, null, null);

        Assert.DoesNotContain("[Error]", json[..7]); // not a whole-call error
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("results");
        Assert.True(items[0].GetProperty("ok").GetBoolean());
        Assert.False(items[1].GetProperty("ok").GetBoolean());
        Assert.Contains("not found", items[1].GetProperty("message").GetString());
    }

    [Fact]
    public void Coordinator_Batch_ConnectionFailure_ReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            FakeReadAdsVariablesBatchResult = new AdsBatchReadResult(
                false, Array.Empty<AdsBatchReadItem>(), "127.0.0.1.1.1", 851,
                "ReadAdsVariablesBatch failed: ADS server not reachable."),
        };

        var text = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(
            new[] { "MAIN.x" }, null, null);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("not reachable", text);
        Assert.EndsWith("(target 127.0.0.1.1.1:851)", text);
    }

    [Fact]
    public void Coordinator_Batch_UsesDefaultPort851WhenNotSpecified()
    {
        var fake = new FakeTwinCatSession();

        _ = new AdsVariableCoordinator(fake).ReadAdsVariablesBatch(new[] { "MAIN.x" }, null, null);

        Assert.Contains("port=851", fake.CallLog[0]);
    }
}

// ibn-live: twincat_measure_signal_transition — session plumbing + JSON shaping.
public class SignalTransitionTests
{
    [Fact]
    public void Measure_HappyPath_RecordsCallAndReturnsResult()
    {
        var fake = new FakeTwinCatSession
        {
            FakeMeasureSignalTransitionResult = new SignalTransitionResult(true, 250.5, false, "OK"),
        };

        var r = fake.MeasureSignalTransition("A", "true", "B", "true", 5000, null, 851);

        Assert.Contains("MeasureSignalTransition(start=A==true, end=B==true", fake.CallLog[0]);
        Assert.True(r.Success);
        Assert.Equal(250.5, r.ElapsedMs);
        Assert.False(r.TimedOut);
    }

    [Fact]
    public void Coordinator_Measure_SerializesAsJson()
    {
        var fake = new FakeTwinCatSession
        {
            FakeMeasureSignalTransitionResult = new SignalTransitionResult(true, 42.0, false, "OK"),
        };

        var json = new AdsVariableCoordinator(fake).MeasureSignalTransition(
            "GVL_IO.bCyl1Cmd", "true", "GVL_IO.bCyl1Extended", "true", 3000, null, null);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(42.0, root.GetProperty("elapsedMs").GetDouble());
        Assert.False(root.GetProperty("timedOut").GetBoolean());
    }

    [Fact]
    public void Coordinator_Measure_TimedOut_StillSuccessWithFlag()
    {
        var fake = new FakeTwinCatSession
        {
            FakeMeasureSignalTransitionResult = new SignalTransitionResult(
                true, 5000.0, true, "Timed out waiting for end condition 'B'=='true'."),
        };

        var json = new AdsVariableCoordinator(fake).MeasureSignalTransition(
            "A", "true", "B", "true", 5000, null, null);

        Assert.DoesNotContain("[Error]", json);
        Assert.True(JsonDocument.Parse(json).RootElement.GetProperty("timedOut").GetBoolean());
    }

    [Fact]
    public void Coordinator_Measure_EmptySymbols_ReturnsErrorAndDoesNotCallSession()
    {
        var fake = new FakeTwinCatSession();

        var s1 = new AdsVariableCoordinator(fake).MeasureSignalTransition("", "true", "B", "true", null, null, null);
        var s2 = new AdsVariableCoordinator(fake).MeasureSignalTransition("A", "true", "  ", "true", null, null, null);

        Assert.StartsWith("[Error]", s1);
        Assert.StartsWith("[Error]", s2);
        Assert.DoesNotContain(fake.CallLog, c => c.StartsWith("MeasureSignalTransition", StringComparison.Ordinal));
    }

    [Fact]
    public void Coordinator_Measure_ClampsTimeoutAndDefaultsEquals()
    {
        var fake = new FakeTwinCatSession();

        _ = new AdsVariableCoordinator(fake).MeasureSignalTransition("A", null, "B", null, 999999, null, null);

        // timeout clamped to 60000, missing *Equals default to "true".
        Assert.Contains("timeout=60000", fake.CallLog[0]);
        Assert.Contains("start=A==true, end=B==true", fake.CallLog[0]);
    }

    [Fact]
    public void Coordinator_Measure_Failure_ReturnsErrorString()
    {
        var fake = new FakeTwinCatSession
        {
            FakeMeasureSignalTransitionResult = new SignalTransitionResult(
                false, 0, false, "MeasureSignalTransition failed: not reachable."),
        };

        var text = new AdsVariableCoordinator(fake).MeasureSignalTransition(
            "A", "true", "B", "true", null, null, null);

        Assert.StartsWith("[Error]", text);
        Assert.Contains("not reachable", text);
    }
}
