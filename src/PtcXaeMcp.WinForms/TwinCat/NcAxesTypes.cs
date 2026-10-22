using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

// One NC-Task discovered under TINC (browse-by-type, ItemType 1).
internal sealed record NcTaskEntry(
    string Path,
    string Name,
    IReadOnlyList<NcAxisEntry> Axes);

// One axis discovered under an NC-Task's axes container (ItemType 22).
internal sealed record NcAxisEntry(
    string Path,
    string Name);

// Current parameters of one axis: the known NcAxisField key→value pairs
// that are present in the axis ProduceXml, plus the raw XML for full
// transparency (mirrors twincat_get_device_xml on the fieldbus side).
internal sealed record NcAxisParams(
    string RawXml,
    IReadOnlyDictionary<string, string> Fields);

// Friendly configure_axis field keys → element path inside the axis
// ProduceXml document (relative to <TreeItem>). Element names captured
// from the live SampleProject fixture (TC3 4026.22). Only these keys
// are accepted; anything else is a hard error so a typo never silently
// no-ops a parameter write.
internal static class NcAxisField
{
    // Ordered so a summary lists fields predictably.
    public static readonly IReadOnlyList<string> Keys =
    [
        "unit",
        "scaleFactorNumerator",
        "scaleFactorDenominator",
        "velocityMax",
        "acceleration",
        "deceleration",
        "jerk",
        "encoderInverse",
        "moduloFactor",
        "softMinEnable",
        "softMinRange",
        "softMaxEnable",
        "softMaxRange",
    ];

    // Each value is the chain of element names from <TreeItem> down to the
    // leaf whose text content is replaced.
    private static readonly IReadOnlyDictionary<string, string[]> Map =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit"]                   = ["NcAxisDef", "AxisParam", "Unit"],
            ["scaleFactorNumerator"]   = ["NcAxisDef", "Encoder", "EncParam", "ScaleFactorNumerator"],
            ["scaleFactorDenominator"] = ["NcAxisDef", "Encoder", "EncParam", "ScaleFactorDenominator"],
            ["velocityMax"]            = ["NcAxisDef", "AxisParam", "Velo", "Maximum"],
            ["acceleration"]           = ["NcAxisDef", "AxisParam", "Dynamic", "Acceleration"],
            ["deceleration"]           = ["NcAxisDef", "AxisParam", "Dynamic", "Deceleration"],
            ["jerk"]                   = ["NcAxisDef", "AxisParam", "Dynamic", "Jerk"],
            ["encoderInverse"]         = ["NcAxisDef", "Encoder", "EncParam", "Inverse"],
            ["moduloFactor"]           = ["NcAxisDef", "Encoder", "EncParam", "ModuloFactor"],
            ["softMinEnable"]          = ["NcAxisDef", "Encoder", "EncParam", "SoftEndMinControl", "Enable"],
            ["softMinRange"]           = ["NcAxisDef", "Encoder", "EncParam", "SoftEndMinControl", "Range"],
            ["softMaxEnable"]          = ["NcAxisDef", "Encoder", "EncParam", "SoftEndMaxControl", "Enable"],
            ["softMaxRange"]           = ["NcAxisDef", "Encoder", "EncParam", "SoftEndMaxControl", "Range"],
        };

    public static bool TryResolve(string key, out string[] path)
        => Map.TryGetValue(key, out path!);

    // axisType is a *composite* field (not a single leaf): setting it changes the
    // <NcAxisDef/AxisIoType> element together with the coupled <Encoder/EncType>
    // and <Drive/DrvType>. Values captured live (100000, TC3 4026.22) by diffing
    // an axis before/after the System-Manager "Achstyp" dropdown. The default (freshly created) axis has NO
    // AxisIoType element and EncType 1 / DrvType 5; ApplyAxisType creates it.
    public const string AxisTypeKey = "axisType";

    public static readonly IReadOnlyDictionary<string, (string Io, string Enc, string Drv)> AxisTypeMap =
        new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["canopen_ds402"] = ("CANopen-DS402", "19", "16"),
            ["coe"]           = ("CANopen-DS402", "19", "16"),
            ["sercos_soe"]    = ("Sercos", "8", "9"),
            ["soe"]           = ("Sercos", "8", "9"),
        };

    // True for every key ApplyFields accepts (simple leaf fields + the composite axisType).
    public static bool IsKnownKey(string key) =>
        key.Equals(AxisTypeKey, StringComparison.OrdinalIgnoreCase) || Map.ContainsKey(key);
}

