#if TWINCAT
using TwinCAT.Ads;
#endif

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>ADS side of <see cref="TcLicenseOnline"/>: reads the license server (AMS port 30) of a target.</summary>
internal static class TcLicenseServer
{
#if TWINCAT
    public static LicenseInfoResult Read(string netId)
    {
        using var client = new AdsClient { Timeout = 3000 };
        client.Connect(netId, TcLicenseOnline.AdsPort);

        byte[] valid = ReadArray(client, TcLicenseOnline.ValidLicensesGroup, TcLicenseOnline.EntrySize, out int validCount, required: true);
        byte[] invalid = ReadArray(client, TcLicenseOnline.InvalidLicensesGroup, TcLicenseOnline.EntrySize, out int invalidCount, required: false);
        byte[] names = ReadArray(client, TcLicenseOnline.LicenseNamesGroup, TcLicenseOnline.NameSize, out int nameCount, required: false);

        var nameMap = TcLicenseOnline.ParseNames(names, nameCount);
        var raws = TcLicenseOnline.ParseEntries(valid, validCount)
            .Concat(TcLicenseOnline.ParseEntries(invalid, invalidCount))
            .ToList();
        // The bulk name table is empty on some hosts (4026.22 here); ask per id like the XAE's license page does.
        foreach (var r in raws.Where(r => !nameMap.ContainsKey(r.Id)))
        {
            string? name = ReadString(client, TcLicenseOnline.LicenseNameGroup, r.Id, 256);
            string? order = ReadString(client, TcLicenseOnline.LicenseOrderNoGroup, r.Id, 17);
            if (name is not null || order is not null) nameMap[r.Id] = (order ?? "", name ?? "");
        }
        var entries = raws.Select(r => TcLicenseOnline.ToEntry(r, nameMap)).ToList();
        return new LicenseInfoResult(entries,
            $"ADS license server {netId}:{TcLicenseOnline.AdsPort} ({validCount} valid, {invalidCount} invalid entr{(validCount + invalidCount == 1 ? "y" : "ies")})");
    }

    private static string? ReadString(AdsClient client, uint group, Guid id, int size)
    {
        byte[] read = new byte[size];
        AdsErrorCode error = client.TryReadWrite(group, 0, read.AsMemory(), id.ToByteArray().AsMemory(), out int got);
        if (error != AdsErrorCode.NoError || got <= 0) return null;
        string text = TcLicenseOnline.AnsiZ(read.AsSpan(0, got));
        return text.Length == 0 ? null : text;
    }

    // Count first (4 bytes at offset 0), then the array (count × itemSize at offset 0) — as Beckhoff's own
    // GetLicensesCommand reads it.
    private static byte[] ReadArray(AdsClient client, uint group, int itemSize, out int count, bool required)
    {
        count = 0;
        byte[] head = new byte[4];
        AdsErrorCode error = client.TryRead(group, 0, head.AsMemory(), out int read);
        if (error != AdsErrorCode.NoError || read < 4)
        {
            if (required)
                throw new InvalidOperationException(
                    $"The license server of {client.Address} does not answer (index group 0x{group:X8}): " +
                    $"{(error != AdsErrorCode.NoError ? $"ADS error {error}" : $"{read} bytes")}. Is the target's TwinCAT system up and reachable over ADS?");
            return [];
        }
        count = TcLicenseOnline.ReadCount(head);
        if (count == 0) return [];
        byte[] data = new byte[count * itemSize];
        error = client.TryRead(group, 0, data.AsMemory(), out read);
        if (error != AdsErrorCode.NoError)
        {
            if (required) throw new InvalidOperationException($"Reading {count} license entries from {client.Address} failed: ADS error {error}.");
            count = 0;
            return [];
        }
        count = Math.Min(count, read / itemSize);
        return data;
    }
#else
    public static LicenseInfoResult Read(string netId) => throw TwinCatNotBuilt.Exception();
#endif
}
