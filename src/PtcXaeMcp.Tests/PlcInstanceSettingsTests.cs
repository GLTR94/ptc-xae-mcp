using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// plc-instance-settings: XML parsing/building against the shapes the XAE produced live (2026-10-08),
/// resolution of the PLC project/instance by ItemType, and the read-back of every write.
/// </summary>
public sealed class PlcInstanceSettingsTests
{
    private const string Project = "TIPC^FakePlc";
    private const string Instance = "TIPC^FakePlc^FakePlc Instance";

    // Trimmed copy of a live ProduceXml(false) of the fixture's instance after a build.
    internal const string InstanceXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>FakePlc Instance</ItemName><ItemType>57</ItemType>" +
        "<TcModuleInstance><Module><DataAreas>" +
        "<DataArea><AreaNo AreaType=\"InputDst\" CreateSymbols=\"true\">0</AreaNo><Name>PlcTask Inputs</Name><ByteSize>10616832</ByteSize>" +
        "<Symbol><Name>GVL_Fixture.bInputSignal</Name><BitSize>8</BitSize><BaseType>BOOL</BaseType><BitOffs>3099648</BitOffs></Symbol></DataArea>" +
        "<DataArea><AreaNo AreaType=\"Internal\" CreateSymbols=\"true\">3</AreaNo><Name>PlcTask Internal</Name>" +
        "<Symbol><Name>GVL_Fixture.nInitParam</Name><BitSize>32</BitSize><BaseType GUID=\"{18071995-0000-0000-0000-000000000009}\">DINT</BaseType>" +
        "<Default><Value>10</Value></Default><Properties><Property><Name>TcVarGlobal</Name></Property><Property><Name>TcInitSymbol</Name></Property></Properties>" +
        "<BitOffs>3101120</BitOffs></Symbol>" +
        "<Symbol><Name>GVL_Fixture.fInitGain</Name><BitSize>64</BitSize><BaseType>LREAL</BaseType>" +
        "<Properties><Property><Name>TcInitSymbol</Name></Property></Properties><BitOffs>3101184</BitOffs></Symbol></DataArea>" +
        "<DataArea><AreaNo AreaType=\"RetainSrc\" CreateSymbols=\"true\">4</AreaNo><Name>PlcTask Retains</Name><ContextId>0</ContextId><ByteSize>10616832</ByteSize>" +
        "<Symbol><Name>GVL_Fixture.nRetainCounter</Name><BitSize>32</BitSize><BaseType GUID=\"{18071995-0000-0000-0000-000000000008}\">UDINT</BaseType>" +
        "<Properties><Property><Name>TcVarGlobal</Name></Property></Properties><BitOffs>3101152</BitOffs></Symbol></DataArea>" +
        "</DataAreas><InstanceName>FakePlc Instance</InstanceName>" +
        "<InitSymbolValues><Value><Name>GVL_Fixture.nInitParam</Name><Value>10</Value></Value>" +
        "<Value><Name>GVL_Fixture.fInitGain</Name><Value>1.5</Value></Value></InitSymbolValues>" +
        "</Module></TcModuleInstance></TreeItem>";

    internal const string UnbuiltInstanceXml =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>FakePlc Instance</ItemName><ItemType>57</ItemType></TreeItem>";

    internal static string ProjectXml(bool tmc, bool tpy) =>
        "<?xml version=\"1.0\"?><TreeItem><ItemName>FakePlc</ItemName><ItemType>56</ItemType><PlcProjectDef>" +
        "<ProjectPath>C:\\x\\FakePlc.plcproj</ProjectPath><RelativeTmcPath>FakePlc\\FakePlc.tmc</RelativeTmcPath>" +
        $"<TmcFileCopy>{(tmc ? "true" : "false")}</TmcFileCopy><TpyFileCopy>{(tpy ? "true" : "false")}</TpyFileCopy>" +
        "</PlcProjectDef></TreeItem>";

