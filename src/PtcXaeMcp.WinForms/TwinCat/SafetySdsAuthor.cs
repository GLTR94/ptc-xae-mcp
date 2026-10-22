using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Authors a new TwinCAT Safety alias-device description (<c>*.sds</c>) for a <em>standard</em>
/// alias device — a single BOOL exchanged between the safety logic and the standard PLC.
/// <para>
/// The template reproduces, element for element, the files the TwinSAFE editor itself ships
/// (<c>Components\Base\SafetyTemplate\Templates\1.3.0.0\…\1 digital input (Standard)_1.sds</c>;
/// captured verbatim under <c>src/PtcXaeMcp.Tests/Fixtures/Safety</c>, where a test compares the two).
/// The type ids come from <c>Components\Safety\AliasDeviceTemplateSettings\Standard IO.xml</c>:
/// <c>Type 1 / SubType 1</c> = "1 Digital Input (Standard)" (standard PLC → safety, IO name <c>In</c>),
/// <c>Type 1 / SubType 2</c> = "1 Digital Output (Standard)" (safety → standard PLC, IO name <c>Out</c>).
/// </para>
/// <para>
/// The <c>Crc</c> attribute is written as <c>0</c>: the CRC is computed by "Verify Complete Safety
/// Project" in the TwinSAFE editor (IEC 61508 T3 boundary). This tool must NEVER forge or compute it.
/// </para>
/// </summary>
internal static class SafetySdsAuthor
{
    /// <summary><c>&lt;AliasDeviceType&gt;&lt;Type&gt;</c> of a standard (non-safe PLC) alias device.</summary>
    public const int StandardAliasDeviceType = 1;

    /// <summary>"1 Digital Input (Standard)": the standard PLC writes, the safety logic reads.</summary>
    public const int DigitalInputSubType = 1;

    /// <summary>"1 Digital Output (Standard)": the safety logic writes, the standard PLC reads.</summary>
    public const int DigitalOutputSubType = 2;

    /// <summary>FileFormatVersion of the Beckhoff 1.3.0.0 templates — used when the project has no sibling to copy it from.</summary>
    public const string DefaultFileFormatVersion = "1.5";

    /// <summary>Human-readable name of a supported sub type (the TwinSAFE editor's own wording).</summary>
    public static string DescribeSubType(int subType) => subType switch
    {
        DigitalInputSubType => "1 Digital Input (Standard): standard PLC → safety",
        DigitalOutputSubType => "1 Digital Output (Standard): safety → standard PLC",
        _ => $"SubType {subType}",
    };

    /// <summary>Authors the <c>.sds</c> XML (CRLF line endings; the BOM is added by the file layer).</summary>
    internal static string AuthorStandardDigital(int sdsId, int subType, string fileFormatVersion)
    {
        if (subType is not (DigitalInputSubType or DigitalOutputSubType))
            throw new ArgumentOutOfRangeException(nameof(subType), subType,
                "Only SubType 1 (digital input) and 2 (digital output) standard alias devices are supported.");

        string ioName = subType == DigitalInputSubType ? "In" : "Out";
        string id = sdsId.ToString(CultureInfo.InvariantCulture);
        string st = subType.ToString(CultureInfo.InvariantCulture);
        return string.Join("\r\n",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            $"<SafetyDeviceDesc Crc=\"0\" FileFormatVersion=\"{fileFormatVersion}\">",
            "  <AliasDeviceType>",
            $"    <Type>{StandardAliasDeviceType}</Type>",
            $"    <SubType>{st}</SubType>",
            "    <VendorId>0</VendorId>",
            "  </AliasDeviceType>",
            $"  <SDSID>{id}</SDSID>",
            "  <StandardAliasDevice>",
            "    <LinkingMode>Manual</LinkingMode>",
            "    <IO>",
            $"      <Name>{ioName}</Name>",
            "      <BitOffsMessage>0</BitOffsMessage>",
            "      <DataType>BIT</DataType>",
            "      <BitSize>1</BitSize>",
            "    </IO>",
            "  </StandardAliasDevice>",
            "</SafetyDeviceDesc>");
    }
}
