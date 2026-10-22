using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class EtherCatEditCoordinatorTests
{
    private static FakeTwinCatSession Live() => new() { IsConnected = true, SolutionOpen = true };

    // ---- AddDevice ---------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddDevice_EmptyName_ReturnsError(string name)
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake).AddDevice(name, "master");
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.AddedEtherCatDevices);
    }

    [Fact]
    public void AddDevice_BadKind_ReturnsError()
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake).AddDevice("Dev", "router");
        Assert.StartsWith("[Error]", result);
        Assert.Contains("master", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.AddedEtherCatDevices);
    }

    [Fact]
    public void AddDevice_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var result = new EtherCatEditCoordinator(fake).AddDevice("Dev", "master");
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.AddedEtherCatDevices);
    }

    [Fact]
    public void AddDevice_DefaultsToMaster_RecordsAndSucceeds()
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake).AddDevice("100000 (EtherCAT)", null);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        var (name, kind) = Assert.Single(fake.AddedEtherCatDevices);
        Assert.Equal("100000 (EtherCAT)", name);
        Assert.Equal("master", kind);
    }

    [Fact]
    public void AddDevice_Slave_RecordsKind()
    {
        var fake = Live();
        new EtherCatEditCoordinator(fake).AddDevice("Sub", "SLAVE");
        Assert.Equal(("Sub", "slave"), Assert.Single(fake.AddedEtherCatDevices));
    }

    [Fact]
    public void AddDevice_SessionThrows_ReturnsError()
    {
        var fake = Live();
        fake.AddEtherCatDeviceThrows = new InvalidOperationException("CreateChild failed: boom");
        var result = new EtherCatEditCoordinator(fake).AddDevice("Dev", "master");
        Assert.StartsWith("[Error]", result);
        Assert.Contains("CreateChild failed", result);
    }

    // ---- AddBox ------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddBox_EmptyParent_ReturnsError(string parent)
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake).AddBox(parent, "EL9011", null, null, null, null);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("required", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.AddedEtherCatBoxes);
    }

    [Theory]
    [InlineData("TIID")]
    [InlineData("tiid")]
    public void AddBox_RootParent_ReturnsError(string parent)
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake).AddBox(parent, "EL9011", null, null, null, null);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("root", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.AddedEtherCatBoxes);
    }

    [Fact]
    public void AddBox_BothVInfoAndJson_ReturnsError()
    {
        var result = new EtherCatEditCoordinator(Live())
            .AddBox("TIID^Device 1 (EtherCAT)", "EL9011", null, null, null, "{\"vInfo\":\"EL9011\"}");
        Assert.StartsWith("[Error]", result);
        Assert.Contains("exactly one", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddBox_NeitherVInfoNorJson_ReturnsError()
    {
        var result = new EtherCatEditCoordinator(Live())
            .AddBox("TIID^Device 1 (EtherCAT)", null, null, null, null, null);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("exactly one", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddBox_InvalidJson_ReturnsError()
    {
        var result = new EtherCatEditCoordinator(Live())
            .AddBox("TIID^Device 1 (EtherCAT)", null, null, null, null, "{ not json");
        Assert.StartsWith("[Error]", result);
        Assert.Contains("JSON", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddBox_InvalidPreviousPort_ReturnsError()
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake)
            .AddBox("TIID^Device 1 (EtherCAT)", "EL9011", null, "BB", null, null);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("previousPort", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.AddedEtherCatBoxes);
    }

    [Fact]
    public void AddBox_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };
        var result = new EtherCatEditCoordinator(fake)
            .AddBox("TIID^Device 1 (EtherCAT)", "EL9011", null, null, null, null);
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.AddedEtherCatBoxes);
    }

    [Fact]
    public void AddBox_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var result = new EtherCatEditCoordinator(fake)
            .AddBox("TIID^Device 1 (EtherCAT)", "EL9011", null, null, null, null);
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.AddedEtherCatBoxes);
    }

    [Fact]
    public void AddBox_HappyPath_SingleBox_RecordsAndSucceeds()
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake)
            .AddBox("TIID^Device 1 (EtherCAT)", "EL9011", "A1.49 (EL9011)", "B", "Term 2", null);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        var (parent, box, before) = Assert.Single(fake.AddedEtherCatBoxes);
        Assert.Equal("TIID^Device 1 (EtherCAT)", parent);
        Assert.Equal("EL9011", box.VInfo);
        Assert.Equal("A1.49 (EL9011)", box.Name);
        Assert.Equal("B", box.PreviousPort);
        Assert.Equal("Term 2", before);
        Assert.Contains("AddEtherCatBox(parent=TIID^Device 1 (EtherCAT)", string.Join(",", fake.CallLog));
    }

    [Fact]
    public void AddBox_HappyPath_SubtreeJson_CountsNodes()
    {
        var fake = Live();
        const string json = """
            { "name": "A1.42 (EK1122)", "vInfo": "EK1122", "previousPort": "B",
              "children": [
                { "vInfo": "EL1809" },
                { "vInfo": "EK1100", "children": [ { "vInfo": "EL2809" } ] }
              ] }
            """;

        var result = new EtherCatEditCoordinator(fake)
            .AddBox("TIID^Device 1 (EtherCAT)", null, null, null, null, json);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        var (_, box, _) = Assert.Single(fake.AddedEtherCatBoxes);
        Assert.Equal("EK1122", box.VInfo);
        Assert.Equal(4, box.NodeCount); // EK1122 + EL1809 + EK1100 + EL2809
    }

    [Fact]
    public void AddBox_ArrayJson_AddsEachTopLevelSibling()
    {
        var fake = Live();
        const string json = """[ { "vInfo": "EL1809" }, { "vInfo": "EL2809" } ]""";

        var result = new EtherCatEditCoordinator(fake)
            .AddBox("TIID^Device 1 (EtherCAT)^Box 1 (EK1100)", null, null, null, null, json);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Equal(2, fake.AddedEtherCatBoxes.Count);
        Assert.Equal("EL1809", fake.AddedEtherCatBoxes[0].Box.VInfo);
        Assert.Equal("EL2809", fake.AddedEtherCatBoxes[1].Box.VInfo);
    }

    [Fact]
    public void AddBox_SessionThrows_ReturnsError()
    {
        var fake = Live();
        fake.AddEtherCatBoxThrows = new InvalidOperationException("CreateChild failed: ESI missing");
        var result = new EtherCatEditCoordinator(fake)
            .AddBox("TIID^Device 1 (EtherCAT)", "ELM9011", null, null, null, null);
        Assert.StartsWith("[Error]", result);
        Assert.Contains("CreateChild failed", result);
    }

    // ---- RemoveBox ---------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("TIID")]
    public void RemoveBox_EmptyOrRoot_ReturnsError(string path)
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake).RemoveBox(path);
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.RemovedEtherCatBoxes);
    }

    [Fact]
    public void RemoveBox_NotConnected_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = false };
        var result = new EtherCatEditCoordinator(fake).RemoveBox("TIID^Device 1^Box 1 (EK1100)");
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.RemovedEtherCatBoxes);
    }

    [Fact]
    public void RemoveBox_HappyPath_RecordsAndSucceeds()
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake)
            .RemoveBox("TIID^Device 1 (EtherCAT)^Box 1 (EK1100)^Term 4 (EL2004)");
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Equal("TIID^Device 1 (EtherCAT)^Box 1 (EK1100)^Term 4 (EL2004)",
            Assert.Single(fake.RemovedEtherCatBoxes));
    }

    [Fact]
    public void RemoveBox_SessionThrows_ReturnsError()
    {
        var fake = Live();
        fake.RemoveEtherCatBoxThrows = new InvalidOperationException("Delete failed: boom");
        var result = new EtherCatEditCoordinator(fake).RemoveBox("TIID^Device 1^Box 1 (EK1100)");
        Assert.StartsWith("[Error]", result);
        Assert.Contains("Delete failed", result);
    }

    // ---- SetPreviousPort ---------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("TIID")]
    public void SetPreviousPort_EmptyOrRoot_ReturnsError(string path)
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake).SetPreviousPort(path, "B");
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.SetPreviousPorts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BB")]
    [InlineData("2")]
    public void SetPreviousPort_BadPort_ReturnsError(string port)
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake)
            .SetPreviousPort("TIID^Device 1^Box 2 (EK1122)^Box 3 (EK1100)", port);
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.SetPreviousPorts);
    }

    [Fact]
    public void SetPreviousPort_NoSolution_ReturnsError()
    {
        var fake = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var result = new EtherCatEditCoordinator(fake)
            .SetPreviousPort("TIID^Device 1^Box 2 (EK1122)^Box 3 (EK1100)", "B");
        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.SetPreviousPorts);
    }

    [Fact]
    public void SetPreviousPort_HappyPath_RecordsAndSucceeds()
    {
        var fake = Live();
        var result = new EtherCatEditCoordinator(fake)
            .SetPreviousPort("TIID^Device 1^Box 2 (EK1122)^Box 3 (EK1100)", "b");
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        var (path, port) = Assert.Single(fake.SetPreviousPorts);
        Assert.Equal("TIID^Device 1^Box 2 (EK1122)^Box 3 (EK1100)", path);
        Assert.Equal("b", port);
    }

    [Fact]
    public void SetPreviousPort_SessionThrows_ReturnsError()
    {
        var fake = Live();
        fake.SetEtherCatPreviousPortThrows =
            new InvalidOperationException("Port 'C' is not available for 'X'. Available: A, B.");
        var result = new EtherCatEditCoordinator(fake)
            .SetPreviousPort("TIID^Device 1^Box 2 (EK1122)^Box 3 (EK1100)", "C");
        Assert.StartsWith("[Error]", result);
        Assert.Contains("not available", result);
    }
}

