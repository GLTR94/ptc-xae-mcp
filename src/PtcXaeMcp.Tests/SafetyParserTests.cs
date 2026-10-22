using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Unit tests for the TwinCAT Safety file parsers and the .splcproj editor.
/// The .sds/.sal expectations come from the Beckhoff-shipped TwinSAFE project template captured
/// under Fixtures/Safety/TemplateProject (schema is not publicly documented; issue #153).
/// </summary>
public class SafetyParserTests
{
    // ── .splcproj parser ──────────────────────────────────────────────────────

    private const string SplcprojSample = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <ProjectGuid>{A1B2C3D4-0000-0000-0000-000000000001}</ProjectGuid>
            <NextSdsId>7</NextSdsId>
          </PropertyGroup>
          <ItemGroup>
            <None Include="Alias Devices\Hardware\EL1918_Ch01.sds" />
            <None Include="Alias Devices\Hardware\EL2904_Ch01.sds" />
            <None Include="Alias Devices\Projekt-Variablen\bSI_DriveReleased.sds" />
            <None Include="Alias Devices\Systemvariablen\_bSI_GroupOk.sds" />
            <None Include="Groups\SafetyGroup1.sal" />
            <None Include="Groups\SafetyGroup2.sal" />
            <None Include="UserFunctionBlocks\FB_Custom.ufb" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void Splcproj_ParsesNextSdsId()
    {
        var result = SafetySplcprojParser.Parse(SplcprojSample);
        Assert.Equal(7, result.NextSdsId);
    }

    [Fact]
    public void Splcproj_ParsesAliasDevices_Count()
    {
        var result = SafetySplcprojParser.Parse(SplcprojSample);
        Assert.Equal(4, result.AliasDevices.Count);
    }

    [Fact]
    public void Splcproj_ClassifiesHardwareAliasDevices()
    {
        var result = SafetySplcprojParser.Parse(SplcprojSample);
        var hw = result.AliasDevices.Where(a => a.Kind == AliasDeviceKind.Hardware).ToList();
        Assert.Equal(2, hw.Count);
        Assert.Contains(hw, a => a.IncludePath.Contains("EL1918"));
        Assert.Contains(hw, a => a.IncludePath.Contains("EL2904"));
    }

    [Fact]
    public void Splcproj_ClassifiesProjectVariableAlias()
    {
        var result = SafetySplcprojParser.Parse(SplcprojSample);
        var pv = result.AliasDevices.Single(a => a.Kind == AliasDeviceKind.ProjectVariable);
        Assert.Contains("bSI_DriveReleased", pv.IncludePath);
    }

    [Fact]
    public void Splcproj_ClassifiesSystemVariableAlias()
    {
        var result = SafetySplcprojParser.Parse(SplcprojSample);
        var sv = result.AliasDevices.Single(a => a.Kind == AliasDeviceKind.SystemVariable);
        Assert.Contains("_bSI_GroupOk", sv.IncludePath);
    }

    [Fact]
    public void Splcproj_ParsesGroups()
    {
        var result = SafetySplcprojParser.Parse(SplcprojSample);
        Assert.Equal(2, result.Groups.Count);
        Assert.Contains(result.Groups, g => g.Contains("SafetyGroup1.sal"));
        Assert.Contains(result.Groups, g => g.Contains("SafetyGroup2.sal"));
    }

    [Fact]
    public void Splcproj_ParsesUserFBs()
    {
        var result = SafetySplcprojParser.Parse(SplcprojSample);
        Assert.Single(result.UserFBs);
        Assert.Contains("FB_Custom.ufb", result.UserFBs[0]);
    }

    [Fact]
    public void Splcproj_EmptyXml_ReturnsEmptyStructure()
    {
        var result = SafetySplcprojParser.Parse("<Project/>");
        Assert.Empty(result.AliasDevices);
        Assert.Empty(result.Groups);
        Assert.Empty(result.UserFBs);
        Assert.Null(result.NextSdsId);
    }

