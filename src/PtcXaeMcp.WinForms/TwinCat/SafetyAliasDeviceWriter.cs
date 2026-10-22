using System.Globalization;
using System.Text;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>Result of <see cref="SafetyAliasDeviceWriter.Add"/> — what was written, never what was intended.</summary>
internal sealed record SafetyAliasAddResult(
    string SignalName,
    string RelativePath,
    int SdsId,
    int SubType,
    string FileFormatVersion,
    string FileFormatVersionSource,
    int? NextSdsIdWritten,     // null: the .splcproj has no <NextSdsId> and none was added
    string Group);

/// <summary>
/// Input rules for <c>twincat_safety_add_alias_device</c> (issue #153, D-8). COM-free.
/// </summary>
internal static class SafetyAliasRules
{
    public static readonly int[] SupportedSubTypes =
        [SafetySdsAuthor.DigitalInputSubType, SafetySdsAuthor.DigitalOutputSubType];

    /// <summary>Null when valid, else an "[Error] …" message. Nothing is touched on disk.</summary>
    public static string? Validate(string? signalName, int subType)
    {
        if (string.IsNullOrWhiteSpace(signalName))
            return "[Error] signalName is required.";

        // The name becomes the .sds FILE name and the variable name in the TwinSAFE editor: it must be a
        // plain IEC identifier — that also rules out '\', '/', ':', '..' and every other path element.
        string trimmed = signalName.Trim();
        if (!string.Equals(trimmed, signalName, StringComparison.Ordinal))
            return $"[Error] signalName '{signalName}' has leading/trailing whitespace.";
        if (IecIdentifier.Validate(trimmed, "signalName") is { } iecError)
            return McpErrors.EnsureError(iecError);

        string[] prefixes = SafetySdsParser.SignalPrefixes();
        if (!SafetySdsParser.IsSignalName(trimmed))
            return $"[Error] signalName '{signalName}' does not start with a configured safety-signal prefix " +
                   $"({string.Join(", ", prefixes)}). The prefixes are a project naming convention, set with " +
                   $"{SafetySdsParser.PrefixesEnvVar} (comma-separated).";

        if (!SupportedSubTypes.Contains(subType))
            return $"[Error] subType {subType} is not supported. Use {SafetySdsAuthor.DigitalInputSubType} " +
                   $"({SafetySdsAuthor.DescribeSubType(SafetySdsAuthor.DigitalInputSubType)}) or " +
                   $"{SafetySdsAuthor.DigitalOutputSubType} ({SafetySdsAuthor.DescribeSubType(SafetySdsAuthor.DigitalOutputSubType)}) " +
                   "— the Type 1 standard digital alias devices of the TwinSAFE editor (Standard IO.xml).";

        return null;
    }
}

/// <summary>
/// Creates a standard digital alias device in a TwinCAT Safety project on disk (issue #153, D-8):
/// validates, allocates an SDSID that no existing <c>.sds</c> uses, writes the <c>.sds</c> through a temp
/// file, registers it in the <c>.splcproj</c> through a temp file, and rolls the <c>.sds</c> (and any
/// directory it created) back when the project file cannot be updated. COM-free — the XAE reload is the
/// caller's job.
/// </summary>
internal static class SafetyAliasDeviceWriter
{
    /// <summary>Test seam: runs between the .sds write and the .splcproj replace.</summary>
    internal static Action<string>? BeforeProjectWrite { get; set; }

