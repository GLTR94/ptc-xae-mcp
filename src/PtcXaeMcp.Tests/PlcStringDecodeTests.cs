using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// The ADS STRING path could not be exercised live (no PLC runtime reachable from
// the test machine), so the part that carries the actual decisions — turning a
// fixed-size, NUL-padded byte slot into a string — is pinned down here instead.
// The surrounding plumbing (read sym.Size raw bytes) is the same call the already
// working STRING-array path uses.
public class PlcStringDecodeTests
{
    private static string Decode(byte[] raw, int offset, int count) =>
        PlcStringCodec.Decode(raw, offset, count);

    [Fact]
    public void TruncatesAtNul_IgnoringPadding()
    {
        // STRING(10) slot holding "Motor" — the rest is padding that must not leak.
        var raw = new byte[11];
        System.Text.Encoding.ASCII.GetBytes("Motor").CopyTo(raw, 0);
        Assert.Equal("Motor", Decode(raw, 0, raw.Length));
    }

    [Fact]
    public void EmptySlot_IsEmptyString()
    {
        Assert.Equal(string.Empty, Decode(new byte[81], 0, 81));
    }

    [Fact]
    public void FullSlot_WithoutNul_UsesWholeSlot()
    {
        var raw = System.Text.Encoding.ASCII.GetBytes("ABCD");
        Assert.Equal("ABCD", Decode(raw, 0, raw.Length));
    }

    [Fact]
    public void DecodesUtf8Payload()
    {
        var raw = new byte[20];
        System.Text.Encoding.UTF8.GetBytes("Störung").CopyTo(raw, 0);
        Assert.Equal("Störung", Decode(raw, 0, raw.Length));
    }

    // The case that matters for real HMI texts: the XAE writes single-byte
    // codepage text, so 'ö' is one byte (0xF6). Strict UTF-8 would fail on it.
    [Fact]
    public void FallsBackToLatin1_ForSingleByteUmlauts()
    {
        var raw = new byte[20];
        System.Text.Encoding.Latin1.GetBytes("Störung").CopyTo(raw, 0);
        Assert.Equal("Störung", Decode(raw, 0, raw.Length));
    }

    [Fact]
    public void ReadsSliceAtOffset_ForArrayElements()
    {
        // Two STRING(4) slots back to back: "AB", "CD".
        var raw = new byte[10];
        System.Text.Encoding.ASCII.GetBytes("AB").CopyTo(raw, 0);
        System.Text.Encoding.ASCII.GetBytes("CD").CopyTo(raw, 5);
        Assert.Equal("AB", Decode(raw, 0, 5));
        Assert.Equal("CD", Decode(raw, 5, 5));
    }
}