    [Fact]
    public void Splcproj_MalformedXml_ReturnsEmptyStructure()
    {
        var result = SafetySplcprojParser.Parse("not xml <><>");
        Assert.Empty(result.AliasDevices);
    }

    // ── path classification ───────────────────────────────────────────────────

    [Fact]
    public void ClassifyFromPath_Hardware()
        => Assert.Equal(AliasDeviceKind.Hardware,
            SafetySplcprojParser.ClassifyFromPath(@"Alias Devices\Hardware\EL1918.sds"));

    [Fact]
    public void ClassifyFromPath_ProjectVariable()
        => Assert.Equal(AliasDeviceKind.ProjectVariable,
            SafetySplcprojParser.ClassifyFromPath(@"Alias Devices\Projekt-Variablen\bSI_Ok.sds"));

    [Fact]
    public void ClassifyFromPath_SystemVariable()
        => Assert.Equal(AliasDeviceKind.SystemVariable,
            SafetySplcprojParser.ClassifyFromPath(@"Alias Devices\Systemvariablen\_bSI_GroupOk.sds"));

    [Fact]
    public void ClassifyFromPath_Unknown()
        => Assert.Equal(AliasDeviceKind.Unknown,
            SafetySplcprojParser.ClassifyFromPath(@"Unknown\somedevice.sds"));

    // ── .sds parser (real format, Beckhoff-shipped sample) ────────────────────

    private static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Safety", "TemplateProject");
    private static string FixtureSds(string name) =>
        File.ReadAllText(Path.Combine(FixtureDir, "TwinSafeGroup1", "Alias Devices", name + ".sds"), System.Text.Encoding.UTF8);

    [Fact]
    public void Sds_EditorAuthoredSample_ParsesElements()
    {
        var d = SafetySdsParser.Parse(FixtureSds("ErrorAcknowledgement"), AliasDeviceKind.Unknown, "ErrorAcknowledgement");
        Assert.NotNull(d);
        Assert.Equal("ErrorAcknowledgement", d!.Name);
        Assert.Equal(1, d.SdsId);
        Assert.Equal(1, d.Type);
        Assert.Equal(1, d.SubType);
        Assert.Equal("Manual", d.LinkingMode);
        Assert.Equal("In", d.IoName);
        Assert.Equal("1.5", d.FileFormatVersion);
    }

    [Fact]
    public void Sds_SecondSample_HasItsOwnSdsId()
    {
        var d = SafetySdsParser.Parse(FixtureSds("Run"), AliasDeviceKind.Unknown, "Run");
        Assert.Equal(2, d!.SdsId);
    }

    [Fact]
    public void Sds_NoSdsIdElement_IsNull_NotZero()
    {
        var d = SafetySdsParser.Parse("<SafetyDeviceDesc><AliasDeviceType><Type>1</Type></AliasDeviceType></SafetyDeviceDesc>",
            AliasDeviceKind.Unknown, "x");
        Assert.Null(d!.SdsId);
    }

    [Fact]
    public void Sds_FormerFabricatedAttributeFormat_IsNotRead()
    {
        // The old parser/author used <SafetyDeviceDescription><DeviceInfo SDSID="3" …> — a format the
        // TwinSAFE editor never writes (issue #153, D-8). It must not yield an SDSID.
        const string fabricated = """
            <SafetyDeviceDescription>
              <DeviceInfo Name="bSI_DriveReleased" SDSID="3" SubType="2" LinkingMode="ProgramInput" />
            </SafetyDeviceDescription>
            """;
        Assert.Null(SafetySdsParser.Parse(fabricated, AliasDeviceKind.ProjectVariable, "bSI_DriveReleased"));
    }