public class EtherCatSpecJsonTests
{
    [Fact]
    public void Parse_SingleObject_ReturnsOneSpec()
    {
        var specs = EtherCatSpecJson.Parse("""{ "name": "A1.49 (EL9011)", "vInfo": "EL9011" }""");
        var spec = Assert.Single(specs);
        Assert.Equal("EL9011", spec.VInfo);
        Assert.Equal("A1.49 (EL9011)", spec.Name);
        Assert.Empty(spec.ChildList);
        Assert.Equal(1, spec.NodeCount);
    }

    [Fact]
    public void Parse_Array_ReturnsSiblings()
    {
        var specs = EtherCatSpecJson.Parse("""[ {"vInfo":"EL1809"}, {"vInfo":"EL2809"} ]""");
        Assert.Equal(2, specs.Count);
        Assert.Equal("EL1809", specs[0].VInfo);
        Assert.Equal("EL2809", specs[1].VInfo);
    }

    [Fact]
    public void Parse_SchemaFragment_UnwrapsRoot()
    {
        var specs = EtherCatSpecJson.Parse("""{ "root": { "vInfo": "EK1200-5000", "children": [ {"vInfo":"EL6070"} ] } }""");
        var spec = Assert.Single(specs);
        Assert.Equal("EK1200-5000", spec.VInfo);
        Assert.Equal(2, spec.NodeCount);
    }

