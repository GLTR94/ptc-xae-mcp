namespace PtcXaeMcp.WinForms.TwinCat;

// PLC type-name normalization for the ADS variable read/write path. Deliberately outside
// the #if TWINCAT region and free of any ADS type (same rationale as PlcStringCodec), so this
// string logic — the part that goes wrong silently — is unit-testable without a running PLC
// or the TwinCAT SDK.
internal static class AdsTypeNames
{
    // Matches "ARRAY[0..9] OF STRING" (already normalized: upper-cased, size qualifier
    // stripped). Only single-dimension arrays.
    private static readonly System.Text.RegularExpressions.Regex ArrayOfStringRegex = new(
        @"^ARRAY\s*\[\s*(-?\d+)\s*\.\.\s*(-?\d+)\s*\]\s*OF\s*STRING$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a symbol's TwinCAT type name for the ADS read/write dispatch: strips the
    /// size qualifier ("STRING(80)" → "STRING"), upper-cases, strips a namespace qualifier
    /// ("TC2_SYSTEM.T_MAXSTRING" → "T_MAXSTRING"), and resolves known Tc2_System string
    /// aliases (T_MAXSTRING / T_STRING / T_AMSNETID) to plain STRING.
    /// </summary>
    /// <remarks>
    /// D-1 (issue #152): stripping the namespace qualifier at the type name's *last* dot broke
    /// on an array of a namespace-qualified string alias — e.g. "ARRAY[0..9] OF
    /// NS.T_MAXSTRING" has its only dot inside the element type, so the old code stripped the
    /// entire "ARRAY[0..9] OF " prefix along with the namespace, and
    /// <see cref="TryGetStringArrayCount"/> then failed to match at all (broken since
    /// 2026-08-08, live-observed on a namespace-qualified STRING array). The namespace strip
    /// now only ever applies to the element type after " OF ", never to the array prefix.
    /// </remarks>
    public static string NormalizeTypeName(string typeName)
    {
        // Strip size qualifiers: "STRING(80)" → "STRING"
        int paren = typeName.IndexOf('(');
        if (paren > 0) typeName = typeName[..paren];
        typeName = typeName.Trim().ToUpperInvariant();

        const string ofMarker = " OF ";
        int ofIdx = typeName.LastIndexOf(ofMarker, StringComparison.Ordinal);
        if (ofIdx >= 0)
        {
            string prefix = typeName[..(ofIdx + ofMarker.Length)];
            string element = ResolveAlias(StripNamespace(typeName[(ofIdx + ofMarker.Length)..]));
            return prefix + element;
        }

        return ResolveAlias(StripNamespace(typeName));
    }

    private static string StripNamespace(string typeName)
    {
        int dot = typeName.LastIndexOf('.');
        return dot >= 0 ? typeName[(dot + 1)..] : typeName;
    }

    private static string ResolveAlias(string typeName) =>
        typeName is "T_MAXSTRING" or "T_STRING" or "T_AMSNETID" ? "STRING" : typeName;

    public static bool TryGetStringArrayCount(string plcType, out int count)
    {
        count = 0;
        var m = ArrayOfStringRegex.Match(plcType);
        if (!m.Success) return false;
        int lo = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        int hi = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        count = hi - lo + 1;
        return count > 0;
    }
}
