using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

/// <summary>Stands in for the host popup: answers with <see cref="Outcome"/> and
/// records every request so tests can assert what the engineer would have seen.</summary>
internal sealed class FakeHmiConfirmer(HmiConfirmOutcome outcome = HmiConfirmOutcome.Confirmed) : IHmiWriteConfirmer
{
    public HmiConfirmOutcome Outcome { get; set; } = outcome;
    public List<(HmiWriteRequest Request, TimeSpan Timeout)> Requests { get; } = new();

    public HmiConfirmOutcome Confirm(HmiWriteRequest request, TimeSpan timeout)
    {
        Requests.Add((request, timeout));
        return Outcome;
    }
}
