using System.Globalization;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #153 (D-5/D-6/D-24): scope property writes are converted and read back invariantly.</summary>
public class ScopePropertyValuesTests
{
    [Fact]
    public void CoerceTo_UsesTheCurrentValueType()
    {
        Assert.Equal(true, ScopePropertyValues.CoerceTo(false, "True"));
        Assert.Equal(851, ScopePropertyValues.CoerceTo(0, 851L));
        Assert.Equal(0.5, ScopePropertyValues.CoerceTo(1.0, "0.5"));
        Assert.Equal("10", ScopePropertyValues.CoerceTo("x", 10));
        Assert.Equal(DayOfWeek.Friday, ScopePropertyValues.CoerceTo(DayOfWeek.Monday, "friday"));
    }

    [Fact]
    public void CoerceTo_Unconvertible_ReturnsTheValue_SoTheAssignmentReportsIt()
        => Assert.Equal("abc", ScopePropertyValues.CoerceTo(1.0, "abc"));

    [Fact]
    public void Numbers_AreInvariant_UnderAGermanCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-CH");
            Assert.Equal(1.5, ScopePropertyValues.CoerceTo(0.0, "1.5"));
            Assert.True(ScopePropertyValues.SameValue(1.5, "1.5"));
            Assert.True(ScopePropertyValues.SameValue("1.5", 1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Custom", "custom", true)]
    [InlineData("Name", "Custom", false)]
    [InlineData("10", 10, true)]
    [InlineData("10.0", 10, true)]
    [InlineData("11", 10, false)]
    public void SameValue_ComparesTheReadBack(object back, object written, bool expected)
        => Assert.Equal(expected, ScopePropertyValues.SameValue(back, written));

    [Fact]
    public void SameValue_Bools() => Assert.True(ScopePropertyValues.SameValue(true, true));

    // fix/scope-units: Record.Record Time holds 100 ns ticks (live 2026-10-06: writing 2 was rejected and
    // read back the template default 10000000 = 1 s; writing 20000000 was accepted and saved as
    // <RecordTime>20000000</RecordTime>). recordTimeS is converted before the write.
    [Theory]
    [InlineData(1.0, 10_000_000L)]
    [InlineData(2.0, 20_000_000L)]
    [InlineData(0.5, 5_000_000L)]
    [InlineData(600.0, 6_000_000_000L)]
    public void SecondsToTicks_Converts100nsTicks(double seconds, long ticks)
    {
        Assert.Equal(ticks, ScopeTime.SecondsToTicks(seconds));
        Assert.Equal(seconds, ScopeTime.TicksToSeconds(ticks), 9);
    }

    public enum FakeTriggerAction { NONE = 0, START_RECORD = 1, STOP_RECORD = 2 }

    // TE130x enums use SCREAMING_SNAKE names (TriggerEventAction.STOP_RECORD); the tools pass "StopRecord".
    [Theory]
    [InlineData("StopRecord", FakeTriggerAction.STOP_RECORD)]
    [InlineData("STOP_RECORD", FakeTriggerAction.STOP_RECORD)]
    [InlineData("startrecord", FakeTriggerAction.START_RECORD)]
    public void CoerceTo_Enum_MatchesNamesLoosely(string written, FakeTriggerAction expected)
    {
        Assert.Equal(expected, ScopePropertyValues.CoerceTo(FakeTriggerAction.NONE, written));
        Assert.True(ScopePropertyValues.SameValue(expected, written));
    }

    [Fact]
    public void CoerceTo_Enum_UnknownName_LeavesTheValueForTheAssignmentToReject()
        => Assert.Equal("Nonsense", ScopePropertyValues.CoerceTo(FakeTriggerAction.NONE, "Nonsense"));

    [Theory]
    [InlineData("StopRecord", 2)]
    [InlineData("stopdisplay", 3)]
    [InlineData("RestartDisplay", 4)]
    [InlineData("StartSubsave", 5)]
    [InlineData("StopSubsave", 6)]
    public void TriggerAction_MapsToTE130xEventActionId(string action, int id)
        => Assert.Equal(id, ScopeTriggerAction.ToEventActionId(action));

    [Fact]
    public void TriggerAction_EventMarker_IsNotGuessed()
        => Assert.Null(ScopeTriggerAction.ToEventActionId("EventMarker"));

    [Theory]
    [InlineData("Rising", 0)]
    [InlineData("falling", 1)]
    public void TriggerEdge_MapsToTE130xReleaseOption(string edge, int id)
        => Assert.Equal(id, ScopeTriggerCondition.ToReleaseId(edge));

    [Fact]
    public void TriggerEdge_Both_IsNotGuessed()
        => Assert.Null(ScopeTriggerCondition.ToReleaseId("Both"));
}
