using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class LicenseInfoParserTests
{
    // ── ParseStatus ────────────────────────────────────────────────────────

    // LicenseStatus is internal — use int for InlineData to avoid CS0051.
    // Values: Present=0, Trial=1, Expired=2, Missing=3, Unknown=4.

    [Theory]
    [InlineData("present",  0 /* Present */)]
    [InlineData("Present",  0)]
    [InlineData("PRESENT",  0)]
    [InlineData("valid",    0)]
    [InlineData("ok",       0)]
    [InlineData("active",   0)]
    [InlineData("1",        0)]
    [InlineData("trial",    1 /* Trial */)]
    [InlineData("Trial",    1)]
    [InlineData("test",     1)]
    [InlineData("expired",  2 /* Expired */)]
    [InlineData("Expired",  2)]
    [InlineData("missing",  3 /* Missing */)]
    [InlineData("0",        3)]
    [InlineData("invalid",  3)]
    [InlineData("",         4 /* Unknown */)]
    [InlineData("  ",       4)]
    [InlineData("unknown",  4)]
    [InlineData("foobar",   4)]
    public void ParseStatus_VariousInputs(string input, int expectedInt)
    {
        Assert.Equal((LicenseStatus)expectedInt, LicenseInfoParser.ParseStatus(input));
    }

    // ── ParseDate ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-12-31", 2026, 12, 31)]
    [InlineData("31.12.2026", 2026, 12, 31)]
    [InlineData("12/31/2026", 2026, 12, 31)]
    [InlineData("20261231",   2026, 12, 31)]
    public void ParseDate_ValidFormats_ReturnDate(string input, int y, int m, int d)
    {
        var result = LicenseInfoParser.ParseDate(input);
        Assert.NotNull(result);
        Assert.Equal(y, result!.Value.Year);
        Assert.Equal(m, result.Value.Month);
        Assert.Equal(d, result.Value.Day);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-date")]
    [InlineData("99999999")]
    public void ParseDate_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(LicenseInfoParser.ParseDate(input));
    }

    // ── Parse (XML) ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyLicenseInfoElement_ReturnsEmptyList()
    {
        var result = LicenseInfoParser.Parse("<LicenseInfo/>");
        Assert.Empty(result.Licenses);
        Assert.Equal("<LicenseInfo/>", result.RawXml);
    }

    [Fact]
    public void Parse_SingleLicenseEntry_ElementSchema()
    {
        string xml = """
            <LicenseInfo>
              <LicenseEntry>
                <LicenseId>TC1200</LicenseId>
                <LicenseName>TwinCAT PLC Runtime</LicenseName>
                <LicenseStatus>Trial</LicenseStatus>
                <ExpiryDate>2026-06-15</ExpiryDate>
              </LicenseEntry>
            </LicenseInfo>
            """;

        var result = LicenseInfoParser.Parse(xml);

        Assert.Single(result.Licenses);
        var lic = result.Licenses[0];
        Assert.Equal("TC1200", lic.Id);
        Assert.Equal("TwinCAT PLC Runtime", lic.Name);
        Assert.Equal(LicenseStatus.Trial, lic.Status);
        Assert.NotNull(lic.ExpiryDate);
        Assert.Equal(2026, lic.ExpiryDate!.Value.Year);
        Assert.Equal(6,    lic.ExpiryDate.Value.Month);
        Assert.Equal(15,   lic.ExpiryDate.Value.Day);
    }

    [Fact]
    public void Parse_MultipleLicenseEntries_AllParsed()
    {
        string xml = """
            <LicenseInfo>
              <LicenseEntry>
                <LicenseId>TC1000</LicenseId>
                <LicenseName>TwinCAT Base</LicenseName>
                <LicenseStatus>Present</LicenseStatus>
                <ExpiryDate></ExpiryDate>
              </LicenseEntry>
              <LicenseEntry>
                <LicenseId>TC1200</LicenseId>
                <LicenseName>TwinCAT PLC</LicenseName>
                <LicenseStatus>Trial</LicenseStatus>
                <ExpiryDate>2026-12-31</ExpiryDate>
              </LicenseEntry>
              <LicenseEntry>
                <LicenseId>TC9999</LicenseId>
                <LicenseName>Unknown Feature</LicenseName>
                <LicenseStatus>Missing</LicenseStatus>
                <ExpiryDate></ExpiryDate>
              </LicenseEntry>
            </LicenseInfo>
            """;

        var result = LicenseInfoParser.Parse(xml);

        Assert.Equal(3, result.Licenses.Count);
        Assert.Equal(LicenseStatus.Present, result.Licenses[0].Status);
        Assert.Equal(LicenseStatus.Trial,   result.Licenses[1].Status);
        Assert.Equal(LicenseStatus.Missing, result.Licenses[2].Status);
        Assert.Null(result.Licenses[0].ExpiryDate);
        Assert.NotNull(result.Licenses[1].ExpiryDate);
    }

    [Fact]
    public void Parse_AttributeBasedSchema_Parsed()
    {
        string xml = """
            <LicenseInfo>
              <License id="TC1200" name="TwinCAT PLC" status="trial" expiryDate="2026-09-30"/>
            </LicenseInfo>
            """;

        var result = LicenseInfoParser.Parse(xml);

        Assert.Single(result.Licenses);
        var lic = result.Licenses[0];
        Assert.Equal("TC1200",      lic.Id);
        Assert.Equal("TwinCAT PLC", lic.Name);
        Assert.Equal(LicenseStatus.Trial, lic.Status);
        Assert.Equal(2026, lic.ExpiryDate?.Year);
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsEmptyList()
    {
        var result = LicenseInfoParser.Parse("this is not xml <<>>");
        Assert.Empty(result.Licenses);
    }

    [Fact]
    public void Parse_RawXmlPreserved()
    {
        string xml = "<LicenseInfo><LicenseEntry/></LicenseInfo>";
        var result = LicenseInfoParser.Parse(xml);
        Assert.Equal(xml, result.RawXml);
    }

    // ── FormatLicenseInfo (BuildCoordinator) ──────────────────────────────

    [Fact]
    public void FormatLicenseInfo_EmptyList_ReportsNoEntries()
    {
        var info = new LicenseInfoResult(Array.Empty<LicenseEntry>(), "<LicenseInfo/>");
        string text = BuildCoordinator.FormatLicenseInfo(info);
        Assert.Contains("No license entries", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatLicenseInfo_SingleEntry_ContainsNameAndStatus()
    {
        var entries = new[]
        {
            new LicenseEntry("TC1200", "TwinCAT PLC", LicenseStatus.Trial,
                new DateTime(2026, 12, 31)),
        };
        var info = new LicenseInfoResult(entries, "<xml/>");
        string text = BuildCoordinator.FormatLicenseInfo(info);

        Assert.Contains("TwinCAT PLC", text);
        Assert.Contains("Trial", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-12-31", text);
    }

    // ── ParseStatus — full keyword matrix (kills mutation survivors) ──────

    [Theory]
    [InlineData("testlicense", 1 /* Trial */)]
    [InlineData("expiredtrial", 2 /* Expired */)]
    [InlineData("notfound",    3 /* Missing */)]
    public void ParseStatus_AdditionalKeywords(string input, int expectedInt)
    {
        Assert.Equal((LicenseStatus)expectedInt, LicenseInfoParser.ParseStatus(input));
    }

    // ── Child() alias tests — each alternate element name must resolve ─────
    // The Child() call tries aliases in order: first found non-empty wins.
    // We omit the primary alias so the fallback alias is exercised.

    [Theory]
    [InlineData("<LicenseEntry><Id>TC-ID</Id><LicenseName>N</LicenseName><LicenseStatus>present</LicenseStatus></LicenseEntry>", "TC-ID")]
    [InlineData("<LicenseEntry><id>TC-id</id><LicenseName>N</LicenseName><LicenseStatus>present</LicenseStatus></LicenseEntry>", "TC-id")]
    public void Parse_ElementAlias_Id(string fragment, string expectedId)
    {
        var result = LicenseInfoParser.Parse($"<LicenseInfo>{fragment}</LicenseInfo>");
        Assert.Single(result.Licenses);
        Assert.Equal(expectedId, result.Licenses[0].Id);
    }

    [Theory]
    [InlineData("<LicenseEntry><LicenseId>TC1200</LicenseId><Name>AltName</Name><LicenseStatus>present</LicenseStatus></LicenseEntry>", "AltName")]
    [InlineData("<LicenseEntry><LicenseId>TC1200</LicenseId><name>AltNameLower</name><LicenseStatus>present</LicenseStatus></LicenseEntry>", "AltNameLower")]
    public void Parse_ElementAlias_Name(string fragment, string expectedName)
    {
        var result = LicenseInfoParser.Parse($"<LicenseInfo>{fragment}</LicenseInfo>");
        Assert.Single(result.Licenses);
        Assert.Equal(expectedName, result.Licenses[0].Name);
    }

    [Theory]
    [InlineData("<LicenseEntry><LicenseId>TC1200</LicenseId><LicenseName>N</LicenseName><Status>trial</Status></LicenseEntry>")]
    [InlineData("<LicenseEntry><LicenseId>TC1200</LicenseId><LicenseName>N</LicenseName><state>trial</state></LicenseEntry>")]
    public void Parse_ElementAlias_Status(string fragment)
    {
        var result = LicenseInfoParser.Parse($"<LicenseInfo>{fragment}</LicenseInfo>");
        Assert.Single(result.Licenses);
        Assert.Equal(LicenseStatus.Trial, result.Licenses[0].Status);
    }

    [Theory]
    [InlineData("<LicenseEntry><LicenseId>TC1200</LicenseId><LicenseName>N</LicenseName><LicenseStatus>trial</LicenseStatus><Expiry>2026-12-31</Expiry></LicenseEntry>")]
    [InlineData("<LicenseEntry><LicenseId>TC1200</LicenseId><LicenseName>N</LicenseName><LicenseStatus>trial</LicenseStatus><ExpirationDate>2026-12-31</ExpirationDate></LicenseEntry>")]
    public void Parse_ElementAlias_Expiry(string fragment)
    {
        var result = LicenseInfoParser.Parse($"<LicenseInfo>{fragment}</LicenseInfo>");
        Assert.Single(result.Licenses);
        var expiry = result.Licenses[0].ExpiryDate;
        Assert.True(expiry.HasValue, "ExpiryDate should have a value");
        Assert.Equal(2026, expiry.GetValueOrDefault().Year);
        Assert.Equal(12,   expiry.GetValueOrDefault().Month);
        Assert.Equal(31,   expiry.GetValueOrDefault().Day);
    }

    // ── Attr() alias tests — fallback attribute names must resolve ─────────
    // Elements are absent so the attribute fallback is exercised.

    [Fact]
    public void Parse_AttributeAlias_licenseId()
    {
        const string xml = """
            <LicenseInfo>
              <License licenseId="TC-ALT" name="N" status="present"/>
            </LicenseInfo>
            """;
        var result = LicenseInfoParser.Parse(xml);
        Assert.Single(result.Licenses);
        Assert.Equal("TC-ALT", result.Licenses[0].Id);
    }

    [Fact]
    public void Parse_AttributeAlias_licenseName()
    {
        const string xml = """
            <LicenseInfo>
              <License id="TC1200" licenseName="AltAttrName" status="present"/>
            </LicenseInfo>
            """;
        var result = LicenseInfoParser.Parse(xml);
        Assert.Single(result.Licenses);
        Assert.Equal("AltAttrName", result.Licenses[0].Name);
    }

    [Fact]
    public void Parse_AttributeAlias_licenseStatus()
    {
        const string xml = """
            <LicenseInfo>
              <License id="TC1200" name="N" licenseStatus="trial"/>
            </LicenseInfo>
            """;
        var result = LicenseInfoParser.Parse(xml);
        Assert.Single(result.Licenses);
        Assert.Equal(LicenseStatus.Trial, result.Licenses[0].Status);
    }

    [Fact]
    public void Parse_AttributeAlias_expiry()
    {
        const string xml = """
            <LicenseInfo>
              <License id="TC1200" name="N" status="trial" expiry="2026-12-31"/>
            </LicenseInfo>
            """;
        var result = LicenseInfoParser.Parse(xml);
        Assert.Single(result.Licenses);
        Assert.True(result.Licenses[0].ExpiryDate.HasValue, "ExpiryDate should have a value");
        Assert.Equal(2026, result.Licenses[0].ExpiryDate.GetValueOrDefault().Year);
    }

    // ── ParseDate — format-specific inputs (kills format-string mutations) ─

    [Theory]
    // yyyyMMdd: not parseable by DateTime.TryParse on any standard culture.
    [InlineData("20261231", 2026, 12, 31)]
    // MM/dd/yyyy: on de-DE culture, TryParse cannot parse M/d/yyyy with slashes.
    [InlineData("12/31/2026", 2026, 12, 31)]
    // dd.MM.yyyy: exercises the explicit format path (on invariant-culture TryParse).
    [InlineData("31.12.2026", 2026, 12, 31)]
    public void ParseDate_ExplicitFormatPath(string input, int y, int m, int d)
    {
        var result = LicenseInfoParser.ParseDate(input);
        Assert.NotNull(result);
        var dt = result.GetValueOrDefault();
        Assert.Equal(y, dt.Year);
        Assert.Equal(m, dt.Month);
        Assert.Equal(d, dt.Day);
    }
}
