using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// <see cref="TwinCatComSession.ParseBroadcastResult"/> — issue #157 item 7 (a missing test
/// the issue called out by name). The method is already <c>internal static</c>, so it is
/// reachable from this project via the existing <c>InternalsVisibleTo</c>.
///
/// <para>
/// C-11 (issue #152): the method's four-selector chain (<c>//TreeItem/RoutePrj/TargetList/
/// Target</c>, then <c>//TargetList/Target</c>, <c>//TargetList/SearchTarget</c>, <c>//Target
/// </c>) previously used <c>??</c> to fall through, but
/// <see cref="System.Xml.XmlNode.SelectNodes(string)"/> returns an empty (non-null)
/// <c>XmlNodeList</c> when nothing matches rather than <c>null</c> — so a selector only "fell
/// through" when the XPath itself threw, never merely because it found 0 nodes, making the
/// 2nd/3rd/4th selectors dead code for any real document. Fixed to chain on
/// <c>nodes.Count == 0</c> instead.
/// <see cref="FallbackSelectors_EngageWhenTheFullPathDoesNotMatch"/> pins down the fixed
/// behaviour: a document shaped for a narrower selector alone (no TreeItem/RoutePrj ancestor)
/// now actually falls through and parses.
/// </para>
/// </summary>
public sealed class ParseBroadcastResultTests
{
    [Fact]
    public void FullPath_ElementChildren_ParsesAllFields()
    {
        const string xml = """
            <TreeItem><RoutePrj><TargetList>
              <Target>
                <Name>PLC1</Name>
                <NetId>5.1.2.3.1.1</NetId>
                <IpAddr>192.168.1.10</IpAddr>
                <Version>3.1.4026</Version>
                <OS>TwinCAT RT (x64)</OS>
              </Target>
            </TargetList></RoutePrj></TreeItem>
            """;

        var (targets, rawXml) = TwinCatComSession.ParseBroadcastResult(xml);

        Assert.Same(xml, rawXml);
        Assert.Single(targets);
        var t = targets[0];
        Assert.Equal("PLC1", t.Name);
        Assert.Equal("5.1.2.3.1.1", t.NetId);
        Assert.Equal("192.168.1.10", t.IpAddr);
        Assert.Equal("3.1.4026", t.Version);
        Assert.Equal("TwinCAT RT (x64)", t.Os);
    }

    [Fact]
    public void FullPath_MultipleTargets_ParsesAllOfThem()
    {
        const string xml = """
            <TreeItem><RoutePrj><TargetList>
              <Target><Name>PLC1</Name></Target>
              <Target><Name>PLC2</Name></Target>
            </TargetList></RoutePrj></TreeItem>
            """;

        var (targets, _) = TwinCatComSession.ParseBroadcastResult(xml);

        Assert.Equal(2, targets.Count);
        Assert.Equal("PLC1", targets[0].Name);
        Assert.Equal("PLC2", targets[1].Name);
    }

    [Fact]
    public void FullPath_AttributeValues_AreUsedWhenChildElementsAreAbsent()
    {
        const string xml = """
            <TreeItem><RoutePrj><TargetList>
              <Target Name="PLC5" NetId="5.1.2.3.1.5" IpAddr="10.0.0.5" Version="3.1.4024" OS="Windows 10"/>
            </TargetList></RoutePrj></TreeItem>
            """;

        var (targets, _) = TwinCatComSession.ParseBroadcastResult(xml);

        Assert.Single(targets);
        var t = targets[0];
        Assert.Equal("PLC5", t.Name);
        Assert.Equal("5.1.2.3.1.5", t.NetId);
        Assert.Equal("10.0.0.5", t.IpAddr);
        Assert.Equal("3.1.4024", t.Version);
        Assert.Equal("Windows 10", t.Os);
    }

    [Fact]
    public void FullPath_MissingFields_DefaultToEmptyString_NotNull()
    {
        const string xml = "<TreeItem><RoutePrj><TargetList><Target><Name>OnlyName</Name></Target></TargetList></RoutePrj></TreeItem>";

        var (targets, _) = TwinCatComSession.ParseBroadcastResult(xml);

        var t = Assert.Single(targets);
        Assert.Equal("OnlyName", t.Name);
        Assert.Equal("", t.NetId);
        Assert.Equal("", t.IpAddr);
        Assert.Equal("", t.Version);
        Assert.Equal("", t.Os);
    }

    [Fact]
    public void NoMatchingNodesAnywhere_ReturnsEmptyListAndOriginalRawXml()
    {
        const string xml = "<Root><SomethingElse/></Root>";

        var (targets, rawXml) = TwinCatComSession.ParseBroadcastResult(xml);

        Assert.Empty(targets);
        Assert.Same(rawXml, rawXml);
        Assert.Same(xml, rawXml);
    }

    [Fact]
    public void MalformedXml_DoesNotThrow_ReturnsEmptyListAndOriginalRawXml()
    {
        const string xml = "<Root><Unclosed>";

        var (targets, rawXml) = TwinCatComSession.ParseBroadcastResult(xml);

        Assert.Empty(targets);
        Assert.Same(xml, rawXml);
    }

    [Fact]
    public void FallbackSelectors_EngageWhenTheFullPathDoesNotMatch()
    {
        // Fixed behaviour (C-11, issue #152): a document shaped for the 2nd/3rd/4th selector
        // alone (no TreeItem/RoutePrj ancestor) now falls through to it and parses, instead of
        // the whole chain silently freezing on the first (empty) selector result.
        const string bareTargetList = "<Root><TargetList><Target><Name>X</Name></Target></TargetList></Root>";
        const string bareSearchTarget = "<Root><TargetList><SearchTarget><Name>X</Name></SearchTarget></TargetList></Root>";
        const string bareTarget = "<Root><Target><Name>X</Name></Target></Root>";

        Assert.Equal("X", Assert.Single(TwinCatComSession.ParseBroadcastResult(bareTargetList).Targets).Name);
        Assert.Equal("X", Assert.Single(TwinCatComSession.ParseBroadcastResult(bareSearchTarget).Targets).Name);
        Assert.Equal("X", Assert.Single(TwinCatComSession.ParseBroadcastResult(bareTarget).Targets).Name);
    }

    [Fact]
    public void FullPathMatches_FallbackSelectorsDoNotAlsoDoubleCount()
    {
        // The full path (//TreeItem/RoutePrj/TargetList/Target) also matches //TargetList/Target
        // and //Target as XPath supersets, but once the first selector finds nodes the chain
        // must stop there, not re-run and duplicate/replace them with a broader selector's
        // (identical, in this case) result via redundant work.
        const string xml = """
            <TreeItem><RoutePrj><TargetList>
              <Target><Name>PLC1</Name></Target>
            </TargetList></RoutePrj></TreeItem>
            """;

        var (targets, _) = TwinCatComSession.ParseBroadcastResult(xml);

        Assert.Single(targets);
    }
}
