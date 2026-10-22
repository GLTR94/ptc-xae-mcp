using System.Reflection;
using Xunit.Sdk;

namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Assembly-wide xUnit hook that narrates the live run. Applied once via
/// <c>[assembly: LiveTestTrace]</c> (see <c>AssemblyInfo.cs</c>), so xUnit
/// invokes it around <b>every</b> test in this assembly: <see cref="Before"/>
/// sends a <c>start</c> marker carrying the test's <c>[Trait("Description")]</c>
/// narration, <see cref="After"/> sends an <c>end</c> marker. The host's MCP
/// Activity tab uses these to show "▶ Läuft (i/N): test — description" live.
/// <para>
/// Delivery is best-effort over a shared client (see <see cref="LiveTestMarker"/>);
/// when the host is unreachable the markers no-op and tests are unaffected.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method,
                AllowMultiple = true)]
public sealed class LiveTestTraceAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest)
        => LiveTestMarker.Start(methodUnderTest.Name, NarratedDescription(methodUnderTest));

    public override void After(MethodInfo methodUnderTest)
        => LiveTestMarker.End(methodUnderTest.Name);

    /// <summary>
    /// Builds the narration string: "Stage XAE — description" (or just the
    /// description when the class has no Stage trait, e.g. offline tests).
    /// </summary>
    private static string? NarratedDescription(MethodInfo method)
    {
        var description = TraitValue(method, "Description");
        var stage       = method.DeclaringType is { } t ? TraitValue(t, "Stage") : null;
        return (stage, description) switch
        {
            (not null, not null) => $"Stage {stage} — {description}",
            (not null, null)     => $"Stage {stage}",
            (null,     not null) => description,
            _                    => null,
        };
    }

    /// <summary>
    /// Reads a <c>[Trait("key", "value")]</c> from a <see cref="MemberInfo"/>.
    /// xUnit's <see cref="TraitAttribute"/> exposes no public Name/Value
    /// properties — values live in the constructor arguments.
    /// </summary>
    private static string? TraitValue(MemberInfo member, string key)
    {
        foreach (var data in member.CustomAttributes)
        {
            if (data.AttributeType.Name != nameof(TraitAttribute)) continue;
            var args = data.ConstructorArguments;
            if (args.Count == 2 && args[0].Value as string == key)
                return args[1].Value as string;
        }
        return null;
    }
}
