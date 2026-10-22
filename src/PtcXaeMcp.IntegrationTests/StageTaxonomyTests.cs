using System.Reflection;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Offline meta-guard: reflects over this assembly and asserts that every
/// <c>[Collection]</c>-bearing test class carries <b>exactly one</b>
/// <c>[Trait("Stage", X)]</c> whose value
/// <list type="bullet">
///   <item>is one of the five known stages (XAE, XAR, Scope, Motion, HMI), and</item>
///   <item>matches the collection name ("Stage X" → trait "X").</item>
/// </list>
/// Prevents a new test class from silently escaping the stage taxonomy.
/// Rides along in Stage XAE (always-on, offline — no live XAE needed).
/// </summary>
[Collection("Stage XAE")]
[Trait("Stage", "XAE")]
public class StageTaxonomyTests
{
    private static readonly IReadOnlySet<string> KnownStages =
        new HashSet<string>(StringComparer.Ordinal) { "XAE", "XAR", "Scope", "Motion", "HMI" };

    [Fact]
    [Trait("Description", "Offline-Guard: jede [Collection]-Klasse trägt genau ein Stage-Trait, Wert ∈ {XAE, XAR, Scope, Motion, HMI}, passend zum Collection-Namen.")]
    public void Every_Collection_Class_Has_Exactly_One_Matching_Stage_Trait()
    {
        var assembly = typeof(StageTaxonomyTests).Assembly;
        var violations = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            // Only test classes with a [Collection] attribute
            var collectionAttr = type.CustomAttributes
                .FirstOrDefault(a => a.AttributeType.Name == "CollectionAttribute");
            if (collectionAttr is null) continue;

            // Extract the collection name (first constructor arg)
            var collectionName = collectionAttr.ConstructorArguments
                .FirstOrDefault().Value as string;
            if (collectionName is null) continue;

            // Collect all [Trait("Stage", "…")] values on this class
            var stageTraits = type.CustomAttributes
                .Where(a => a.AttributeType.Name == "TraitAttribute")
                .Select(a => new
                {
                    Key   = a.ConstructorArguments.Count >= 1 ? a.ConstructorArguments[0].Value as string : null,
                    Value = a.ConstructorArguments.Count >= 2 ? a.ConstructorArguments[1].Value as string : null,
                })
                .Where(t => t.Key == "Stage")
                .Select(t => t.Value)
                .OfType<string>()
                .ToList();

            if (stageTraits.Count == 0)
            {
                violations.Add($"  {type.Name}: [Collection(\"{collectionName}\")] has NO Stage trait.");
                continue;
            }

            if (stageTraits.Count > 1)
            {
                violations.Add($"  {type.Name}: has {stageTraits.Count} Stage traits ({string.Join(", ", stageTraits)}); must have exactly one.");
                continue;
            }

            var stageValue = stageTraits[0];

            if (!KnownStages.Contains(stageValue))
            {
                violations.Add($"  {type.Name}: Stage trait \"{stageValue}\" is not a known stage (XAE|XAR|Scope|Motion|HMI).");
                continue;
            }

            // Collection "Stage X" must match Trait "Stage" → "X"
            var expectedCollectionName = $"Stage {stageValue}";
            if (!string.Equals(collectionName, expectedCollectionName, StringComparison.Ordinal))
            {
                violations.Add($"  {type.Name}: [Collection(\"{collectionName}\")] but [Trait(\"Stage\", \"{stageValue}\")] — expected collection \"{expectedCollectionName}\".");
            }
        }

        Assert.True(violations.Count == 0,
            "Stage taxonomy violations found:\n" + string.Join("\n", violations) + "\n\n" +
            "Every [Collection(\"Stage X\")] class must carry [Trait(\"Stage\", \"X\")] " +
            "with X ∈ {XAE, XAR, Scope, Motion, HMI}.");
    }
}