    public static SafetyAliasAddResult Add(string splcprojPath, string signalName, int subType, string? group)
    {
        if (SafetyAliasRules.Validate(signalName, subType) is { } invalid)
            throw new InvalidOperationException(invalid.StartsWith("[Error] ", StringComparison.Ordinal) ? invalid[8..] : invalid);

        string projectDir = Path.GetFullPath(Path.GetDirectoryName(splcprojPath)
            ?? throw new InvalidOperationException($"'{splcprojPath}' has no directory."));
        var snap = SafetyProjectSnapshot.Load(splcprojPath);

        // ---- idempotency / collisions — before anything is written ----------------
        var clash = snap.AliasDevices.FirstOrDefault(a =>
            string.Equals(Path.GetFileNameWithoutExtension(a.IncludePath), signalName, StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
            throw new InvalidOperationException(
                $"Alias device '{signalName}' already exists in the safety project ({clash.IncludePath}). Nothing was changed.");

        // ---- group + target folder ------------------------------------------------
        string groupRel = ResolveGroup(snap.Structure.Groups, group);
        string groupDir = Path.GetDirectoryName(groupRel) ?? string.Empty;
        string targetDir = PickTargetDirectory(snap, groupDir);
        string relPath = Path.Combine(targetDir, signalName + ".sds");

        string fullPath = Path.GetFullPath(Path.Combine(projectDir, relPath));
        if (!fullPath.StartsWith(projectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to write outside the safety project folder: {fullPath}");
        if (File.Exists(fullPath))
            throw new InvalidOperationException(
                $"'{relPath}' already exists on disk but is not registered in the .splcproj. " +
                "Nothing was changed — resolve it in the TwinSAFE editor first.");

        // ---- SDSID: never one that an existing .sds already carries ---------------
        int maxExisting = snap.AliasDevices.Select(a => a.Info?.SdsId ?? 0).DefaultIfEmpty(0).Max();
        int sdsId = Math.Max(Math.Max(snap.Structure.NextSdsId ?? 1, maxExisting + 1), 1);
        int? nextWritten = snap.Structure.NextSdsId is null ? null : sdsId + 1;

        // ---- FileFormatVersion: from a sibling standard digital device, else the template ---
        var sibling = snap.AliasDevices
            .Select(a => a.Info)
            .FirstOrDefault(i => i is { Type: SafetySdsAuthor.StandardAliasDeviceType, FileFormatVersion: not null }
                                 && SafetyAliasRules.SupportedSubTypes.Contains(i.SubType));
        string version = sibling?.FileFormatVersion ?? SafetySdsAuthor.DefaultFileFormatVersion;
        string versionSource = sibling is not null
            ? $"copied from '{sibling.Name}.sds'"
            : "Beckhoff 1.3.0.0 template default (no standard alias device in the project to copy it from)";

        string sdsXml = SafetySdsAuthor.AuthorStandardDigital(sdsId, subType, version);
        var (projXml, hasBom, hasCrlf) = SafetySplcprojEditor.ReadFile(splcprojPath);
        var (updated, changed) = SafetySplcprojEditor.AddAliasDevice(projXml, relPath, nextWritten);
        if (!changed)
            throw new InvalidOperationException($"'{relPath}' is already registered in the .splcproj. Nothing was changed.");

        // ---- transaction ------------------------------------------------------------
        string? createdDir = null;
        string sdsDir = Path.GetDirectoryName(fullPath)!;
        if (!System.IO.Directory.Exists(sdsDir))
        {
            createdDir = FirstMissingAncestor(sdsDir);
            System.IO.Directory.CreateDirectory(sdsDir);
        }

        string sdsTemp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        string projTemp = splcprojPath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        bool sdsPlaced = false;
        try
        {
            SafetySplcprojEditor.WriteFile(sdsTemp, sdsXml, withBom: true, withCrlf: true);
            File.Move(sdsTemp, fullPath, overwrite: false);
            sdsPlaced = true;

            BeforeProjectWrite?.Invoke(splcprojPath);
            SafetySplcprojEditor.WriteFile(projTemp, updated, hasBom, hasCrlf);
            File.Replace(projTemp, splcprojPath, destinationBackupFileName: null);
        }
        catch (Exception ex)
        {
            TryDelete(sdsTemp);
            TryDelete(projTemp);
            if (sdsPlaced) TryDelete(fullPath);
            if (createdDir is not null) TryDeleteEmptyTree(createdDir);
            throw new InvalidOperationException(
                $"Could not create alias device '{signalName}': {ex.Message}. Rolled back — the .sds was removed " +
                "and the .splcproj is unchanged.", ex);
        }

        // ---- read back what landed ----------------------------------------------------
        var after = SafetyProjectSnapshot.Load(splcprojPath);
        var landed = after.AliasDevices.FirstOrDefault(a =>
            string.Equals(a.IncludePath, relPath, StringComparison.OrdinalIgnoreCase));
        if (landed?.Info is not { } info || info.SdsId != sdsId || info.SubType != subType)
            throw new InvalidOperationException(
                $"Alias device '{signalName}' was written but the read-back does not match " +
                $"(registered={landed is not null}, SDSID={landed?.Info?.SdsId}, SubType={landed?.Info?.SubType}). " +
                "Check the project in the TwinSAFE editor.");

        return new SafetyAliasAddResult(signalName, relPath, sdsId, subType, version, versionSource,
            nextWritten, Path.GetFileNameWithoutExtension(groupRel));
    }

    private static string ResolveGroup(IReadOnlyList<string> groups, string? group)
    {
        if (groups.Count == 0)
            throw new InvalidOperationException("The safety project has no group (.sal) — create one in the TwinSAFE editor first.");
        if (string.IsNullOrWhiteSpace(group))
        {
            if (groups.Count == 1) return groups[0];
            throw new InvalidOperationException(
                $"The safety project has {groups.Count} groups ({string.Join(", ", groups.Select(Path.GetFileNameWithoutExtension))}); " +
                "pass group to choose one.");
        }
        var hit = groups.Where(g => string.Equals(Path.GetFileNameWithoutExtension(g), group.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return hit.Count == 1
            ? hit[0]
            : throw new InvalidOperationException(
                $"Group '{group}' not found. Available: {string.Join(", ", groups.Select(Path.GetFileNameWithoutExtension))}.");
    }

    // The folder that already holds this group's standard digital alias devices whose names follow the
    // signal convention (most of them, if they are spread); else "<group folder>\Alias Devices" — the
    // layout of the Beckhoff project templates (TwinSafeGroup1\Alias Devices\*.sds).
    private static string PickTargetDirectory(SafetyProjectSnapshot snap, string groupDir)
    {
        string prefix = groupDir.Length == 0 ? string.Empty : groupDir + Path.DirectorySeparatorChar;
        var folder = snap.AliasDevices
            .Where(a => a.Info is { Type: SafetySdsAuthor.StandardAliasDeviceType, SignalName: not null }
                        && SafetyAliasRules.SupportedSubTypes.Contains(a.Info.SubType)
                        && a.IncludePath.Replace('/', '\\').StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => Path.GetDirectoryName(a.IncludePath.Replace('/', '\\')) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
        return folder ?? Path.Combine(groupDir, "Alias Devices");
    }

    private static string FirstMissingAncestor(string dir)
    {
        string missing = dir;
        for (string? p = Path.GetDirectoryName(dir); p is not null && !System.IO.Directory.Exists(p); p = Path.GetDirectoryName(p))
            missing = p;
        return missing;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyTree(string dir)
    {
        try
        {
            if (System.IO.Directory.Exists(dir) && !System.IO.Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)
                    .Any(e => File.Exists(e)))
                System.IO.Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static string Describe(SafetyAliasAddResult r) =>
        new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Alias device '{r.SignalName}' written to disk (group '{r.Group}') — NOT yet verified by the TwinSAFE editor.")
            .AppendLine(CultureInfo.InvariantCulture, $"  .sds: {r.RelativePath}")
            .AppendLine(CultureInfo.InvariantCulture, $"  SDSID: {r.SdsId.ToString(CultureInfo.InvariantCulture)} (no existing .sds uses it)")
            .AppendLine(CultureInfo.InvariantCulture, $"  SubType: {r.SubType.ToString(CultureInfo.InvariantCulture)} = {SafetySdsAuthor.DescribeSubType(r.SubType)}")
            .AppendLine(CultureInfo.InvariantCulture, $"  FileFormatVersion: {r.FileFormatVersion} ({r.FileFormatVersionSource})")
            .AppendLine(r.NextSdsIdWritten is int n
                ? $"  .splcproj: registered; NextSdsId set to {n.ToString(CultureInfo.InvariantCulture)}"
                : "  .splcproj: registered; it has no <NextSdsId> element and none was added")
            .AppendLine("  CRC: 0 — run 'Verify Complete Safety Project' in the TwinSAFE editor before any download.")
            .ToString();
}
