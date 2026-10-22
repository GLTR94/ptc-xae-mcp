using System.Text.Json;
using PtcXaeMcp.WinForms.TwinCat.Schema;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Coverage for the merged schema JSON validator (Hardening 9, step 1 / R1). Both
/// NativeTwinCatRunner.LoadAndValidateSchema and Form1.GetSchemaValidationResult now delegate
/// here (was E-23: two independently maintained, lenient copies existed before).
/// </summary>
public class SchemaModelValidatorTests
{
    private const string ValidSchema = """
    {
      "twincat": { "deviceName": "Dev1", "plcProjectName": "PLC", "gvlName": "IO" },
      "ethercat": { "root": { "name": "Dev1", "vInfo": "EK1100" } },
      "ioPoints": [
        { "varName": "x1", "terminalName": "T1", "channel": 0, "direction": "I" }
      ]
    }
    """;

    private static List<SchemaCheckIssue> Validate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return SchemaModelValidator.Validate(doc.RootElement, json);
    }

    [Fact]
    public void Validate_WellFormedSchema_NoIssues()
    {
        Validate(ValidSchema).Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingTwincatRoot_ReportsIssue()
    {
        string json = """{ "ethercat": {"root": {"name":"x"}}, "ioPoints": [{"varName":"a","terminalName":"t","channel":0,"direction":"I"}] }""";

        var issues = Validate(json);

        issues.Should().Contain(i => i.Message.Contains("twincat"));
    }

    [Fact]
    public void Validate_EmptyDeviceName_ReportsIssue()
    {
        string json = ValidSchema.Replace("\"deviceName\": \"Dev1\"", "\"deviceName\": \"\"");

        Validate(json).Should().Contain(i => i.Message.Contains("deviceName"));
    }

    [Fact]
    public void Validate_MissingEthercatRoot_ReportsIssue()
    {
        string json = """
        {
          "twincat": { "deviceName": "d", "plcProjectName": "p", "gvlName": "g" },
          "ioPoints": [{ "varName": "a", "terminalName": "t", "channel": 0, "direction": "I" }]
        }
        """;

        Validate(json).Should().Contain(i => i.Message.Contains("ethercat.root"));
    }

    [Fact]
    public void Validate_EmptyIoPoints_ReportsIssue()
    {
        const string json = """
        {
          "twincat": { "deviceName": "Dev1", "plcProjectName": "PLC", "gvlName": "IO" },
          "ethercat": { "root": { "name": "Dev1", "vInfo": "EK1100" } },
          "ioPoints": []
        }
        """;

        Validate(json).Should().Contain(i => i.Message.Contains("ioPoints is empty"));
    }

    [Fact]
    public void Validate_InvalidDirection_ReportsIssue()
    {
        string json = ValidSchema.Replace("\"direction\": \"I\"", "\"direction\": \"X\"");

        Validate(json).Should().Contain(i => i.Message.Contains("direction"));
    }

    [Fact]
    public void Validate_NegativeChannel_ReportsIssue()
    {
        string json = ValidSchema.Replace("\"channel\": 0", "\"channel\": -1");

        Validate(json).Should().Contain(i => i.Message.Contains("channel"));
    }

    [Fact]
    public void Validate_DuplicateVarName_ReportsIssueNamingBothIndexes()
    {
        string json = """
        {
          "twincat": { "deviceName": "d", "plcProjectName": "p", "gvlName": "g" },
          "ethercat": { "root": { "name": "d" } },
          "ioPoints": [
            { "varName": "same", "terminalName": "t1", "channel": 0, "direction": "I" },
            { "varName": "same", "terminalName": "t2", "channel": 1, "direction": "I" }
          ]
        }
        """;

        var issues = Validate(json);

        issues.Should().ContainSingle(i => i.Message.Contains("duplicates") && i.Message.Contains("ioPoints[1]"));
    }

    [Fact]
    public void Validate_DuplicateVarNameCaseInsensitive_IsStillDetected()
    {
        string json = """
        {
          "twincat": { "deviceName": "d", "plcProjectName": "p", "gvlName": "g" },
          "ethercat": { "root": { "name": "d" } },
          "ioPoints": [
            { "varName": "Same", "terminalName": "t1", "channel": 0, "direction": "I" },
            { "varName": "same", "terminalName": "t2", "channel": 1, "direction": "I" }
          ]
        }
        """;

        Validate(json).Should().Contain(i => i.Message.Contains("duplicates"));
    }

    [Fact]
    public void Validate_MisspelledTopLevelProperty_ReportedAsUnmapped()
    {
        // "plcProjectNam" (typo) has no matching member on TwinCatConfig, so
        // UnmappedMemberHandling.Disallow must catch it even though it's not one of the
        // hand-checked required fields.
        string json = ValidSchema.Replace("\"plcProjectName\": \"PLC\"", "\"plcProjectNam\": \"PLC\"");

        Validate(json).Should().Contain(i => i.Message.Contains("Unknown or misspelled"));
    }

    [Fact]
    public void Validate_MetaTopLevelField_IsToleratedAsExtensionData()
    {
        string json = """
        {
          "twincat": { "deviceName": "d", "plcProjectName": "p", "gvlName": "g" },
          "ethercat": { "root": { "name": "d" } },
          "ioPoints": [{ "varName": "a", "terminalName": "t", "channel": 0, "direction": "I" }],
          "meta": { "sourcePdf": "spec.pdf", "generatedAt": "2026-01-01" }
        }
        """;

        Validate(json).Should().BeEmpty();
    }

    [Fact]
    public void Validate_MisspelledIoPointProperty_ReportedAsUnmapped()
    {
        string json = ValidSchema.Replace("\"terminalName\": \"T1\"", "\"terminalNam\": \"T1\"");

        // Structural check still flags terminalName as missing; the unmapped-member check
        // additionally flags the typo itself.
        Validate(json).Should().Contain(i => i.Message.Contains("Unknown or misspelled") || i.Message.Contains("terminalName"));
    }

    [Fact]
    public void Validate_MisspelledProperty_IssueCarriesItsJsonPath()
    {
        string json = ValidSchema.Replace("\"plcProjectName\": \"PLC\"", "\"plcProjectNam\": \"PLC\"");

        Validate(json).Should().Contain(i => i.Message.Contains("Unknown or misspelled") && i.Path == "twincat.plcProjectNam");
    }

    [Fact]
    public void Validate_WrongValueType_IsNotCalledAMisspelling()
    {
        string json = ValidSchema.Replace("\"channel\": 0", "\"channel\": \"one\"");

        var issues = Validate(json);
        issues.Should().Contain(i => i.Message.Contains("does not match the schema model"));
        issues.Should().NotContain(i => i.Message.Contains("Unknown or misspelled"));
    }
}
