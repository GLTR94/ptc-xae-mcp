using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #152 D-2/D-3/D-4 (no ADS runtime on the test machine, so the decisions are tested here):
/// batch items fail individually, primitive/STRING writes are confirmed by a read-back, and STRING
/// values are encoded as Windows-1252 with unrepresentable characters refused.
/// </summary>
public class ReadBackAdsTests
{
    // ---- D-2 batch --------------------------------------------------------------------------

    [Fact]
    public void Batch_HandleFailureOnOneSymbol_DoesNotEndTheBatch()
    {
        var items = AdsReadBack.CollectBatch(["MAIN.a", "MAIN.missing", "", "MAIN.b"], path =>
            path == "MAIN.missing"
                ? throw new InvalidOperationException("CreateVariableHandle: symbol not found")
                : new AdsBatchReadItem(path, "INT", 1, true, "OK"));

        items.Should().HaveCount(4);
        items.Select(i => i.Success).Should().Equal(true, false, false, true);
        items[1].Message.Should().StartWith("[Error] read failed").And.Contain("symbol not found");
        items[2].Message.Should().Contain("empty symbol path");
    }

    [Fact]
    public void Batch_Cancellation_Propagates()
    {
        var act = () => AdsReadBack.CollectBatch(["MAIN.a"], _ => throw new OperationCanceledException());
        act.Should().Throw<OperationCanceledException>();
    }

    // ---- D-3 primitive read-back ------------------------------------------------------------

    [Fact]
    public void Primitive_SameValue_IsConfirmed()
    {
        AdsReadBack.PrimitiveMismatch((short)42, (short)42).Should().BeNull();
        AdsReadBack.PrimitiveMismatch(1.5f, 1.5f).Should().BeNull();
        AdsReadBack.PrimitiveMismatch(float.NaN, float.NaN).Should().BeNull();
        AdsReadBack.PrimitiveMismatch(true, true).Should().BeNull();
    }

    [Fact]
    public void Primitive_ValueDidNotLand_IsMismatch()
    {
        AdsReadBack.PrimitiveMismatch((short)42, (short)0).Should().Be("wrote 42, read back 0");
        AdsReadBack.PrimitiveMismatch(2.5, null).Should().Contain("<nothing>");
    }

    // ---- D-4 STRING encoding ----------------------------------------------------------------

    [Fact]
    public void Encode_Umlaut_IsOneByteWindows1252()
    {
        byte[] slot = PlcStringCodec.Encode("Störung", 81);

        slot.Should().HaveCount(81);
        slot[2].Should().Be(0xF6);            // 'ö' single byte, not UTF-8 C3 B6
        slot[7].Should().Be(0);               // NUL right after the 7 characters
        PlcStringCodec.Decode(slot, 0, slot.Length).Should().Be("Störung");
    }

    [Fact]
    public void Encode_EuroSign_IsWindows1252Not_Latin1()
    {
        PlcStringCodec.Encode("€", 2)[0].Should().Be(0x80);
    }

    [Theory]
    [InlineData("Temperatur 5 °C ✓", "U+2713")]
    [InlineData("Ω", "U+03A9")]
    [InlineData("a😀", "U+1F600")]
    public void Encode_Unrepresentable_IsRefusedNamingTheCharacter(string value, string codePoint)
    {
        var act = () => PlcStringCodec.Encode(value, 81);
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{codePoint}*Nothing was written*");
    }

    [Fact]
    public void Encode_TooLong_IsRefused()
    {
        var act = () => PlcStringCodec.Encode("ABCDE", 5); // STRING(4)
        act.Should().Throw<InvalidOperationException>().WithMessage("*5 bytes*holds 4*");
    }

    [Fact]
    public void Encode_EmbeddedNul_IsRefused()
    {
        var act = () => PlcStringCodec.Encode("A\0B", 10);
        act.Should().Throw<InvalidOperationException>().WithMessage("*NUL*");
    }

    [Fact]
    public void SlotMismatch_ComparesUpToTheNul()
    {
        byte[] written = PlcStringCodec.Encode("AB", 6);
        byte[] sameWithGarbageAfterNul = [(byte)'A', (byte)'B', 0, 9, 9, 9];
        byte[] other = PlcStringCodec.Encode("AC", 6);

        PlcStringCodec.SlotMismatch(written, sameWithGarbageAfterNul, 0, 6).Should().BeNull();
        PlcStringCodec.SlotMismatch(written, other, 0, 6).Should().Be("wrote 'AB', read back 'AC'");
    }
}
