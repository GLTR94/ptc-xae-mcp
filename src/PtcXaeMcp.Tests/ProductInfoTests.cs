using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Issue #270: the header logo is an embedded file (<c>Assets/logo.png</c>) instead of a
/// bitmap serialized into <c>Form1.resx</c>.
/// </summary>
public sealed class ProductInfoTests
{
    [Fact]
    public void LogoResource_IsEmbeddedUnderItsLogicalName()
    {
        var names = typeof(ProductInfo).Assembly.GetManifestResourceNames();
        Assert.Contains(ProductInfo.LogoResourceName, names);
    }

    [Fact]
    public void LoadLogo_ReturnsAWideImage()
    {
        using var logo = ProductInfo.LoadLogo();
        Assert.NotNull(logo);
        Assert.True(logo.Width > logo.Height, $"logo is {logo.Width}x{logo.Height}");
    }
}
