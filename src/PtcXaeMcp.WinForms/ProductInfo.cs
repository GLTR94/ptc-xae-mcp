using System.Runtime.InteropServices;

namespace PtcXaeMcp.WinForms;

/// <summary>
/// The product's display name, shown in the window title, the header, the MCP
/// <c>serverInfo.title</c> and host messages. Technical identifiers (exe name
/// <c>ptc-xae-mcp.exe</c>, install folder, <c>%LOCALAPPDATA%\ptc-xae-mcp</c>, the MCP
/// <c>serverInfo.name</c> and client config keys) deliberately keep the old spelling
/// so existing installs, updates and client configurations keep working.
/// </summary>
internal static class ProductInfo
{
    public const string DisplayName = "Pneutronic XAE MCP";

    /// <summary>Company website, opened from the header logo and the menu (same as in NOTICE).</summary>
    public const string WebsiteUrl = "https://pneutronic.ch";

    /// <summary>Manifest name of the header logo (<c>Assets/logo.png</c>, an
    /// <c>EmbeddedResource</c> with this <c>LogicalName</c> in the csproj). Branding lives here and
    /// in that one file instead of a bitmap serialized into <c>Form1.resx</c> (issue #270).</summary>
    internal const string LogoResourceName = "PtcXaeMcp.WinForms.Assets.logo.png";

    /// <summary>Grey of the word mark on a dark background (the logo's own grey is too dark there).</summary>
    internal static readonly Color LogoGreyOnDark = Color.FromArgb(0xC9, 0xCD, 0xD2);

    /// <summary>
    /// The header logo, or <c>null</c> when the resource is missing or unreadable (the header
    /// then shows the product name only). With <paramref name="forDarkBackground"/> the grey
    /// lettering is lightened (<see cref="LogoGreyOnDark"/>); the red bar and cross stay red.
    /// The caller owns the returned image.
    /// </summary>
    public static Image? LoadLogo(bool forDarkBackground = false)
    {
        try
        {
            using Stream? stream = typeof(ProductInfo).Assembly.GetManifestResourceStream(LogoResourceName);
            if (stream is null)
                return null;
            // Image.FromStream needs its stream for the image's lifetime; the copy does not.
            using var decoded = Image.FromStream(stream);
            var bitmap = new Bitmap(decoded);
            if (forDarkBackground)
                LightenGreys(bitmap, LogoGreyOnDark);
            return bitmap;
        }
        catch (ArgumentException)
        {
            return null;
        }
        // GDI+ reports many invalid or unsupported image files as OutOfMemoryException.
        catch (OutOfMemoryException)
        {
            return null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    /// <summary>
    /// Recolours every grey (unsaturated) pixel to <paramref name="target"/>, keeping its alpha so
    /// anti-aliased edges stay smooth; coloured pixels (the red bar and cross) are left alone.
    /// </summary>
    internal static void LightenGreys(Bitmap bitmap, Color target)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * bitmap.Height;
            var px = new byte[bytes];
            Marshal.Copy(data.Scan0, px, 0, bytes);
            for (int i = 0; i < bytes; i += 4)
            {
                byte b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                if (a == 0) continue;
                int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                if (max - min > 40) continue; // saturated: brand red
                px[i] = target.B; px[i + 1] = target.G; px[i + 2] = target.R;
            }
            Marshal.Copy(px, 0, data.Scan0, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