// Pure read-modify-write over the axis ProduceXml string. Kept free of COM
// so it is fully unit-testable against a captured real ProduceXml dump:
// parse → set targeted leaf values → return the whole document for
// ConsumeXml. Unspecified elements are preserved verbatim.
internal static class NcAxisXml
{
    public static (string Xml, IReadOnlyList<string> Applied) ApplyFields(
        string axisXml, IReadOnlyDictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(axisXml))
            throw new InvalidOperationException("Axis ProduceXml was empty.");
        if (fields.Count == 0)
            throw new InvalidOperationException("No axis fields supplied.");

        XDocument doc;
        try { doc = XDocument.Parse(axisXml, LoadOptions.PreserveWhitespace); }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Axis ProduceXml is not valid XML: {ex.Message}", ex);
        }

        var root = doc.Root
            ?? throw new InvalidOperationException("Axis ProduceXml has no root element.");

        var applied = new List<string>();

        // Composite axisType first (creates/sets AxisIoType + EncType + DrvType).
        if (fields.TryGetValue(NcAxisField.AxisTypeKey, out var axisTypeVal))
            ApplyAxisType(root, axisTypeVal, applied);

        // Iterate in the canonical key order so the result is deterministic.
        foreach (var key in NcAxisField.Keys)
        {
            if (!fields.TryGetValue(key, out var value)) continue;
            if (!NcAxisField.TryResolve(key, out var chain))
                throw new InvalidOperationException($"Unknown axis field '{key}'.");

            XElement cursor = root;
            foreach (var step in chain)
            {
                cursor = cursor.Element(step)
                    ?? throw new InvalidOperationException(
                        $"Axis ProduceXml has no <{string.Join("/", chain)}> element " +
                        $"(missing '{step}') — cannot set '{key}'. The axis may not be " +
                        "fully parameterized yet.");
            }

            cursor.Value = value;
            applied.Add($"{key}={value}");
        }

        // Also reject any supplied key that is not a known field, so a
        // caller typo surfaces instead of being silently dropped.
        foreach (var key in fields.Keys)
            if (!NcAxisField.IsKnownKey(key))
                throw new InvalidOperationException(
                    $"Unknown axis field '{key}'. Valid: {string.Join(", ", NcAxisField.Keys)}, " +
                    $"{NcAxisField.AxisTypeKey}.");

        return (doc.ToString(SaveOptions.DisableFormatting), applied);
    }

    // Set the composite "Achstyp": the <NcAxisDef/AxisIoType> element plus the
    // coupled <Encoder/EncType> and <Drive/DrvType>. AxisIoType is created (right
    // after <AxisType>) when absent — a freshly created/default axis has none.
    // TwinCAT regenerates the dependent encoder/drive process image on ConsumeXml.
    private static void ApplyAxisType(XElement root, string value, List<string> applied)
    {
        var key = (value ?? string.Empty).Trim();
        if (!NcAxisField.AxisTypeMap.TryGetValue(key, out var t))
            throw new InvalidOperationException(
                $"Unknown axisType '{value}'. Valid: {string.Join(", ", NcAxisField.AxisTypeMap.Keys)}.");

        var ncDef = root.Element("NcAxisDef")
            ?? throw new InvalidOperationException(
                "Axis ProduceXml has no <NcAxisDef> element — cannot set axisType.");

        var ioEl = ncDef.Element("AxisIoType");
        if (ioEl is null)
        {
            ioEl = new XElement("AxisIoType");
            var axisTypeEl = ncDef.Element("AxisType");
            if (axisTypeEl is not null) axisTypeEl.AddAfterSelf(ioEl);
            else ncDef.AddFirst(ioEl);
        }
        ioEl.Value = t.Io;

        SetTypeChild(ncDef, "Encoder", "EncType", t.Enc);
        SetTypeChild(ncDef, "Drive", "DrvType", t.Drv);

        applied.Add($"axisType={key} (AxisIoType={t.Io}, EncType={t.Enc}, DrvType={t.Drv})");
    }

    // Set <NcAxisDef/{parent}/{child}> = val, creating the leaf as the parent's
    // first child if missing. The parent (Encoder/Drive) must already exist.
    private static void SetTypeChild(XElement ncDef, string parentName, string childName, string val)
    {
        var parent = ncDef.Element(parentName)
            ?? throw new InvalidOperationException(
                $"Axis ProduceXml has no <NcAxisDef/{parentName}> element — cannot set axisType.");
        var child = parent.Element(childName);
        if (child is null) { child = new XElement(childName); parent.AddFirst(child); }
        child.Value = val;
    }

    /// <summary>
    /// D-9 (issue #152): compares the requested fields with a fresh ProduceXml read-back
    /// (<see cref="ReadFields"/>). Numbers compare by value ("1000" == "1000.0"), booleans by
    /// meaning ("true" == "1"), axisType by its AxisIoType ("coe" == "CANopen-DS402"), anything else
    /// as trimmed, case-insensitive text. Returns one "key: wanted X, read back Y" per mismatch.
    /// </summary>
    public static IReadOnlyList<string> VerifyApplied(
        IReadOnlyDictionary<string, string> requested, IReadOnlyDictionary<string, string> readBack)
    {
        var mismatches = new List<string>();
        foreach (var (key, wantRaw) in requested)
        {
            string want = (wantRaw ?? string.Empty).Trim();
            readBack.TryGetValue(key, out string? gotRaw);
            string? got = gotRaw?.Trim();

            bool same = got is not null && (key.Equals(NcAxisField.AxisTypeKey, StringComparison.OrdinalIgnoreCase)
                ? NcAxisField.AxisTypeMap.TryGetValue(want, out var t) && string.Equals(t.Io, got, StringComparison.OrdinalIgnoreCase)
                : SameValue(want, got));
            if (!same)
                mismatches.Add($"{key}: wanted '{want}', read back {(got is null ? "<absent>" : $"'{got}'")}");
        }
        return mismatches;
    }

    private static bool SameValue(string want, string got)
    {
        if (string.Equals(want, got, StringComparison.OrdinalIgnoreCase)) return true;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var num = System.Globalization.NumberStyles.Float;
        if (double.TryParse(want, num, inv, out double w) && double.TryParse(got, num, inv, out double g))
            return w == g || Math.Abs(w - g) <= 1e-9 * Math.Max(Math.Abs(w), Math.Abs(g));
        if (TryBool(want, out bool wb) && TryBool(got, out bool gb)) return wb == gb;
        return false;
    }

    private static bool TryBool(string s, out bool value)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "true" or "1": value = true; return true;
            case "false" or "0": value = false; return true;
            default: value = false; return false;
        }
    }

    // Pure read: extract the current value of every known NcAxisField key
    // that is present in the axis ProduceXml. Keys whose element is absent
    // (axis not fully parameterized) are simply omitted — reading never
    // throws on a partial axis. Returned in canonical key order.
    public static IReadOnlyDictionary<string, string> ReadFields(string axisXml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(axisXml)) return result;

        XDocument doc;
        try { doc = XDocument.Parse(axisXml); }
        catch { return result; }

        var root = doc.Root;
        if (root is null) return result;

        foreach (var key in NcAxisField.Keys)
        {
            if (!NcAxisField.TryResolve(key, out var chain)) continue;
            XElement? cursor = root;
            foreach (var step in chain)
            {
                cursor = cursor.Element(step);
                if (cursor is null) break;
            }
            if (cursor is not null) result[key] = cursor.Value;
        }

        // axisType: explicit AxisIoType when present, else flagged as unset with
        // the raw EncType/DrvType pair (a default axis has no AxisIoType element).
        var ncDef = root.Element("NcAxisDef");
        if (ncDef is not null)
        {
            var io = ncDef.Element("AxisIoType")?.Value;
            if (!string.IsNullOrEmpty(io))
                result[NcAxisField.AxisTypeKey] = io;
            else
            {
                var enc = ncDef.Element("Encoder")?.Element("EncType")?.Value;
                var drv = ncDef.Element("Drive")?.Element("DrvType")?.Value;
                if (enc is not null || drv is not null)
                    result[NcAxisField.AxisTypeKey] = $"(none; EncType={enc ?? "?"}, DrvType={drv ?? "?"})";
            }
        }
        return result;
    }
}
