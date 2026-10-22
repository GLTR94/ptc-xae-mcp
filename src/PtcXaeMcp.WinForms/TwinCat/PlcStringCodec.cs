namespace PtcXaeMcp.WinForms.TwinCat;

// Turning a PLC STRING slot into a .NET string. Deliberately outside the
// #if TWINCAT region and free of any ADS type, so the decision it encodes can be
// tested without a runtime: this is the part that goes wrong silently (wrong
// encoding → mojibake, missing NUL handling → trailing padding), while the ADS
// call around it either works or throws.
internal static class PlcStringCodec
{
    /// <summary>
    /// Decodes one fixed-size STRING slot: cuts at the first NUL, then decodes.
    /// </summary>
    /// <remarks>
    /// A TwinCAT STRING is a byte string, not a declared encoding — the PLC holds
    /// whatever bytes were written. Text authored in the XAE arrives in the
    /// project's single-byte codepage, so a German umlaut is ONE byte (0xF6 for
    /// 'ö') and strict UTF-8 decoding of it fails. Try UTF-8 first (correct when
    /// something wrote UTF-8, e.g. this tool before issue #152 D-4) and fall back
    /// to Windows-1252 — TwinCAT's default codepage and what <see cref="Encode"/> writes.
    /// </remarks>
    public static string Decode(byte[] raw, int offset, int count)
    {
        if (raw is null || count <= 0 || offset >= raw.Length) return string.Empty;
        count = Math.Min(count, raw.Length - offset);

        int nul = Array.IndexOf(raw, (byte)0, offset, count);
        int len = Math.Max(0, nul >= 0 ? nul - offset : count);
        if (len == 0) return string.Empty;

        try
        {
            return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetString(raw, offset, len);
        }
        catch (ArgumentException)
        {
            return Cp1252Lenient.GetString(raw, offset, len);
        }
    }

    // TwinCAT's default single-byte codepage. Built into .NET (System.Text.Encoding.CodePages);
    // the provider hands out the encoding without a global RegisterProvider call.
    private static readonly System.Text.Encoding Cp1252Strict =
        System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1252,
            System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback)!;

    private static readonly System.Text.Encoding Cp1252Lenient =
        System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1252,
            System.Text.EncoderFallback.ReplacementFallback, System.Text.DecoderFallback.ReplacementFallback)!;

    /// <summary>
    /// Encodes <paramref name="value"/> into one fixed-size STRING slot of <paramref name="slotSize"/>
    /// bytes (declared length + NUL), zero-padded. Issue #152 D-4: a TwinCAT STRING is single-byte
    /// (Windows-1252 by default) — UTF-8 put two bytes per umlaut into the PLC ("Ã¶" in the HMI).
    /// Refuses, instead of silently mangling, a character 1252 cannot represent, an embedded NUL
    /// (it would cut the string) and a value longer than the slot.
    /// </summary>
    public static byte[] Encode(string? value, int slotSize)
    {
        value ??= string.Empty;
        if (slotSize < 1)
            throw new InvalidOperationException($"STRING slot size {slotSize} is invalid.");
        int nul = value.IndexOf('\0', StringComparison.Ordinal);
        if (nul >= 0)
            throw new InvalidOperationException(
                $"The value contains a NUL character at position {nul}; a PLC STRING ends at the first NUL, so it would be cut.");

        byte[] bytes;
        try { bytes = Cp1252Strict.GetBytes(value); }
        catch (System.Text.EncoderFallbackException ex)
        {
            string ch = ex.CharUnknownHigh != '\0'
                ? char.ConvertFromUtf32(char.ConvertToUtf32(ex.CharUnknownHigh, ex.CharUnknownLow))
                : ex.CharUnknown.ToString();
            int code = char.ConvertToUtf32(ch, 0);
            throw new InvalidOperationException(
                $"The value contains '{ch}' (U+{code:X4}) at position {ex.Index}, which a TwinCAT STRING " +
                "(Windows-1252, single-byte) cannot hold. Nothing was written. Use WSTRING for Unicode text, " +
                "or replace the character.", ex);
        }

        if (bytes.Length > slotSize - 1)
            throw new InvalidOperationException(
                $"The value is {bytes.Length} bytes; this STRING holds {slotSize - 1} plus a NUL. Nothing was written.");
        var slot = new byte[slotSize];
        Array.Copy(bytes, slot, bytes.Length);
        return slot;
    }

    /// <summary>
    /// D-3 read-back for a STRING slot: the bytes read back must equal the written payload up to and
    /// including its terminating NUL (what follows the NUL is irrelevant to the PLC). Null when equal,
    /// otherwise a "wrote …, read back …" description.
    /// </summary>
    public static string? SlotMismatch(byte[] written, byte[] readBack, int offset, int count)
    {
        int end = Array.IndexOf(written, (byte)0, offset, count);
        int significant = (end >= 0 ? end + 1 : offset + count) - offset;
        bool same = readBack.Length >= offset + significant &&
                    written.AsSpan(offset, significant).SequenceEqual(readBack.AsSpan(offset, significant));
        return same
            ? null
            : $"wrote '{Decode(written, offset, count)}', read back '{Decode(readBack, offset, count)}'";
    }
}
