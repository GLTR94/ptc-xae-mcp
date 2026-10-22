using System.Globalization;
using System.Text;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>One alias device registered in the <c>.splcproj</c> and what its <c>.sds</c> file says.</summary>
internal sealed record SafetyAliasDeviceFile(
    string IncludePath,
    AliasDeviceKind Kind,
    SafetyDeviceInfo? Info,     // null when the file is missing or unreadable
    string? Problem);           // why Info is null

/// <summary>One <c>aliasDeviceIoUsage</c> with the group (<c>.sal</c>) it came from.</summary>
internal sealed record SafetyGroupIoUsage(string Group, SalIoUsageEntry Usage);

/// <summary>
/// Everything the read tools need from a TwinCAT Safety project, loaded from disk. COM-free, so it is
/// unit-tested against the Beckhoff-shipped template files (issue #153).
/// </summary>
internal sealed record SafetyProjectSnapshot(
    string SplcprojPath,
    SplcprojStructure Structure,
    IReadOnlyList<SafetyAliasDeviceFile> AliasDevices,
    IReadOnlyList<SafetyGroupIoUsage> IoUsage,
    IReadOnlyList<string> MissingGroups,
    IReadOnlyList<MappingsEntry> Mappings,
    bool HasMappingsFile)
{
    public string Directory => Path.GetDirectoryName(SplcprojPath)!;

    public static SafetyProjectSnapshot Load(string splcprojPath)
    {
        string dir = Path.GetDirectoryName(splcprojPath)
            ?? throw new InvalidOperationException($"'{splcprojPath}' has no directory.");
        SplcprojStructure proj = SafetySplcprojParser.Parse(File.ReadAllText(splcprojPath, Encoding.UTF8));

        var devices = new List<SafetyAliasDeviceFile>();
        foreach (var entry in proj.AliasDevices)
        {
            string path = Path.Combine(dir, entry.IncludePath);
            if (!File.Exists(path))
            {
                devices.Add(new SafetyAliasDeviceFile(entry.IncludePath, entry.Kind, null, "file not found"));
                continue;
            }
            var info = SafetySdsParser.Parse(File.ReadAllText(path, Encoding.UTF8), entry.Kind,
                Path.GetFileNameWithoutExtension(path));
            devices.Add(new SafetyAliasDeviceFile(entry.IncludePath, entry.Kind, info,
                info is null ? "not a readable SafetyDeviceDesc" : null));
        }

        var usage = new List<SafetyGroupIoUsage>();
        var missing = new List<string>();
        foreach (string salRel in proj.Groups)
        {
            string salPath = Path.Combine(dir, salRel);
            if (!File.Exists(salPath)) { missing.Add(salRel); continue; }
            string group = Path.GetFileNameWithoutExtension(salRel);
            foreach (var u in SafetySalParser.ParseIoUsage(File.ReadAllText(salPath, Encoding.UTF8)))
                usage.Add(new SafetyGroupIoUsage(group, u));
        }

        string mappingsPath = Path.Combine(dir, "Mappings.xml");
        bool hasMappings = File.Exists(mappingsPath);
        var mappings = hasMappings
            ? SafetyMappingsParser.Parse(File.ReadAllText(mappingsPath, Encoding.UTF8))
            : Array.Empty<MappingsEntry>();

        return new SafetyProjectSnapshot(splcprojPath, proj, devices, usage, missing, mappings, hasMappings);
    }
}