    [Fact]
    public void Sds_SignalName_IsTheFileName_WhenItMatchesThePrefixConvention()
    {
        Assert.Equal("bSI_Ok", SafetySdsParser.Parse(FixtureSds("Run"), AliasDeviceKind.ProjectVariable, "bSI_Ok")!.SignalName);
        Assert.Null(SafetySdsParser.Parse(FixtureSds("Run"), AliasDeviceKind.Unknown, "Run")!.SignalName);
    }

    [Fact]
    public void Sds_MalformedXml_ReturnsNull()
        => Assert.Null(SafetySdsParser.Parse("<<not xml>>", AliasDeviceKind.Hardware));

    [Fact]
    public void Sds_CustomPrefixEnvVar_RecognizesNonDefaultSignalName()
    {
        string? previous = Environment.GetEnvironmentVariable(SafetySdsParser.PrefixesEnvVar);
        try
        {
            Assert.Null(SafetySdsParser.Parse(FixtureSds("Run"), AliasDeviceKind.Unknown, "fx_Drive")!.SignalName);
            Environment.SetEnvironmentVariable(SafetySdsParser.PrefixesEnvVar, "fx_");
            Assert.Equal("fx_Drive", SafetySdsParser.Parse(FixtureSds("Run"), AliasDeviceKind.Unknown, "fx_Drive")!.SignalName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SafetySdsParser.PrefixesEnvVar, previous);
        }
    }

    // ── .sal IO-usage parser (Beckhoff template TwinSAFE.sal) ─────────────────

