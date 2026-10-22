using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #157 item 1: twincat_get_log is bounded by default.</summary>
public class LogTailTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Tail_NoCapOrShortText_ReturnsTextUnchanged(int maxChars)
    {
        const string log = "line 1\nline 2";

        LogTail.Tail(log, maxChars).Should().BeSameAs(log);
    }

    [Fact]
    public void Tail_LongText_KeepsNewestCharactersWithANote()
    {
        string log = new string('o', 30) + "newest";

        string tail = LogTail.Tail(log, 10);

        tail.Should().StartWith("[26 earlier characters omitted");
        tail.Should().EndWith("\noooonewest");
    }
}
