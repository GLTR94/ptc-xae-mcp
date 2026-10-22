namespace PtcXaeMcp.Tests;

// ┌──────────────────────────────────────────────────────────────────┐
// │  FAKE-ONLY PROJECT — these tests NEVER touch TwinCAT.              │
// │  All TwinCAT interaction here goes through FakeTwinCatSession.     │
// │  For real end-to-end coverage against a running XAE, run the       │
// │  PtcXaeMcp.IntegrationTests project as the live suite.             │
// └──────────────────────────────────────────────────────────────────┘
public class PlaceholderTests
{
    public PlaceholderTests() =>
        Console.WriteLine(
            "▶ PtcXaeMcp.Tests — FAKE TwinCAT (FakeTwinCatSession). " +
            "NOT a live XAE run.");

    [Fact]
    public void ProjectBuilds() => Assert.True(true);
}
