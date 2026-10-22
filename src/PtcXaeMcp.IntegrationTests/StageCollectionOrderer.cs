using Xunit;
using Xunit.Abstractions;

[assembly: TestCollectionOrderer(
    "PtcXaeMcp.IntegrationTests.StageCollectionOrderer",
    "PtcXaeMcp.IntegrationTests")]

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Orders test collections: XAE → XAR → Scope → Motion → HMI. HMI runs last because
/// its fixture closes the open solution and opens a TEMP HMI project.
/// Collections not matching a known stage name sort first (rank 0).
/// </summary>
public sealed class StageCollectionOrderer : ITestCollectionOrderer
{
    private static int Rank(string name) => name switch
    {
        "Stage XAE"    => 1,
        "Stage XAR"    => 2,
        "Stage Scope"  => 3,
        "Stage Motion" => 4,
        "Stage HMI"    => 5,
        _ => 0,
    };

    public IEnumerable<ITestCollection> OrderTestCollections(
        IEnumerable<ITestCollection> testCollections)
        => testCollections.OrderBy(c => Rank(c.DisplayName));
}