    private static FakeTwinCatSession OpenFake(string instanceXml = InstanceXml)
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        fake.TreeChildren["TIPC"] = [new TreeChildInfo("FakePlc", 56)];
        fake.TreeChildren[Project] = [new TreeChildInfo("PlcTask", 58), new TreeChildInfo("FakePlc Instance", 57)];
        fake.TreeItemXml[Instance] = instanceXml;
        fake.TreeItemXml[Project] = ProjectXml(false, false);
        return fake;
    }

    private static PlcInstanceSettingsCoordinator Coordinator(FakeTwinCatSession fake) => new(fake);

    // ---- XML: init symbols ---------------------------------------------------------------

    [Fact]
    public void ParseInitSymbols_ReadsNamesValuesAndTypes()
    {
        var symbols = PlcInstanceSettingsXml.ParseInitSymbols(InstanceXml)!;
        symbols.Should().BeEquivalentTo(new[]
        {
            new InitSymbolValue("GVL_Fixture.nInitParam", "10", "DINT"),
            new InitSymbolValue("GVL_Fixture.fInitGain", "1.5", "LREAL"),
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ParseInitSymbols_UnbuiltInstance_IsNull() =>
        PlcInstanceSettingsXml.ParseInitSymbols(UnbuiltInstanceXml).Should().BeNull();

    [Fact]
    public void ParseInitSymbols_ModuleWithoutInitValues_IsEmpty() =>
        PlcInstanceSettingsXml.ParseInitSymbols("<TreeItem><TcModuleInstance><Module/></TcModuleInstance></TreeItem>")
            .Should().BeEmpty();

    [Fact]
    public void ParseInitSymbols_BrokenXml_Throws() =>
        FluentActions.Invoking(() => PlcInstanceSettingsXml.ParseInitSymbols("<TreeItem>"))
            .Should().Throw<InvalidOperationException>().WithMessage("*cannot be parsed*");

    // ---- read_init_symbols -------------------------------------------------------------

    [Fact]
    public void ReadInitSymbols_ListsEverySymbol()
    {
        var fake = OpenFake();
        string text = Coordinator(fake).ReadInitSymbols(null);
        text.Should().Contain("2 init symbol(s)")
            .And.Contain("GVL_Fixture.nInitParam : DINT = 10")
            .And.Contain("GVL_Fixture.fInitGain : LREAL = 1.5");
    }

    [Fact]
    public void ReadInitSymbols_FindsInstanceByItemType_NotByLabel()
    {
        var fake = OpenFake();
        fake.TreeChildren[Project] = [new TreeChildInfo("FakePlc Instanz", 57)];
        fake.TreeItemXml["TIPC^FakePlc^FakePlc Instanz"] = InstanceXml;
        Coordinator(fake).ReadInitSymbols("fakeplc").Should().Contain("TIPC^FakePlc^FakePlc Instanz");
    }

    [Fact]
    public void ReadInitSymbols_NoSolution_IsError() =>
        Coordinator(new FakeTwinCatSession { IsConnected = true }).ReadInitSymbols(null)
            .Should().StartWith("[Error] No solution");

    [Fact]
    public void ReadInitSymbols_UnknownProject_ListsPresent() =>
        Coordinator(OpenFake()).ReadInitSymbols("Other")
            .Should().StartWith("[Error] No PLC project 'Other'").And.Contain("FakePlc");

    [Fact]
    public void ReadInitSymbols_TwoProjectsWithoutName_AsksForName()
    {
        var fake = OpenFake();
        fake.TreeChildren["TIPC"].Add(new TreeChildInfo("Plc2", 56));
        Coordinator(fake).ReadInitSymbols(null).Should().Contain("pass plcProjectName");
    }

    [Fact]
    public void ReadInitSymbols_NoInstance_IsError()
    {
        var fake = OpenFake();
        fake.TreeChildren[Project] = [];
        Coordinator(fake).ReadInitSymbols(null).Should().StartWith("[Error]").And.Contain("no PLC instance");
    }

    [Fact]
    public void ReadInitSymbols_Unbuilt_SaysBuildFirst() =>
        Coordinator(OpenFake(UnbuiltInstanceXml)).ReadInitSymbols(null)
            .Should().StartWith("[Error]").And.Contain("twincat_build_plc");

    // ---- XML: building / comparing ----------------------------------------------------

    [Fact]
    public void BuildInitSymbolXml_UsesTheShapeTheXaeApplies_AndEscapes()
    {
        string xml = PlcInstanceSettingsXml.BuildInitSymbolXml("GVL.s<1>", "'a&b'");
        xml.Should().Be("<TreeItem><TcModuleInstance><Module><InitSymbolValues><Value><Name>GVL.s&lt;1&gt;</Name>" +
                        "<Value>'a&amp;b'</Value></Value></InitSymbolValues></Module></TcModuleInstance></TreeItem>");
    }

    [Theory]
    [InlineData("42", "42", true)]
    [InlineData(" 1.50 ", "1.5", true)]
    [InlineData("TRUE", "true", true)]
    [InlineData("TRUE", "1", true)]
    [InlineData("99999999999", "1215752191", false)]
    [InlineData("abc", "10", false)]
    [InlineData("eMode.Auto", "EMODE.AUTO", true)]
    public void ValuesEqual(string requested, string actual, bool equal) =>
        PlcInstanceSettingsXml.ValuesEqual(requested, actual).Should().Be(equal);

    // ---- write_init_symbol -------------------------------------------------------------

    private static string InitValueIn(FakeTwinCatSession fake, string name) =>
        PlcInstanceSettingsXml.ParseInitSymbols(fake.TreeItemXml[Instance])!.Single(s => s.Name == name).Value;

    [Fact]
    public void WriteInitSymbol_LogsOutWritesReadsBackAndSaves()
    {
        var fake = OpenFake();
        string text = Coordinator(fake).WriteInitSymbol("gvl_fixture.ninitparam", "42", null);

        text.Should().StartWith("Set init value GVL_Fixture.nInitParam (DINT) = 42 (was 10)")
            .And.Contain("read-back: 42").And.Contain("saved").And.Contain("Activate Configuration");
        InitValueIn(fake, "GVL_Fixture.nInitParam").Should().Be("42");
        InitValueIn(fake, "GVL_Fixture.fInitGain").Should().Be("1.5");
        fake.CallLog.Should().ContainInOrder("LogoutPlc()", $"ConsumeTreeItemXml({Instance})", "SaveAll()");
    }

    [Fact]
    public void WriteInitSymbol_NotAnInitSymbol_IsRefusedBeforeTheXae()
    {
        var fake = OpenFake();
        Coordinator(fake).WriteInitSymbol("GVL_Fixture.nCount", "7", null)
            .Should().StartWith("[Error] 'GVL_Fixture.nCount' is not an init symbol").And.Contain("GVL_Fixture.nInitParam");
        fake.CallLog.Should().NotContain(c => c == "LogoutPlc()" || c.StartsWith("ConsumeTreeItemXml", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "1", "symbol is required")]
    [InlineData("GVL_Fixture.nInitParam", " ", "value is required")]
    public void WriteInitSymbol_MissingArgs(string symbol, string value, string message)
    {
        var fake = OpenFake();
        Coordinator(fake).WriteInitSymbol(symbol, value, null).Should().Contain(message);
        fake.ConsumedXml.Should().BeEmpty();
    }

    [Fact]
    public void WriteInitSymbol_SilentNoOp_IsAnError()
    {
        var fake = OpenFake();
        fake.ConsumeXmlApplier = null; // XAE accepts the call but applies nothing
        Coordinator(fake).WriteInitSymbol("GVL_Fixture.nInitParam", "42", null)
            .Should().StartWith("[Error] The XAE did not store '42'").And.Contain("read-back is '10'").And.Contain("old value is unchanged");
        fake.CallLog.Should().NotContain("SaveAll()");
    }

    [Fact]
    public void WriteInitSymbol_WrappedValue_IsRestored()
    {
        var fake = OpenFake();
        fake.ConsumeXmlApplier = (path, current, payload) =>
            FakeTwinCatSession.DefaultApplier(path, current, payload.Replace(">99999999999<", ">1215752191<", StringComparison.Ordinal));
        Coordinator(fake).WriteInitSymbol("GVL_Fixture.nInitParam", "99999999999", null)
            .Should().StartWith("[Error]").And.Contain("1215752191").And.Contain("restored the old value '10'");
        InitValueIn(fake, "GVL_Fixture.nInitParam").Should().Be("10");
        fake.ConsumedXml.Should().HaveCount(2);
    }

    [Fact]
    public void WriteInitSymbol_StillOnline_IsRefused()
    {
        var fake = OpenFake();
        fake.FakeLogoutPlcResult = new LogoutPlcResult(false, true, "still online");
        Coordinator(fake).WriteInitSymbol("GVL_Fixture.nInitParam", "42", null).Should().StartWith("[Error]");
        fake.ConsumedXml.Should().BeEmpty();
    }

    [Fact]
    public void WriteInitSymbol_ConsumeThrows_IsAnError()
    {
        var fake = OpenFake();
        fake.ConsumeTreeItemXmlThrows = new InvalidOperationException("ConsumeXml failed on 'x': boom");
        Coordinator(fake).WriteInitSymbol("GVL_Fixture.nInitParam", "42", null).Should().StartWith("[Error]").And.Contain("boom");
    }

    // ---- retain ------------------------------------------------------------------------

    [Fact]
    public void ParseRetainAreas_OnlyRetainSrc()
    {
        var areas = PlcInstanceSettingsXml.ParseRetainAreas(InstanceXml)!;
        areas.Should().ContainSingle();
        areas[0].Should().BeEquivalentTo(new RetainArea("PlcTask Retains", 4, 10616832,
            [new RetainSymbol("GVL_Fixture.nRetainCounter", "UDINT", 32, 3101152)]));
    }

    [Fact]
    public void ParseRetainMappings_PassesChildrenThrough()
    {
        const string xml = "<TreeItem><TcModuleInstance><Module/></TcModuleInstance>" +
                           "<RetainDataMappings><Mapping><Name>a</Name></Mapping></RetainDataMappings></TreeItem>";
        PlcInstanceSettingsXml.ParseRetainMappings(xml).Should().Equal("<Mapping><Name>a</Name></Mapping>");
        PlcInstanceSettingsXml.ParseRetainMappings(InstanceXml).Should().BeEmpty();
    }

    [Fact]
    public void ReadRetainConfig_ListsAreaAndSymbol_AndNeverWrites()
    {
        var fake = OpenFake();
        string text = Coordinator(fake).ReadRetainConfig(null);
        text.Should().Contain("1 retain area(s), 1 symbol(s)")
            .And.Contain("area 'PlcTask Retains' (AreaNo 4, 10616832 bytes)")
            .And.Contain("GVL_Fixture.nRetainCounter : UDINT (BitSize 32, BitOffs 3101152)")
            .And.Contain("RetainDataMappings: none");
        fake.CallLog.Should().NotContain(c => c == "LogoutPlc()" || c == "SaveAll()" || c.StartsWith("ConsumeTreeItemXml", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadRetainConfig_NoRetain_SaysSo() =>
        Coordinator(OpenFake("<TreeItem><TcModuleInstance><Module><DataAreas/></Module></TcModuleInstance></TreeItem>"))
            .ReadRetainConfig(null).Should().Contain("no retain area");

    [Fact]
    public void ReadRetainConfig_Unbuilt_SaysBuildFirst() =>
        Coordinator(OpenFake(UnbuiltInstanceXml)).ReadRetainConfig(null).Should().StartWith("[Error]").And.Contain("build");

    // ---- type files --------------------------------------------------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ParseTypeFileFlags(bool tmc, bool tpy) =>
        PlcInstanceSettingsXml.ParseTypeFileFlags(ProjectXml(tmc, tpy)).Should().Be(new TypeFileFlags(tmc, tpy));

    [Fact]
    public void ParseTypeFileFlags_NoProjectDef_IsNull() =>
        PlcInstanceSettingsXml.ParseTypeFileFlags("<TreeItem><ItemType>56</ItemType></TreeItem>").Should().BeNull();

    [Fact]
    public void BuildTypeFileXml_OnlyGivenFlags_TmcFirst()
    {
        PlcInstanceSettingsXml.BuildTypeFileXml(true, false)
            .Should().Be("<TreeItem><PlcProjectDef><TmcFileCopy>true</TmcFileCopy><TpyFileCopy>false</TpyFileCopy></PlcProjectDef></TreeItem>");
        PlcInstanceSettingsXml.BuildTypeFileXml(null, true)
            .Should().Be("<TreeItem><PlcProjectDef><TpyFileCopy>true</TpyFileCopy></PlcProjectDef></TreeItem>");
    }

    [Fact]
    public void GetTypeFileGeneration_ReadsTheProjectNode_WithoutInstance()
    {
        var fake = OpenFake();
        fake.TreeItemXml[Project] = ProjectXml(true, false);
        fake.TreeChildren[Project] = [];
        Coordinator(fake).GetTypeFileGeneration(null).Should().Be("PLC project 'TIPC^FakePlc': TmcFileCopy=true, TpyFileCopy=false.");
    }

    [Fact]
    public void SetTypeFileGeneration_TpyOn_ReportsCoupledTmc()
    {
        var fake = OpenFake();
        string text = Coordinator(fake).SetTypeFileGeneration(null, true, null);
        text.Should().Contain("set tpy=true").And.Contain("read-back: TmcFileCopy=true, TpyFileCopy=true (the XAE changed the other flag with it)")
            .And.Contain("saved");
        fake.CallLog.Should().ContainInOrder("LogoutPlc()", $"ConsumeTreeItemXml({Project})", "SaveAll()");
    }

    [Fact]
    public void SetTypeFileGeneration_TmcOff_ClearsTpy()
    {
        var fake = OpenFake();
        fake.TreeItemXml[Project] = ProjectXml(true, true);
        Coordinator(fake).SetTypeFileGeneration(false, null, null)
            .Should().Contain("read-back: TmcFileCopy=false, TpyFileCopy=false (the XAE changed the other flag with it)");
    }

    [Fact]
    public void SetTypeFileGeneration_TmcOnTpyOff_FromBothOn()
    {
        var fake = OpenFake();
        fake.TreeItemXml[Project] = ProjectXml(true, true);
        Coordinator(fake).SetTypeFileGeneration(true, false, null)
            .Should().Contain("read-back: TmcFileCopy=true, TpyFileCopy=false;");
    }

    [Theory]
    [InlineData(null, null, "Pass tmc and/or tpy")]
    [InlineData(false, true, "not possible")]
    public void SetTypeFileGeneration_InvalidRequest_IsRefusedBeforeTheXae(bool? tmc, bool? tpy, string message)
    {
        var fake = OpenFake();
        Coordinator(fake).SetTypeFileGeneration(tmc, tpy, null).Should().StartWith("[Error]").And.Contain(message);
        fake.CallLog.Should().NotContain("LogoutPlc()");
        fake.ConsumedXml.Should().BeEmpty();
    }

    [Fact]
    public void SetTypeFileGeneration_SilentNoOp_IsAnError()
    {
        var fake = OpenFake();
        fake.ConsumeXmlApplier = null;
        Coordinator(fake).SetTypeFileGeneration(true, null, null)
            .Should().StartWith("[Error] The XAE did not apply the setting").And.Contain("read-back TmcFileCopy=false");
        fake.CallLog.Should().NotContain("SaveAll()");
    }

    [Fact]
    public void SetTypeFileGeneration_NoProjectDef_IsAnError()
    {
        var fake = OpenFake();
        fake.TreeItemXml[Project] = "<TreeItem><ItemType>56</ItemType></TreeItem>";
        Coordinator(fake).SetTypeFileGeneration(true, null, null).Should().StartWith("[Error]").And.Contain("no PlcProjectDef");
        fake.ConsumedXml.Should().BeEmpty();
    }

    [Fact]
    public void ReadInitSymbols_None_ExplainsHowToGetOne() =>
        Coordinator(OpenFake("<TreeItem><TcModuleInstance><Module/></TcModuleInstance></TreeItem>")).ReadInitSymbols(null)
            .Should().Contain("no init symbols").And.Contain("TcInitSymbol");
}
