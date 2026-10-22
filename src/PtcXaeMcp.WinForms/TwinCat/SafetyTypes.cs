namespace PtcXaeMcp.WinForms.TwinCat;

// ── Classification ────────────────────────────────────────────────────────────

internal enum AliasDeviceKind
{
    Hardware,        // bound to physical FSoE/TSC terminal channel
    ProjectVariable, // signal exchanged with the standard PLC (the writable kind)
    SystemVariable,  // group diagnostics (_bSI_*/_bSQ_*)
    Unknown,
}

// ── .splcproj parsed structure ────────────────────────────────────────────────

internal sealed record SplcprojStructure(
    IReadOnlyList<SplcprojAliasEntry> AliasDevices,
    IReadOnlyList<string> Groups,    // .sal paths relative to project dir
    IReadOnlyList<string> UserFBs,   // .ufb paths relative to project dir
    int? NextSdsId);                 // null when the .splcproj has no <NextSdsId>

internal sealed record SplcprojAliasEntry(
    string IncludePath,             // as written in <None Include="...">
    AliasDeviceKind Kind);          // inferred from folder segment of IncludePath

// ── .sds parsed info ─────────────────────────────────────────────────────────
//
// Real format (Beckhoff-shipped templates under
// %TWINCAT3DIR%Components\Base\SafetyTemplate\Templates.3.0.0, and
// AliasDeviceTemplateSettings\Standard IO.xml):
//   <SafetyDeviceDesc Crc="…" FileFormatVersion="1.5">
//     <AliasDeviceType><Type>1</Type><SubType>1</SubType><VendorId>0</VendorId></AliasDeviceType>
//     <SDSID>1</SDSID>
//     <StandardAliasDevice><LinkingMode>Manual</LinkingMode><IO><Name>In</Name>…</IO></StandardAliasDevice>
//   </SafetyDeviceDesc>
// The device NAME is the file name — the file itself carries no name.

internal sealed record SafetyDeviceInfo(
    string Name,                    // .sds file name without extension
    int? SdsId,                     // <SDSID>; null when the file carries none
    int SubType,                    // <AliasDeviceType><SubType>; 0 when absent
    string? LinkingMode,            // <StandardAliasDevice><LinkingMode>
    string? SignalName,             // file name when it matches the configured signal prefixes
    AliasDeviceKind Kind,           // folder-derived classification (see SafetySplcprojParser)
    int? Type = null,               // <AliasDeviceType><Type>; 1 = standard (non-safe PLC) alias device
    string? IoName = null,          // <IO><Name> of a standard alias device ("In"/"Out")
    string? FileFormatVersion = null);

// ── .sal IO-map assignment ────────────────────────────────────────────────────

/// <summary>
/// One <c>aliasDeviceIoUsage</c> (sdsId + channelId) of a <c>.sal</c> group file. <paramref name="Variable"/>
/// is the enclosing <c>&lt;variable name=…&gt;</c>; <paramref name="Usage"/> is "source" (the variable is read
/// from the alias-device channel) or "target" (the variable is written to it), from the enclosing
/// <c>sourceUsages</c>/<c>targetUsages</c> element. Both null when the file does not nest it that way.
/// </summary>
internal sealed record SalIoUsageEntry(
    int SdsId,
    int ChannelId,
    string? Variable = null,
    string? Usage = null,
    int? FunctionId = null);

// ── Mappings.xml assignment ───────────────────────────────────────────────────

internal sealed record MappingsEntry(
    string SignalName,              // SIxxx / SQxxx
    string? Terminal,
    string? Channel,
    string? SafeAddress,
    string? Function,
    int? SdsId = null);             // only when the row itself carries an SDSID attribute

// ── .sal FBD network dump ─────────────────────────────────────────────────────

internal sealed record SalGroup(
    string Name,
    IReadOnlyList<SalNetwork> Networks);

internal sealed record SalNetwork(
    string? Id,
    IReadOnlyList<SalFbInstance> Instances,
    IReadOnlyList<SalConnection> Connections);

internal sealed record SalFbInstance(
    string InstanceName,
    string? TypeName,
    IReadOnlyList<SalFbParam> Params,
    bool Inferred);

internal sealed record SalFbParam(string Name, string Value);

internal sealed record SalConnection(
    string SourceMoniker,
    string TargetMoniker,
    bool Inferred);
