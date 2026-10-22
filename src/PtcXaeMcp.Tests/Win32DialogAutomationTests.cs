using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

public class Win32DialogAutomationTests
{
    // ── ExtractSecurityCode ────────────────────────────────────────────────

    [Fact]
    public void ExtractSecurityCode_ValidCode_Found()
    {
        var texts = new[] { "Please enter the code:", "AB3Z9", "Cancel" };
        Assert.Equal("AB3Z9", Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_CodeAmongNoise_Found()
    {
        var texts = new[]
        {
            "Sicherheitscode eingeben",
            "Um eine Testlizenz zu aktivieren, geben Sie bitte den folgenden Code ein:",
            "X7K2M",
            "OK",
            "Abbrechen",
        };
        Assert.Equal("X7K2M", Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_AllDigits_Found()
    {
        var texts = new[] { "12345" };
        Assert.Equal("12345", Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_EmptyList_ReturnsNull()
    {
        Assert.Null(Win32DialogAutomation.ExtractSecurityCode(Array.Empty<string>()));
    }

    [Fact]
    public void ExtractSecurityCode_NoFiveCharMatch_ReturnsNull()
    {
        var texts = new[] { "OK", "Cancel", "Enter the code", "ABCDEF" /* 6 chars */ };
        Assert.Null(Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_MixedCaseMatched()
    {
        // TC3 4026.22 shows mixed-case codes like "bW5Rm" — must match
        var texts = new[] { "bW5Rm" };
        Assert.Equal("bW5Rm", Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_AllLowercaseMatched()
    {
        // All-lowercase 5-char alphanumeric is also valid
        var texts = new[] { "abcde" };
        Assert.Equal("abcde", Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_WhitespaceAroundCode_Trimmed()
    {
        var texts = new[] { "  Q5R1T  " };
        Assert.Equal("Q5R1T", Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_FourCharCode_NotMatched()
    {
        var texts = new[] { "AB3Z" }; // 4 chars
        Assert.Null(Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_FirstMatchReturned()
    {
        // Two valid codes — first one wins
        var texts = new[] { "AAAAA", "BBBBB" };
        Assert.Equal("AAAAA", Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    [Fact]
    public void ExtractSecurityCode_WithSpecialChars_NotMatched()
    {
        var texts = new[] { "A-B3Z", "A B3Z" }; // hyphen / space in code
        Assert.Null(Win32DialogAutomation.ExtractSecurityCode(texts));
    }

    // ── IsTrialLicenseText ─────────────────────────────────────────────────

    [Theory]
    [InlineData("trial license")]
    [InlineData("TRIAL LICENSE")]
    [InlineData("Generate trial licenses")]
    [InlineData("Testlizenz")]
    [InlineData("Testlizenzen erzeugen?")]  // German plural form
    [InlineData("Some required runtime licenses missing — Generate trial licenses?")]
    public void IsTrialLicenseText_KnownPhrases_ReturnsTrue(string text) =>
        Assert.True(Win32DialogAutomation.IsTrialLicenseText(text));

    [Theory]
    [InlineData("")]
    [InlineData("OK")]
    [InlineData("Neustart TwinCAT System")]
    [InlineData("Sicherheitscode eingeben")]
    public void IsTrialLicenseText_UnrelatedPhrases_ReturnsFalse(string text) =>
        Assert.False(Win32DialogAutomation.IsTrialLicenseText(text));

    // ── IsLicenseCreatedText ───────────────────────────────────────────────

    [Theory]
    [InlineData("7 Tages Lizenzdatei erzeugt")]
    [InlineData("Lizenzdatei")]
    [InlineData("License file created")]
    [InlineData("'C:\\repos\\Foo\\TrialLicense.tclrs'")]
    public void IsLicenseCreatedText_KnownPhrases_ReturnsTrue(string text) =>
        Assert.True(Win32DialogAutomation.IsLicenseCreatedText(text));

    [Theory]
    [InlineData("")]
    [InlineData("OK")]
    [InlineData("Sicherheitscode eingeben")]
    [InlineData("Testlizenz erzeugen?")]
    public void IsLicenseCreatedText_UnrelatedPhrases_ReturnsFalse(string text) =>
        Assert.False(Win32DialogAutomation.IsLicenseCreatedText(text));

    // ── ExtractLicenseFilePath ─────────────────────────────────────────────

    [Fact]
    public void ExtractLicenseFilePath_QuotedPath_Unquoted()
    {
        var texts = new[]
        {
            "7 Tages Lizenzdatei erzeugt",
            "'C:\\repos\\SampleProject\\TrialLicense.tclrs'",
            "OK",
        };
        Assert.Equal(
            "C:\\repos\\SampleProject\\TrialLicense.tclrs",
            Win32DialogAutomation.ExtractLicenseFilePath(texts));
    }

    [Fact]
    public void ExtractLicenseFilePath_NoPath_ReturnsNull() =>
        Assert.Null(Win32DialogAutomation.ExtractLicenseFilePath(new[] { "OK", "Abbrechen" }));

    // ── IsTrialLicenseButtonText ───────────────────────────────────────────

    [Theory]
    [InlineData("Aktiviere 7 Tage Testlizenz")]
    [InlineData("7 Tage Testlizenz")]
    [InlineData("7 Days Trial License...")]
    [InlineData("7 Days Trial License")]
    [InlineData("Trial License")]
    [InlineData("Testlizenz aktivieren")]
    public void IsTrialLicenseButtonText_KnownLabels_ReturnsTrue(string text) =>
        Assert.True(Win32DialogAutomation.IsTrialLicenseButtonText(text));

    [Theory]
    [InlineData("")]
    [InlineData("OK")]
    [InlineData("Ja")]
    [InlineData("Lizenzinfo")]
    [InlineData("Testlizenz erzeugen?")]    // question text, not button label
    [InlineData("Sicherheitscode eingeben")]
    public void IsTrialLicenseButtonText_UnrelatedText_ReturnsFalse(string text) =>
        Assert.False(Win32DialogAutomation.IsTrialLicenseButtonText(text));
}
