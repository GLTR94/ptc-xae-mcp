using PtcXaeMcp.Tests.Fakes;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class ScopeCoordinatorTests
{
    // ── CreateScopeProject ──────────────────────────────────────────────────

    [Fact]
    public void CreateScopeProject_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeProject(null);

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void CreateScopeProject_SolutionOpen_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeProject("MyScopeProject");

        Assert.Contains("CreateScopeProject(name=MyScopeProject)", fake.CallLog);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void CreateScopeProject_SessionThrows_ReturnsError()
    {
        var fake  = new FakeTwinCatSession
        {
            IsConnected              = true,
            SolutionOpen             = true,
            CreateScopeProjectThrows = new InvalidOperationException("Template not found."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeProject(null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Template not found", result);
    }

    // ── RemoveScopeProject ──────────────────────────────────────────────────

    [Fact]
    public void RemoveScopeProject_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.RemoveScopeProject("SC_IT_dead");

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void RemoveScopeProject_SolutionOpen_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.RemoveScopeProject("SC_IT_abc123");

        Assert.Contains("RemoveScopeProject(name=SC_IT_abc123)", fake.CallLog);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void RemoveScopeProject_SessionThrows_ReturnsError()
    {
        var fake  = new FakeTwinCatSession
        {
            IsConnected              = true,
            SolutionOpen             = true,
            RemoveScopeProjectThrows = new InvalidOperationException("ambiguous: 2 measurement projects."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.RemoveScopeProject(null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("ambiguous", result);
    }

    // ── AddScopeChannel ────────────────────────────────────────────────────

    [Fact]
    public void AddScopeChannel_EmptySymbol_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("", null, null, null, false);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("symbol", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddScopeChannel_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("TIPC^PLC^MAIN.rValue", null, null, null, false);

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddScopeChannel_ValidInputs_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("TIPC^PLC^MAIN.rValue", "Chart1", "Axis1", "Ch1", true);

        Assert.Contains("AddScopeChannel(symbol=TIPC^PLC^MAIN.rValue", fake.CallLog[0]);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void AddScopeChannel_StackedAxisFlag_PassedThrough()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        coord.AddScopeChannel("SYM", null, null, null, stackedAxis: true);

        Assert.Contains("stacked=True", fake.CallLog[0]);
    }

    // ── SetScopeAxisMode ───────────────────────────────────────────────────

    [Fact]
    public void SetScopeAxisMode_EmptyChartName_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAxisMode("", "stacked");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("chartName", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetScopeAxisMode_UnknownMode_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAxisMode("Chart1", "sideways");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("sideways", result);
    }

    [Theory]
    [InlineData("overlaid")]
    [InlineData("stacked")]
    [InlineData("OVERLAID")]
    [InlineData("STACKED")]
    public void SetScopeAxisMode_ValidMode_DelegatesToSession(string mode)
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAxisMode("Chart1", mode);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        // Coordinator normalises to lower-case before calling session.
        Assert.Contains($"mode={mode.ToLowerInvariant()}", fake.CallLog[0]);
    }

    [Fact]
    public void SetScopeAxisMode_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAxisMode("Chart1", "stacked");

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    // ── SetScopeLabels ─────────────────────────────────────────────────────

    [Fact]
    public void SetScopeLabels_EmptyNodeSelector_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeLabels("", "NewName", null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("nodeSelector", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetScopeLabels_NoFieldsSupplied_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeLabels("Chart1", null, null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("At least one", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetScopeLabels_PartialUpdate_OnlyNameSupplied_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeLabels("Chart1", "My Chart", null, null);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("SetScopeLabels(node=Chart1", fake.CallLog[0]);
        Assert.Contains("name=My Chart", fake.CallLog[0]);
        Assert.Contains("chartTitle=<null>", fake.CallLog[0]);
    }

    [Fact]
    public void SetScopeLabels_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeLabels("Chart1", "x", null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    // ── ListScopeChannels ──────────────────────────────────────────────────

    [Fact]
    public void ListScopeChannels_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var (summary, entries) = coord.ListScopeChannels();

        Assert.StartsWith("[Error]", summary);
        Assert.Empty(entries);
    }

    [Fact]
    public void ListScopeChannels_Empty_ReportsNone()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var (summary, entries) = coord.ListScopeChannels();

        Assert.Contains("ListScopeChannels()", fake.CallLog);
        Assert.Empty(entries);
        Assert.Contains("No scope project", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListScopeChannels_WithNodes_ReturnsSummaryAndEntries()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected  = true,
            SolutionOpen = true,
            FakeScopeNodes =
            {
                new ScopeNodeEntry(ScopeNodeKind.Chart,     "Chart1", "Chart1"),
                new ScopeNodeEntry(ScopeNodeKind.AxisGroup, "Axis1",  "Chart1^Axis1"),
                new ScopeNodeEntry(ScopeNodeKind.Channel,   "Ch1",    "Chart1^Axis1^Ch1",
                    BoundAcquisition: "MAIN.rValue"),
            },
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, entries) = coord.ListScopeChannels();

        Assert.Equal(3, entries.Count);
        Assert.Contains("1 channel", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 chart", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListScopeChannels_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected             = true,
            SolutionOpen            = true,
            ListScopeChannelsThrows = new InvalidOperationException("COM busy"),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, entries) = coord.ListScopeChannels();

        Assert.StartsWith("[Error]", summary);
        Assert.Empty(entries);
    }

    // ── GetScopeStatus ─────────────────────────────────────────────────────

    [Fact]
    public void GetScopeStatus_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var (summary, status) = coord.GetScopeStatus();

        Assert.StartsWith("[Error]", summary);
        Assert.Null(status);
    }

    [Fact]
    public void GetScopeStatus_NoProject_ReportsNotExists()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected      = true,
            SolutionOpen     = true,
            FakeScopeStatus  = new ScopeStatus(false, null, 0, 0, 0, 0),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, status) = coord.GetScopeStatus();

        Assert.NotNull(status);
        Assert.False(status!.ScopeProjectExists);
        Assert.Contains("No TwinCAT Measurement", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetScopeStatus_ProjectExists_ReturnsCounts()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected     = true,
            SolutionOpen    = true,
            FakeScopeStatus = new ScopeStatus(true, "MeasurementProject", 2, 3, 4, 4),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, status) = coord.GetScopeStatus();

        Assert.NotNull(status);
        Assert.True(status!.ScopeProjectExists);
        Assert.Equal(2, status.ChartCount);
        Assert.Equal(4, status.ChannelCount);
        Assert.Contains("MeasurementProject", summary);
    }

    // ── ExportScopeData ────────────────────────────────────────────────────

    [Fact]
    public void ExportScopeData_UnknownFormat_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.ExportScopeData("XML");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("XML", result);
        Assert.Empty(fake.CallLog);
    }

    [Theory]
    [InlineData("CSV")]
    [InlineData("BINARY")]
    [InlineData("TDMS")]
    [InlineData("DAT")]
    [InlineData("SVD")]
    [InlineData("csv")]    // case-insensitive
    public void ExportScopeData_ValidFormat_DelegatesToSession(string format)
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.ExportScopeData(format);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains($"ExportScopeData(format={format.ToUpperInvariant()})", fake.CallLog);
    }

    [Fact]
    public void ExportScopeData_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.ExportScopeData("CSV");

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void ExportScopeData_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected           = true,
            SolutionOpen          = true,
            ExportScopeDataThrows = new InvalidOperationException("No recorded data"),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.ExportScopeData("CSV");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("No recorded data", result);
    }

    // ── AddScopeChannel: acquisition-symbol params ─────────────────────────

    [Fact]
    public void AddScopeChannel_BackwardCompatible_FiveArgCallStillValid()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        // Old call shape (no acquisition-symbol args) must still compile + work.
        var result = coord.AddScopeChannel("TIPC^PLC^MAIN.rValue", "C", "A", "Ch", false);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("symbolName=<null>", fake.CallLog[0]);
        Assert.Contains("port=<null>", fake.CallLog[0]);
    }

    [Fact]
    public void AddScopeChannel_NewParams_PassedThrough()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("TIPC^PLC^MAIN.rValue", null, null, null, false,
            symbolName: "GVL_HMI.HmiOpened", targetPort: 851,
            sampleMode: "Fixed Sampletime", sampleTimeMs: 10, symbolComment: "hmi flag");

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("symbolName=GVL_HMI.HmiOpened", fake.CallLog[0]);
        Assert.Contains("port=851", fake.CallLog[0]);
        Assert.Contains("sampleMode=Fixed Sampletime", fake.CallLog[0]);
        Assert.Contains("sampleMs=10", fake.CallLog[0]);
    }

    // fix/scope-units: in "Task Sampletime" mode TE130x takes the sample time from the task and
    // silently ignores a written Sample Time (live 2026-10-06: wrote 50, read back the previous 20).
    // The combination is contradictory, so it is refused up front instead of failing on read-back.
    [Fact]
    public void AddScopeChannel_TaskSampletimeWithSampleTime_RefusedBeforeSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("SYM", null, null, null, false,
            sampleMode: "Task Sampletime", sampleTimeMs: 10);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Task Sampletime", result);
        Assert.Contains("Fixed Sampletime", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeAcquisitionSymbol_TaskSampletimeWithSampleTime_RefusedBeforeSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("ACQ", symbolName: null, targetPort: null, targetSystem: null,
            sampleMode: "task sampletime", sampleTimeMs: 50, timeOffsetS: null, symbolComment: null, enabled: null);

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void AddScopeChannel_BadTargetPort_ReturnsError(int port)
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("SYM", null, null, null, false, targetPort: port);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("targetPort", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddScopeChannel_NonPositiveSampleTime_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("SYM", null, null, null, false, sampleTimeMs: 0);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("sampleTimeMs", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddScopeChannel_UnknownSampleMode_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("SYM", null, null, null, false, sampleMode: "Telepathy");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Telepathy", result);
        Assert.Empty(fake.CallLog);
    }

    [Theory]
    [InlineData("Task Sampletime")]
    [InlineData("task sampletime")]
    [InlineData("Fixed Sampletime")]
    public void AddScopeChannel_KnownSampleMode_Accepted(string mode)
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeChannel("SYM", null, null, null, false, sampleMode: mode);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    // ── ReadScopeAcquisition ───────────────────────────────────────────────

    [Fact]
    public void ReadScopeAcquisition_EmptySelector_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var (summary, info) = coord.ReadScopeAcquisition("");

        Assert.StartsWith("[Error]", summary);
        Assert.Null(info);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void ReadScopeAcquisition_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var (summary, info) = coord.ReadScopeAcquisition("Acq1");

        Assert.StartsWith("[Error]", summary);
        Assert.Null(info);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void ReadScopeAcquisition_HasSymbol_DelegatesAndSummarises()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected  = true,
            SolutionOpen = true,
            FakeScopeAcquisitionInfo = new ScopeAcquisitionInfo(
                "HmiOpened", "DataPool^HmiOpened", "GVL_HMI.HmiOpened", "BIT",
                "Task Sampletime", "10", "851", "Local", true,
                new[] { new ScopeAcquisitionProperty("Symbol Name", "GVL_HMI.HmiOpened") }),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, info) = coord.ReadScopeAcquisition("HmiOpened");

        Assert.Contains("ReadScopeAcquisition(sel=HmiOpened)", fake.CallLog);
        Assert.NotNull(info);
        Assert.True(info!.HasSymbol);
        Assert.Contains("GVL_HMI.HmiOpened", summary);
        Assert.DoesNotContain("NO SYMBOL", summary);
    }

    [Fact]
    public void ReadScopeAcquisition_NoSymbol_FlagsWarning()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected  = true,
            SolutionOpen = true,
            FakeScopeAcquisitionInfo = new ScopeAcquisitionInfo(
                "Acq1", "DataPool^Acq1", null, null, null, null, null, null, false,
                Array.Empty<ScopeAcquisitionProperty>()),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, info) = coord.ReadScopeAcquisition("Acq1");

        Assert.NotNull(info);
        Assert.False(info!.HasSymbol);
        Assert.Contains("NO SYMBOL", summary);
    }

    [Fact]
    public void ReadScopeAcquisition_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected               = true,
            SolutionOpen              = true,
            ReadScopeAcquisitionThrows = new InvalidOperationException("Acquisition 'X' not found"),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, info) = coord.ReadScopeAcquisition("X");

        Assert.StartsWith("[Error]", summary);
        Assert.Contains("not found", summary);
        Assert.Null(info);
    }

    // ── SetScopeAcquisitionSymbol ──────────────────────────────────────────

    [Fact]
    public void SetScopeAcquisitionSymbol_EmptySelector_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("", "GVL.x", null, null, null, null, null, null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("acqSelector", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeAcquisitionSymbol_NoFields_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("Acq1", null, null, null, null, null, null, null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("At least one", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeAcquisitionSymbol_BadPort_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("Acq1", null, 99999, null, null, null, null, null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("targetPort", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeAcquisitionSymbol_UnknownSampleMode_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("Acq1", null, null, null, "Wishful", null, null, null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Wishful", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeAcquisitionSymbol_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("Acq1", "GVL.x", null, null, null, null, null, null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeAcquisitionSymbol_PartialUpdate_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("DataPool^HmiOpened",
            symbolName: "GVL_HMI.HmiOpened", targetPort: null, targetSystem: null,
            sampleMode: null, sampleTimeMs: 20, timeOffsetS: null, symbolComment: null, enabled: null);

        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
        Assert.Contains("SetScopeAcquisitionSymbol(sel=DataPool^HmiOpened", fake.CallLog[0]);
        Assert.Contains("symbolName=GVL_HMI.HmiOpened", fake.CallLog[0]);
        Assert.Contains("sampleMs=20", fake.CallLog[0]);
        Assert.Contains("port=<null>", fake.CallLog[0]);
    }

    [Fact]
    public void SetScopeAcquisitionSymbol_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected                   = true,
            SolutionOpen                  = true,
            SetScopeAcquisitionSymbolThrows = new InvalidOperationException("No requested field could be applied"),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeAcquisitionSymbol("Acq1", "GVL.x", null, null, null, null, null, null, null);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("could be applied", result);
    }

    // ── StartScopeRecord ──────────────────────────────────────────────────

    [Fact]
    public void StartScopeRecord_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.StartScopeRecord();

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void StartScopeRecord_SolutionOpen_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.StartScopeRecord();

        Assert.Contains("StartScopeRecord", fake.CallLog[0]);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void StartScopeRecord_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected          = true,
            SolutionOpen         = true,
            StartScopeRecordThrows = new InvalidOperationException("Scope not in Config state."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.StartScopeRecord();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Config state", result);
    }

    // ── StopScopeRecord ───────────────────────────────────────────────────

    [Fact]
    public void StopScopeRecord_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.StopScopeRecord();

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void StopScopeRecord_SolutionOpen_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.StopScopeRecord();

        Assert.Contains("StopScopeRecord", fake.CallLog[0]);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void StopScopeRecord_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected         = true,
            SolutionOpen        = true,
            StopScopeRecordThrows = new InvalidOperationException("COM error stopping record."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.StopScopeRecord();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("COM error", result);
    }

    // ── GetScopeRecordState ───────────────────────────────────────────────

    [Fact]
    public void GetScopeRecordState_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var (summary, state) = coord.GetScopeRecordState();

        Assert.StartsWith("[Error]", summary);
        Assert.Null(state);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void GetScopeRecordState_RecordState_ReturnsSummary()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected       = true,
            SolutionOpen      = true,
            FakeScopeRecordState = new ScopeRecordState(ScopeViewState.Record, true, false),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, state) = coord.GetScopeRecordState();

        Assert.NotNull(state);
        Assert.Equal(ScopeViewState.Record, state!.State);
        Assert.True(state.IsRecording);
        Assert.False(state.HasData);
        Assert.Contains("Recording", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetScopeRecordState_ReplyState_ReturnsDataAvailable()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected       = true,
            SolutionOpen      = true,
            FakeScopeRecordState = new ScopeRecordState(ScopeViewState.Reply, false, true),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, state) = coord.GetScopeRecordState();

        Assert.NotNull(state);
        Assert.Equal(ScopeViewState.Reply, state!.State);
        Assert.False(state.IsRecording);
        Assert.True(state.HasData);
        Assert.Contains("data", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetScopeRecordState_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected               = true,
            SolutionOpen              = true,
            GetScopeRecordStateThrows = new InvalidOperationException("ScopeState unavailable."),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, state) = coord.GetScopeRecordState();

        Assert.StartsWith("[Error]", summary);
        Assert.Null(state);
    }

    // ── SaveScopeData ─────────────────────────────────────────────────────

    [Fact]
    public void SaveScopeData_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SaveScopeData();

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SaveScopeData_SolutionOpen_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SaveScopeData(@"C:\tmp\my.svdx");

        Assert.Contains("SaveScopeData", fake.CallLog[0]);
        Assert.Contains("my.svdx", fake.CallLog[0]);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void SaveScopeData_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected       = true,
            SolutionOpen      = true,
            SaveScopeDataThrows = new InvalidOperationException("No recorded data — run a recording first."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SaveScopeData();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("recorded data", result);
    }

    // ── SetScopeRecordSettings ─────────────────────────────────────────────

    [Fact]
    public void SetScopeRecordSettings_NoFields_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings();

        Assert.StartsWith("[Error]", result);
        Assert.Contains("At least one", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeRecordSettings_InvalidRecordTime_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings(recordTimeS: -1);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("recordTimeS", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeRecordSettings_InvalidRecordMode_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings(recordMode: "AutoStart");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("AutoStart", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeRecordSettings_InvalidAutoSaveMode_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings(autoSaveMode: "ZIP");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("ZIP", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeRecordSettings_InvalidSyncMode_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings(syncMode: "Ultra");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Ultra", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeRecordSettings_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings(recordTimeS: 10);

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void SetScopeRecordSettings_ValidFields_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings(
            recordTimeS: 10, recordMode: "TriggerStart", autoSaveMode: "SVDX");

        Assert.Contains("SetScopeRecordSettings", fake.CallLog[0]);
        Assert.Contains("recordTimeS=10", fake.CallLog[0]);
        Assert.Contains("recordMode=TriggerStart", fake.CallLog[0]);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void SetScopeRecordSettings_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected                  = true,
            SolutionOpen                 = true,
            SetScopeRecordSettingsThrows = new InvalidOperationException("No field could be applied."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.SetScopeRecordSettings(recordTimeS: 5);

        Assert.StartsWith("[Error]", result);
        Assert.Contains("could be applied", result);
    }

    // ── CreateScopeTriggerGroup ────────────────────────────────────────────

    [Fact]
    public void CreateScopeTriggerGroup_EmptyAction_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeTriggerGroup("  ");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("action", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void CreateScopeTriggerGroup_UnknownAction_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeTriggerGroup("LaunchMissiles");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("LaunchMissiles", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void CreateScopeTriggerGroup_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeTriggerGroup("StopRecord");

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void CreateScopeTriggerGroup_ValidAction_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeTriggerGroup("StopRecord");

        Assert.Contains("CreateScopeTriggerGroup(action=StopRecord)", fake.CallLog[0]);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void CreateScopeTriggerGroup_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected                   = true,
            SolutionOpen                  = true,
            CreateScopeTriggerGroupThrows = new InvalidOperationException("Trigger node not found."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.CreateScopeTriggerGroup("StopRecord");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("Trigger node", result);
    }

    // ── AddScopeTriggerSet ────────────────────────────────────────────────

    [Fact]
    public void AddScopeTriggerSet_EmptyGroup_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeTriggerSet("", "ChannelSet");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("groupName", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddScopeTriggerSet_InvalidSetType_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeTriggerSet("TriggerGroup_StopRecord", "WeirdType");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("WeirdType", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddScopeTriggerSet_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeTriggerSet("TriggerGroup_StopRecord", "ChannelSet");

        Assert.StartsWith("[Error]", result);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void AddScopeTriggerSet_ValidChannelSet_DelegatesToSession()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeTriggerSet(
            "TriggerGroup_StopRecord", "ChannelSet", symbol: "MAIN.rActual", threshold: 1.5);

        Assert.Contains("AddScopeTriggerSet(group=TriggerGroup_StopRecord", fake.CallLog[0]);
        Assert.Contains("setType=ChannelSet", fake.CallLog[0]);
        Assert.Contains("symbol=MAIN.rActual", fake.CallLog[0]);
        Assert.False(result.StartsWith("[Error]", StringComparison.Ordinal), result);
    }

    [Fact]
    public void AddScopeTriggerSet_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected              = true,
            SolutionOpen             = true,
            AddScopeTriggerSetThrows = new InvalidOperationException("Trigger group 'X' not found."),
        };
        var coord = new ScopeCoordinator(fake);

        var result = coord.AddScopeTriggerSet("X", "ChannelSet");

        Assert.StartsWith("[Error]", result);
        Assert.Contains("not found", result);
    }

    // ── ListScopeTriggers ─────────────────────────────────────────────────

    [Fact]
    public void ListScopeTriggers_NoSolution_ReturnsError()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = false };
        var coord = new ScopeCoordinator(fake);

        var (summary, groups) = coord.ListScopeTriggers();

        Assert.StartsWith("[Error]", summary);
        Assert.Empty(groups);
        Assert.Empty(fake.CallLog);
    }

    [Fact]
    public void ListScopeTriggers_Empty_ReturnsBenigneMessage()
    {
        var fake  = new FakeTwinCatSession { IsConnected = true, SolutionOpen = true, FakeScopeTriggers = new() };
        var coord = new ScopeCoordinator(fake);

        var (summary, groups) = coord.ListScopeTriggers();

        Assert.Empty(groups);
        Assert.DoesNotContain("[Error]", summary);
    }

    [Fact]
    public void ListScopeTriggers_WithGroups_ReturnsSummary()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected = true,
            SolutionOpen = true,
            FakeScopeTriggers = new()
            {
                new ScopeTriggerGroupEntry("TG1", "StopRecord",
                    new[] { new ScopeTriggerSetEntry("ChannelSet", "MAIN.x", 1.0, "Rising", null) }),
            },
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, groups) = coord.ListScopeTriggers();

        Assert.Single(groups);
        Assert.Contains("1", summary);
        Assert.DoesNotContain("[Error]", summary);
    }

    [Fact]
    public void ListScopeTriggers_SessionThrows_ReturnsError()
    {
        var fake = new FakeTwinCatSession
        {
            IsConnected              = true,
            SolutionOpen             = true,
            ListScopeTriggersThrows  = new InvalidOperationException("COM error listing triggers."),
        };
        var coord = new ScopeCoordinator(fake);

        var (summary, groups) = coord.ListScopeTriggers();

        Assert.StartsWith("[Error]", summary);
        Assert.Empty(groups);
    }
}