    [Fact]
    public void Parse_FullSchemaFile_UnwrapsEthercatRoot()
    {
        const string json = """
            { "twincat": { "deviceName": "PNE" },
              "ethercat": { "root": { "name": "A1.01 (EK1200-5000)", "vInfo": "EK1200-5000",
                "children": [ { "vInfo": "EL6070", "previousPort": "A" } ] } } }
            """;
        var spec = Assert.Single(EtherCatSpecJson.Parse(json));
        Assert.Equal("EK1200-5000", spec.VInfo);
        Assert.Equal("A", spec.ChildList[0].PreviousPort);
        Assert.Equal(2, spec.NodeCount);
    }

    [Fact]
    public void Parse_MissingVInfo_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EtherCatSpecJson.Parse("""{ "name": "no vinfo here" }"""));
        Assert.Contains("vInfo", ex.Message);
    }

    [Fact]
    public void Parse_NotJson_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EtherCatSpecJson.Parse("{ not json"));
        Assert.Contains("JSON", ex.Message);
    }

    [Fact]
    public void Parse_Empty_Throws() =>
        Assert.Throws<InvalidOperationException>(() => EtherCatSpecJson.Parse("   "));
}

/// <summary>
/// Regression guard for the engine extraction: pins the vInfo-candidate
/// contract that BOTH twincat_apply_schema (NativeTwinCatRunner) and the
/// ethercat-topology-edit tools depend on. If this changes, apply_schema's
/// box-creation behaviour changed too.
/// </summary>
public class EtherCatVInfoTests
{
    [Fact]
    public void BoxItemSubType_Is9099() =>
        Assert.Equal(9099, EtherCatVInfo.BoxItemSubType);

    [Fact]
    public void Candidates_BareType_IsPassedThroughOnly()
    {
        var c = EtherCatVInfo.GetCreateChildVInfoCandidates("EK1100");
        Assert.Equal(new[] { "EK1100" }, c);
    }

    [Fact]
    public void Candidates_Revisioned_FallsBackToBaseType()
    {
        var c = EtherCatVInfo.GetCreateChildVInfoCandidates("EK1100-0000-0017");
        Assert.Equal("EK1100-0000-0017", c[0]);
        Assert.Contains("EK1100", c);
    }

    [Fact]
    public void Candidates_KebDrive_IncludesEsiAliases()
    {
        var c = EtherCatVInfo.GetCreateChildVInfoCandidates("10S6P");
        Assert.Contains("PTC_S6P_STO_V1", c);
        Assert.Contains("KEB_PTC_S6P_STO_V1 (MDP)", c);
    }

    [Theory]
    [InlineData("EK1100-0000-0017", "EK1100")]
    [InlineData("KRC4 secondary EL6695-1001", "EL6695")]
    [InlineData("EK1100", null)]
    public void TryGetBaseVInfo_CollapsesToTerminalBase(string input, string? expected)
    {
        bool ok = EtherCatVInfo.TryGetBaseVInfo(input, out var baseV);
        Assert.Equal(expected is not null, ok);
        Assert.Equal(expected, baseV);
    }
}
