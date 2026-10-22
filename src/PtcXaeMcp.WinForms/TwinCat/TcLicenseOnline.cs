using System.Buffers.Binary;
using System.Text;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Issue #55: the XAE's License editor reads the target's licenses over ADS from the license server (AMS port 30),
/// not from TIRC^License (that node only knows the license-REQUEST form). Wire format as Beckhoff's own
/// TwinCAT.SystemService GetLicensesCommand reads it: index group 0x01010006 (valid) / 0x0101000A (invalid), offset 0 —
/// first 4 bytes = count N, then N × 48-byte _TcLicenseOnlineInfo; names via 0x0101000B — 4 bytes count, then
/// N × 96-byte _TcLicenseName {GUID; char[16] orderNo; char[64] name}. Pure parsing here; ADS in the session.
/// </summary>
internal static class TcLicenseOnline
{
    public const int AdsPort = 30;
    public const uint ValidLicensesGroup = 0x01010006;
    public const uint InvalidLicensesGroup = 0x0101000A;
    public const uint LicenseNamesGroup = 0x0101000B;
    public const uint LicenseNameGroup = 0x0101000C;     // ReadWrite: write GUID, read 256 bytes ANSI name
    public const uint LicenseOrderNoGroup = 0x0101000D;  // ReadWrite: write GUID, read 17 bytes ANSI order no.
    public const int EntrySize = 48;
    public const int NameSize = 96;

    public const int CodeValid = 0, CodeDemo = 596, CodeValidOem = 597, CodeNotFound = 1828, CodeExpired = 1829;
    private const uint SeverityBit = 0x80000000;

    internal readonly record struct Raw(Guid Id, long ExpireFileTime, uint Count, uint Used, uint Result);

    public static int ReadCount(ReadOnlySpan<byte> four) => four.Length < 4 ? 0 : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(four));

    public static List<Raw> ParseEntries(ReadOnlySpan<byte> data, int count)
    {
        var list = new List<Raw>(count);
        for (int i = 0; i < count && (i + 1) * EntrySize <= data.Length; i++)
        {
            var e = data.Slice(i * EntrySize, EntrySize);
            list.Add(new Raw(new Guid(e[..16]), BinaryPrimitives.ReadInt64LittleEndian(e[16..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[24..]), BinaryPrimitives.ReadUInt32LittleEndian(e[28..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[32..])));
        }
        return list;
    }

    public static Dictionary<Guid, (string OrderNo, string Name)> ParseNames(ReadOnlySpan<byte> data, int count)
    {
        var map = new Dictionary<Guid, (string, string)>();
        for (int i = 0; i < count && (i + 1) * NameSize <= data.Length; i++)
        {
            var e = data.Slice(i * NameSize, NameSize);
            map[new Guid(e[..16])] = (Ansi(e.Slice(16, 16)), Ansi(e.Slice(32, 64)));
        }
        return map;
    }

    internal static string AnsiZ(ReadOnlySpan<byte> s) => Ansi(s);

    private static string Ansi(ReadOnlySpan<byte> s)
    {
        int end = s.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? s : s[..end]).Trim();
    }

    public static LicenseStatus StatusOf(uint result) => (int)(result & 0xFFFF) switch
    {
        CodeValid or CodeValidOem when (result & SeverityBit) == 0 => LicenseStatus.Present,
        CodeDemo => LicenseStatus.Trial,
        CodeExpired => LicenseStatus.Expired,
        CodeNotFound => LicenseStatus.Missing,
        _ => LicenseStatus.Unknown,
    };

    public static LicenseEntry ToEntry(Raw raw, IReadOnlyDictionary<Guid, (string OrderNo, string Name)> names)
    {
        names.TryGetValue(raw.Id, out var n);
        DateTime? expiry = raw.ExpireFileTime <= 0 ? null : DateTime.FromFileTimeUtc(raw.ExpireFileTime);
        return new LicenseEntry(raw.Id.ToString("B").ToUpperInvariant(), string.IsNullOrEmpty(n.Name) ? "" : n.Name,
            StatusOf(raw.Result), expiry,
            OrderNo: string.IsNullOrEmpty(n.OrderNo) ? null : n.OrderNo,
            Count: (int)raw.Count, Used: (int)raw.Used, ResultCode: (int)(raw.Result & 0xFFFF));
    }
}
