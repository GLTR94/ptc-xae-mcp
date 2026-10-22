using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat.HostSelection;

/// <summary>Which TwinCAT XAE Shell a project's TcVersion maps to.</summary>
public enum XaeHostClass
{
    /// <summary>TcVersion build &lt; 4026 → 32-bit XAE Shell (ProgID 15.0).</summary>
    Shell32,

    /// <summary>TcVersion build &gt;= 4026 → 64-bit XAE Shell (ProgID 17.0).</summary>
    Shell64,
}

/// <summary>
/// The <c>TcVersion</c> declared on a <c>.tsproj</c> root element
/// (e.g. <c>TcVersion="3.1.4026.22"</c>), parsed to a comparable 4-part
/// version. The 32/64-bit XAE Shell split is documented (Infosys 242746251)
/// as build &gt;= 4026 → 64-bit; comparison is on major.minor.build and the
/// revision is deliberately ignored (4026.0 and 4026.22 are both 64-bit).
/// </summary>
public sealed class TcVersion
{
    /// <summary>The documented 32→64-bit boundary, on the build component.</summary>
    public const int Shell64MinBuild = 4026;

    public int Major { get; }
    public int Minor { get; }
    public int Build { get; }
    public int Revision { get; }

    private TcVersion(int major, int minor, int build, int revision)
    {
        Major = major;
        Minor = minor;
        Build = build;
        Revision = revision;
    }

    /// <summary>
    /// Parses a TcVersion string. Accepts 3-part (<c>3.1.4026</c>) or 4-part
    /// (<c>3.1.4026.22</c>) forms; a missing revision defaults to 0. Any other
    /// shape — null/blank, non-numeric, &lt;3 parts, suffixes like
    /// <c>-rc1</c> — fails cleanly (no throw).
    /// </summary>
    public static bool TryParse(string? s, out TcVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        var parts = s.Trim().Split('.');
        if (parts.Length is < 3 or > 4)
            return false;

        var nums = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return false;
            nums[i] = n;
        }

        version = new TcVersion(nums[0], nums[1], nums[2], parts.Length == 4 ? nums[3] : 0);
        return true;
    }

    /// <summary>
    /// Classifies into the XAE Shell host class. Conservative at the
    /// boundary: build &gt;= 4026 → <see cref="XaeHostClass.Shell64"/>,
    /// otherwise <see cref="XaeHostClass.Shell32"/> (so 4025.x → 32-bit).
    /// </summary>
    public XaeHostClass Classify() =>
        Build >= Shell64MinBuild ? XaeHostClass.Shell64 : XaeHostClass.Shell32;

    public override string ToString() => $"{Major}.{Minor}.{Build}.{Revision}";
}