/// <summary>
/// Renders <c>twincat_safety_read_io_map</c> (issue #153, D-7 — release blocker). Prints only facts:
/// <list type="bullet">
/// <item>an I/O usage is joined to an alias device only by its <c>sdsId</c> = the device's
/// <c>&lt;SDSID&gt;</c> (the same key on both sides); an sdsId with no matching device says so;</item>
/// <item>a Mappings.xml row names a device/SDSID only when the row itself carries an SDSID — rows are
/// never paired with an I/O-usage entry by position (the former code gave EVERY row the first usage's
/// device, so all signals showed the same device and SDSID);</item>
/// <item>the <c>.sal</c> I/O usage is listed as its own section.</item>
/// </list>
/// </summary>
internal static class SafetyIoMapReport
{
    public static string Render(SafetyProjectSnapshot s)
    {
        var bySdsId = new Dictionary<int, List<SafetyAliasDeviceFile>>();
        foreach (var d in s.AliasDevices)
            if (d.Info?.SdsId is int id)
                (bySdsId.TryGetValue(id, out var l) ? l : bySdsId[id] = new List<SafetyAliasDeviceFile>()).Add(d);

        var sb = new StringBuilder();
        sb.AppendLine("=== Safe I/O Assignment ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Safety project: {Path.GetFileName(s.SplcprojPath)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Alias devices: {s.AliasDevices.Count} ({bySdsId.Count} with an SDSID)  " +
                      $"I/O usages (.sal aliasDeviceIoUsage): {s.IoUsage.Count}  " +
                      $"Mappings.xml rows: {(s.HasMappingsFile ? s.Mappings.Count.ToString(CultureInfo.InvariantCulture) : "no Mappings.xml")}");

        // ---- 1. alias devices -------------------------------------------------
        sb.AppendLine();
        sb.AppendLine("Alias devices (from the .sds files):");
        if (s.AliasDevices.Count == 0) sb.AppendLine("  (none registered in the .splcproj)");
        foreach (var d in s.AliasDevices.OrderBy(d => d.Info?.SdsId ?? int.MaxValue))
        {
            if (d.Info is null) { sb.AppendLine(CultureInfo.InvariantCulture, $"  {d.IncludePath}: {d.Problem}"); continue; }
            var i = d.Info;
            string sds = i.SdsId is int id ? id.ToString(CultureInfo.InvariantCulture) : "(none)";
            string type = i.Type is int t ? $"Type {t}/SubType {i.SubType}" : "(no AliasDeviceType)";
            string io = i.IoName is not null ? $"  IO={i.IoName}" : string.Empty;
            sb.AppendLine(CultureInfo.InvariantCulture, $"  SDSID {sds,-5} {i.Name}  {type}{io}  [{d.IncludePath}]");
        }
        foreach (var dup in bySdsId.Where(kv => kv.Value.Count > 1))
            sb.Append(CultureInfo.InvariantCulture, $"  [WARN] SDSID {dup.Key} is used by {dup.Value.Count} files: ")
              .AppendLine(string.Join(", ", dup.Value.Select(v => v.IncludePath)));

        // ---- 2. .sal I/O usage ------------------------------------------------
        sb.AppendLine();
        sb.AppendLine("Channel usage per group (.sal aliasDeviceIoUsage; device joined by sdsId = SDSID):");
        if (s.IoUsage.Count == 0) sb.AppendLine("  (no aliasDeviceIoUsage in any group file)");
        foreach (var g in s.IoUsage)
        {
            var u = g.Usage;
            string device = bySdsId.TryGetValue(u.SdsId, out var hits)
                ? (hits.Count == 1 ? hits[0].Info!.Name : $"ambiguous: {string.Join(" / ", hits.Select(h => h.Info!.Name))}")
                : "(no alias device with this SDSID)";
            string variable = u.Variable is not null ? $"variable {u.Variable}" : "(no enclosing variable)";
            string usage = u.Usage is not null ? $" ({u.Usage})" : string.Empty;
            string fn = u.FunctionId is int f ? $" functionId {f}" : string.Empty;
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [{g.Group}] {variable}{usage} ← sdsId {u.SdsId} channel {u.ChannelId}{fn} → {device}");
        }
        foreach (string m in s.MissingGroups)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [WARN] group file not found: {m}");

        // ---- 3. Mappings.xml --------------------------------------------------
        if (s.HasMappingsFile)
        {
            sb.AppendLine();
            sb.AppendLine("Mappings.xml (as found; a row names a device only if it carries its own SDSID):");
            if (s.Mappings.Count == 0) sb.AppendLine("  (no rows with a signal name)");
            foreach (var m in s.Mappings)
            {
                string dev = m.SdsId is int id
                    ? (bySdsId.TryGetValue(id, out var h) && h.Count == 1
                        ? $"  device {h[0].Info!.Name} (SDSID {id})"
                        : $"  SDSID {id} (no single alias device with this SDSID)")
                    : string.Empty;
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {m.SignalName}  terminal={m.Terminal ?? "-"}  channel={m.Channel ?? "-"}  " +
                              $"safeAddress={m.SafeAddress ?? "-"}  function={m.Function ?? "-"}{dev}");
            }
        }

        return sb.ToString();
    }
}