    [Fact]
    public void Sal_IoUsage_FromTemplate_HasVariableAndUsage()
    {
        var sal = File.ReadAllText(Path.Combine(FixtureDir, "TwinSafeGroup1", "TwinSafeGroup1.sal"), System.Text.Encoding.UTF8);
        var result = SafetySalParser.ParseIoUsage(sal);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, u => u is { SdsId: 1, ChannelId: 1, Variable: "GroupPort_ErrAck", Usage: "source", FunctionId: 0 });
        Assert.Contains(result, u => u is { SdsId: 2, ChannelId: 1, Variable: "GroupPort_RunStop", Usage: "source" });
    }

    [Fact]
    public void Sal_GroupAliasPortWithSdsIdAttribute_IsNotAnIoUsage()
    {
        // The template's <twinSAFEGroupAliasPort sdsId="0" …> carries an sdsId but is no I/O usage;
        // the old "any element with sdsId" fallback counted such elements.
        const string xml = """<a><twinSAFEGroupAliasPort name="RunStop" sdsId="4" channelId="2" /></a>""";
        Assert.Empty(SafetySalParser.ParseIoUsage(xml));
    }

    [Fact]
    public void Sal_MalformedXml_ReturnsEmpty() => Assert.Empty(SafetySalParser.ParseIoUsage("<<bad xml>>"));

    // ── .sal FBD parser ───────────────────────────────────────────────────────

    private const string SalWithNetworks = """
        <?xml version="1.0" encoding="utf-8"?>
        <SafetyGroup>
          <Network Id="1">
            <FunctionBlock instanceName="FB_EStop1" typeName="FB_TwoHandControl">
              <Param name="tMonitoring" value="T#100ms" />
            </FunctionBlock>
            <Connection source="FB_EStop1.xOut" target="FB_Output.xIn" />
          </Network>
          <Network Id="2">
            <FunctionBlock instanceName="FB_DriveEnable" typeName="safeAnd">
            </FunctionBlock>
          </Network>
        </SafetyGroup>
        """;

    [Fact]
    public void Sal_ParsesNetworks_Count()
    {
        var result = SafetySalParser.ParseGroups(SalWithNetworks);
        Assert.Single(result);
        Assert.Equal(2, result[0].Networks.Count);
    }

    [Fact]
    public void Sal_ParsesFbInstanceAndParam()
    {
        var fb = SafetySalParser.ParseGroups(SalWithNetworks)[0].Networks[0].Instances[0];
        Assert.Equal("FB_EStop1", fb.InstanceName);
        Assert.Equal("FB_TwoHandControl", fb.TypeName);
        Assert.False(fb.Inferred);
        Assert.Equal("tMonitoring", Assert.Single(fb.Params).Name);
    }

    [Fact]
    public void Sal_ParsesConnection()
    {
        var conn = SafetySalParser.ParseGroups(SalWithNetworks)[0].Networks[0].Connections[0];
        Assert.Equal("FB_EStop1.xOut", conn.SourceMoniker);
        Assert.Equal("FB_Output.xIn", conn.TargetMoniker);
        Assert.False(conn.Inferred);
    }

    // ── Mappings.xml parser ───────────────────────────────────────────────────

    private const string MappingsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Mappings>
          <Mapping SignalName="SI101" Terminal="EL1918" Channel="1" SafeAddress="101" Function="E-Stop" />
          <Mapping SignalName="SI102" Terminal="EL1918" Channel="2" SafeAddress="102" Function="Safety Door" />
          <Mapping SignalName="SQ201" Terminal="EL2904" Channel="1" SafeAddress="201" Function="Drive Enable" SDSID="2" />
        </Mappings>
        """;

    [Fact]
    public void Mappings_ParsesFields_AndOnlyAnExplicitSdsId()
    {
        var result = SafetyMappingsParser.Parse(MappingsXml);
        Assert.Equal(3, result.Count);
        Assert.Equal(new MappingsEntry("SI101", "EL1918", "1", "101", "E-Stop", null), result[0]);
        Assert.Null(result[1].SdsId);
        Assert.Equal(2, result[2].SdsId);
    }

    // ── IO map (D-7): no fabricated join — two-device fixture ─────────────────

    private static string CopyFixtureProject(string tempRoot)
    {
        foreach (string f in Directory.EnumerateFiles(FixtureDir, "*", SearchOption.AllDirectories))
        {
            string dest = Path.Combine(tempRoot, Path.GetRelativePath(FixtureDir, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest);
        }
        return Path.Combine(tempRoot, "TemplateProject.splcproj");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ptc-safety-test-" + Guid.NewGuid().ToString("N")[..8]);
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
    }

    [Fact]
    public void IoMap_TwoDevices_EachUsageJoinedToItsOwnDevice()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        string report = SafetyIoMapReport.Render(SafetyProjectSnapshot.Load(splcproj));

        Assert.Contains("variable GroupPort_ErrAck (source) ← sdsId 1 channel 1 functionId 0 → ErrorAcknowledgement", report);
        Assert.Contains("variable GroupPort_RunStop (source) ← sdsId 2 channel 1 functionId 0 → Run", report);
        Assert.DoesNotContain("[inferred]", report);
        Assert.Contains("no Mappings.xml", report);
    }

    [Fact]
    public void IoMap_MappingRows_AreNotPairedWithTheFirstUsage()
    {
        // Regression for D-7: every Mappings.xml row used to get ioUsage[0]'s device and SDSID.
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        File.WriteAllText(Path.Combine(tmp.Path, "Mappings.xml"), MappingsXml);
        string report = SafetyIoMapReport.Render(SafetyProjectSnapshot.Load(splcproj));

        string[] lines = report.Split('\n');
        string si101 = lines.Single(l => l.Contains("SI101"));
        string si102 = lines.Single(l => l.Contains("SI102"));
        string sq201 = lines.Single(l => l.Contains("SQ201"));
        Assert.DoesNotContain("ErrorAcknowledgement", si101);
        Assert.DoesNotContain("SDSID", si101);
        Assert.DoesNotContain("SDSID", si102);
        Assert.Contains("device Run (SDSID 2)", sq201);   // the row's OWN key
    }

    [Fact]
    public void IoMap_UsageWithUnknownSdsId_SaysSo()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        string sal = Path.Combine(tmp.Path, "TwinSafeGroup1", "TwinSafeGroup1.sal");
        File.WriteAllText(sal, File.ReadAllText(sal).Replace("sdsId=\"2\" channelId=\"1\"", "sdsId=\"9\" channelId=\"1\""));
        string report = SafetyIoMapReport.Render(SafetyProjectSnapshot.Load(splcproj));
        Assert.Contains("sdsId 9 channel 1 functionId 0 → (no alias device with this SDSID)", report);
    }

    // ── .splcproj editor ──────────────────────────────────────────────────────

    [Fact]
    public void SplcprojEditor_AddsAliasDevice_InMsBuildNamespace()
    {
        var (modified, changed) = SafetySplcprojEditor.AddAliasDevice(
            SplcprojSample, @"Alias Devices\Projekt-Variablen\bSQ_NewSignal.sds", newNextSdsId: 8);

        Assert.True(changed);
        Assert.DoesNotContain("xmlns=\"\"", modified);
        var parsed = SafetySplcprojParser.Parse(modified);
        Assert.Contains(parsed.AliasDevices, a => a.IncludePath.Contains("bSQ_NewSignal"));
        Assert.Equal(8, parsed.NextSdsId);
    }

    [Fact]
    public void SplcprojEditor_AlreadyRegistered_IsUnchanged_IncludingNextSdsId()
    {
        // D-22: the idempotency check runs BEFORE NextSdsId is touched.
        var (modified, changed) = SafetySplcprojEditor.AddAliasDevice(
            SplcprojSample, @"alias devices/projekt-variablen/bSI_DriveReleased.sds", newNextSdsId: 99);
        Assert.False(changed);
        Assert.Equal(SplcprojSample, modified);
        Assert.Equal(7, SafetySplcprojParser.Parse(modified).NextSdsId);
    }

    [Fact]
    public void SplcprojEditor_PathWithQuote_IsRegisteredLiterally()
    {
        // D-22: the former XPath literal broke on a quote ("\'" is no XPath escape).
        var (modified, changed) = SafetySplcprojEditor.AddAliasDevice(SplcprojSample, @"Alias Devices\it's.sds", 8);
        Assert.True(changed);
        Assert.Contains(SafetySplcprojParser.Parse(modified).AliasDevices, a => a.IncludePath == @"Alias Devices\it's.sds");
        var (again, changedAgain) = SafetySplcprojEditor.AddAliasDevice(modified, @"Alias Devices\it's.sds", 9);
        Assert.False(changedAgain);
        Assert.Equal(modified, again);
    }

    [Fact]
    public void SplcprojEditor_NoNextSdsIdElement_DoesNotInventOne()
    {
        string template = File.ReadAllText(Path.Combine(FixtureDir, "TemplateProject.splcproj"));
        var (modified, _) = SafetySplcprojEditor.AddAliasDevice(template, @"TwinSafeGroup1\Alias Devices\bSI_X.sds", 5);
        Assert.DoesNotContain("NextSdsId", modified);
    }

    // ── .sds author vs the editor-authored sample ─────────────────────────────

    [Fact]
    public void SdsAuthor_DigitalInput_MatchesTheEditorAuthoredFile_ExceptCrcAndSdsId()
    {
        string editor = FixtureSds("ErrorAcknowledgement");   // Crc="879336946", SDSID 1, SubType 1
        string ours = SafetySdsAuthor.AuthorStandardDigital(sdsId: 1, subType: 1, fileFormatVersion: "1.5");

        static string Norm(string s) => s.TrimStart('﻿').Replace("\r\n", "\n").Trim();
        string expected = Norm(editor).Replace("Crc=\"879336946\"", "Crc=\"0\"");
        Assert.Equal(expected, Norm(ours));
    }

    [Fact]
    public void SdsAuthor_DigitalOutput_DiffersOnlyInSubTypeAndIoName()
    {
        string input = SafetySdsAuthor.AuthorStandardDigital(4, 1, "1.5");
        string output = SafetySdsAuthor.AuthorStandardDigital(4, 2, "1.5");
        Assert.Equal(input.Replace("<SubType>1</SubType>", "<SubType>2</SubType>").Replace("<Name>In</Name>", "<Name>Out</Name>"), output);
    }

    [Fact]
    public void SdsAuthor_UnsupportedSubType_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SafetySdsAuthor.AuthorStandardDigital(1, 3, "1.5"));

    // ── validation (SafetyAliasRules) ──────────────────────────────────────────

    [Theory]
    [InlineData(@"..\..\bSI_Evil")]
    [InlineData(@"bSI_a\b")]
    [InlineData("bSI_a/b")]
    [InlineData("bSI_a:b")]
    [InlineData("bSI a")]
    [InlineData(" bSI_a")]
    [InlineData("")]
    public void Rules_RejectsNamesThatAreNotPlainIdentifiers(string name)
        => Assert.StartsWith("[Error]", SafetyAliasRules.Validate(name, 2));

    [Fact]
    public void Rules_RejectsNameWithoutConfiguredPrefix_AndNamesTheEnvVar()
    {
        string? err = SafetyAliasRules.Validate("xDriveReleased", 2);
        Assert.StartsWith("[Error]", err);
        Assert.Contains(SafetySdsParser.PrefixesEnvVar, err);
    }

    [Fact]
    public void Rules_PrefixIsConfigurable()
    {
        string? previous = Environment.GetEnvironmentVariable(SafetySdsParser.PrefixesEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(SafetySdsParser.PrefixesEnvVar, "fx_");
            Assert.Null(SafetyAliasRules.Validate("fx_DriveReleased", 2));
            Assert.NotNull(SafetyAliasRules.Validate("bSI_DriveReleased", 2));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SafetySdsParser.PrefixesEnvVar, previous);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void Rules_OnlyDigitalStandardSubTypes(int subType)
        => Assert.StartsWith("[Error]", SafetyAliasRules.Validate("bSI_Ok", subType));

    [Fact]
    public void Rules_AcceptsDigitalInputAndOutput()
    {
        Assert.Null(SafetyAliasRules.Validate("bSQ_Request", 1));
        Assert.Null(SafetyAliasRules.Validate("bSI_Feedback", 2));
    }

    // ── writer (temp file + rollback + read-back) ──────────────────────────────

    [Fact]
    public void Writer_Add_WritesNextToGroupDevices_WithFreshSdsId_AndReadsBack()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);

        var r = SafetyAliasDeviceWriter.Add(splcproj, "bSI_DriveReleased", 2, group: null);

        Assert.Equal(@"TwinSafeGroup1\Alias Devices\bSI_DriveReleased.sds", r.RelativePath);
        Assert.Equal(3, r.SdsId);                       // SDSIDs 1 and 2 are taken
        Assert.Null(r.NextSdsIdWritten);                // the template has no <NextSdsId>
        string file = Path.Combine(tmp.Path, r.RelativePath);
        byte[] bytes = File.ReadAllBytes(file);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..]);   // BOM like the editor's files
        var parsed = SafetySdsParser.Parse(File.ReadAllText(file), AliasDeviceKind.Unknown, "bSI_DriveReleased")!;
        Assert.Equal((3, 1, 2, "Out"), (parsed.SdsId!.Value, parsed.Type!.Value, parsed.SubType, parsed.IoName!));
        Assert.Contains(SafetySplcprojParser.Parse(File.ReadAllText(splcproj)).AliasDevices,
            a => a.IncludePath == r.RelativePath);
        Assert.Equal(0, Directory.EnumerateFiles(tmp.Path, "*.tmp-*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public void Writer_Add_ExistingName_IsRefused_NothingWritten()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        SafetyAliasDeviceWriter.Add(splcproj, "bSI_A", 2, null);
        string before = File.ReadAllText(splcproj);

        var ex = Assert.Throws<InvalidOperationException>(() => SafetyAliasDeviceWriter.Add(splcproj, "BSI_a", 1, null));
        Assert.Contains("already exists", ex.Message);
        Assert.Equal(before, File.ReadAllText(splcproj));
    }

    [Fact]
    public void Writer_Add_BumpsExistingNextSdsId_PastEveryUsedSdsId()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        File.WriteAllText(splcproj, File.ReadAllText(splcproj)
            .Replace("<SPlcProjVersion>", "<NextSdsId>2</NextSdsId>\n    <SPlcProjVersion>"));

        var r = SafetyAliasDeviceWriter.Add(splcproj, "bSQ_Request", 1, null);

        Assert.Equal(3, r.SdsId);                       // NextSdsId=2 is already used by Run.sds
        Assert.Equal(4, r.NextSdsIdWritten);
        Assert.Equal(4, SafetySplcprojParser.Parse(File.ReadAllText(splcproj)).NextSdsId);
    }

    [Fact]
    public void Writer_ProjectWriteFails_RollsBackTheSds()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        string before = File.ReadAllText(splcproj);
        try
        {
            SafetyAliasDeviceWriter.BeforeProjectWrite = _ => throw new IOException("disk full (simulated)");
            var ex = Assert.Throws<InvalidOperationException>(() => SafetyAliasDeviceWriter.Add(splcproj, "bSI_B", 2, null));
            Assert.Contains("Rolled back", ex.Message);
        }
        finally
        {
            SafetyAliasDeviceWriter.BeforeProjectWrite = null;
        }

        Assert.False(File.Exists(Path.Combine(tmp.Path, "TwinSafeGroup1", "Alias Devices", "bSI_B.sds")));
        Assert.Equal(before, File.ReadAllText(splcproj));
        Assert.Equal(0, Directory.EnumerateFiles(tmp.Path, "*.tmp-*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public void Writer_UnregisteredFileAlreadyOnDisk_IsNotOverwritten()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        string stray = Path.Combine(tmp.Path, "TwinSafeGroup1", "Alias Devices", "bSI_C.sds");
        File.WriteAllText(stray, "keep me");

        Assert.Throws<InvalidOperationException>(() => SafetyAliasDeviceWriter.Add(splcproj, "bSI_C", 2, null));
        Assert.Equal("keep me", File.ReadAllText(stray));
    }

    [Fact]
    public void Writer_SeveralGroups_RequiresGroup()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        Directory.CreateDirectory(Path.Combine(tmp.Path, "G2"));
        File.WriteAllText(Path.Combine(tmp.Path, "G2", "G2.sal"), "<safetyApplication />");
        File.WriteAllText(splcproj, File.ReadAllText(splcproj)
            .Replace("<None Include=\"TargetSystemConfig.xml\">", "<None Include=\"G2\\G2.sal\" />\n    <None Include=\"TargetSystemConfig.xml\">"));

        var ex = Assert.Throws<InvalidOperationException>(() => SafetyAliasDeviceWriter.Add(splcproj, "bSI_D", 2, null));
        Assert.Contains("pass group", ex.Message);

        var r = SafetyAliasDeviceWriter.Add(splcproj, "bSI_D", 2, "G2");
        Assert.Equal(@"G2\Alias Devices\bSI_D.sds", r.RelativePath);
    }

    [Fact]
    public void Writer_InvalidName_ThrowsBeforeTouchingDisk()
    {
        using var tmp = new TempDir();
        string splcproj = CopyFixtureProject(tmp.Path);
        string before = File.ReadAllText(splcproj);
        Assert.Throws<InvalidOperationException>(() => SafetyAliasDeviceWriter.Add(splcproj, @"..\..\bSI_Evil", 2, null));
        Assert.Equal(before, File.ReadAllText(splcproj));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(tmp.Path)!, "bSI_Evil.sds")));
    }
}
