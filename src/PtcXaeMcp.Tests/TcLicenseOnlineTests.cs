using System.Buffers.Binary;
using System.Text;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #55: parsing the license server's _TcLicenseOnlineInfo / _TcLicenseName arrays.</summary>
public class TcLicenseOnlineTests
{
    private static readonly Guid Plc = new("3ff18e8d-c6ab-4f1d-9d39-bb5f6e71a6b6");
    private static readonly Guid Hmi = new("1d1a2f9c-0000-4000-8000-000000000001");

    private static byte[] Entry(Guid id, long expire, uint count, uint used, uint result)
    {
        var b = new byte[TcLicenseOnline.EntrySize];
        id.TryWriteBytes(b);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(16), expire);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), count);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), used);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), result);
        return b;
    }

    private static byte[] Name(Guid id, string order, string name)
    {
        var b = new byte[TcLicenseOnline.NameSize];
        id.TryWriteBytes(b);
        Encoding.Latin1.GetBytes(order).CopyTo(b, 16);
        Encoding.Latin1.GetBytes(name).CopyTo(b, 32);
        return b;
    }

    [Fact]
    public void Entries_AndNames_MapToLicenseEntries()
    {
        long expire = new DateTime(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();
        byte[] data = [.. Entry(Plc, 0, 1, 1, 0), .. Entry(Hmi, expire, 10, 2, 596)];
        byte[] names = [.. Name(Plc, "TC1200", "TC3 PLC"), .. Name(Hmi, "TF2010", "TC3 HMI Clients Pack 10")];

        var map = TcLicenseOnline.ParseNames(names, 2);
        var entries = TcLicenseOnline.ParseEntries(data, 2).Select(r => TcLicenseOnline.ToEntry(r, map)).ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(LicenseStatus.Present, entries[0].Status);
        Assert.Equal("TC1200", entries[0].OrderNo);
        Assert.Null(entries[0].ExpiryDate);
        Assert.Equal(LicenseStatus.Trial, entries[1].Status);
        Assert.Equal("TC3 HMI Clients Pack 10", entries[1].Name);
        Assert.Equal(new DateTime(2026, 10, 17), entries[1].ExpiryDate!.Value.Date);
        Assert.Equal((10, 2), (entries[1].Count, entries[1].Used));
        Assert.Equal(Plc.ToString("B").ToUpperInvariant(), entries[0].Id);
    }

    [Theory]
    [InlineData(0u, "Present")]
    [InlineData(597u, "Present")]
    [InlineData(596u, "Trial")]
    [InlineData(1829u, "Expired")]
    [InlineData(1828u, "Missing")]
    [InlineData(0x80000000u, "Unknown")]
    [InlineData(1831u, "Unknown")]
    public void Status_FollowsTheLicenseLevel(uint result, string expected) =>
        Assert.Equal(Enum.Parse<LicenseStatus>(expected), TcLicenseOnline.StatusOf(result));

    [Fact]
    public void ShortBuffers_AreTolerated()
    {
        Assert.Equal(0, TcLicenseOnline.ReadCount([1, 0]));
        Assert.Equal(3, TcLicenseOnline.ReadCount([3, 0, 0, 0]));
        Assert.Single(TcLicenseOnline.ParseEntries(Entry(Plc, 0, 1, 0, 0), 5));
        Assert.Empty(TcLicenseOnline.ParseNames(new byte[10], 1));
    }

    [Fact]
    public void Format_ShowsOrderUsageAndSource()
    {
        var entry = new LicenseEntry("{X}", "TC3 HMI Clients Pack 10", LicenseStatus.Trial, null,
            OrderNo: "TF2010", Count: 10, Used: 2, ResultCode: 596);
        string text = BuildCoordinator.FormatLicenseInfo(new LicenseInfoResult([entry], "ADS license server 1.2.3.4.1.1:30"));

        Assert.Contains("[Trial] TF2010 TC3 HMI Clients Pack 10", text);
        Assert.Contains("used 2/10", text);
        Assert.Contains("Source: ADS license server", text);
    }
}
