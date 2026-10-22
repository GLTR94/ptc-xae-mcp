# Generates the app icon src\PtcXaeMcp.WinForms\favicon.ico (16-256 px, PNG-compressed entries)
# from the same geometry as src\PtcXaeMcp.WinForms\favicon.svg: "PTC" in grey on a white tile with
# a thin grey border and the red bar over the C, as above the C of the PNEUTRONIC word mark.
# The letters are drawn as geometry (no font), so the result does not depend on installed fonts.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\gen_app_icon.ps1 [-Preview preview.png]
#
# Uses System.Drawing (GDI+) of Windows PowerShell 5.1; no other dependency. Keep the numbers in
# sync with favicon.svg (viewBox 0 0 256 256).
[CmdletBinding()]
param(
    [string]$OutIco = '',
    [string]$Preview = ''
)
$ErrorActionPreference = 'Stop'
# $PSScriptRoot is still empty in parameter defaults under Windows PowerShell 5.1.
if (-not $OutIco) { $OutIco = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\PtcXaeMcp.WinForms\favicon.ico' }
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class PtcAppIcon {
  static readonly Color Grey = Color.FromArgb(0x6E, 0x70, 0x73);
  static readonly Color Red = Color.FromArgb(0xD2, 0x12, 0x2E);
  static readonly Color Border = Color.FromArgb(0xC9, 0xCB, 0xCE);
  static GraphicsPath Tile() {
    var p = new GraphicsPath(); const float x = 10, y = 10, w = 236, h = 236, d = 100;
    p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
    p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
    p.CloseFigure(); return p;
  }
  static GraphicsPath Letters() {
    var g = new GraphicsPath(FillMode.Alternate);
    // P: stem + bowl (outer radius 22, hole radius 6)
    g.StartFigure(); g.AddLine(34, 98, 75, 98); g.AddArc(53, 98, 44, 44, -90, 180);
    g.AddLine(75, 142, 50, 142); g.AddLine(50, 142, 50, 158); g.AddLine(50, 158, 34, 158); g.CloseFigure();
    g.StartFigure(); g.AddLine(50, 114, 75, 114); g.AddArc(69, 114, 12, 12, -90, 180); g.AddLine(75, 126, 50, 126); g.CloseFigure();
    // T
    g.StartFigure(); g.AddPolygon(new[] { new PointF(106, 98), new PointF(159, 98), new PointF(159, 114), new PointF(140.5f, 114),
      new PointF(140.5f, 158), new PointF(124.5f, 158), new PointF(124.5f, 114), new PointF(106, 114) });
    // C: ring segment, centre (198,128), radii 30/14, open +-40 degrees to the right
    g.StartFigure(); g.AddArc(168, 98, 60, 60, -40, -280); g.AddArc(184, 114, 28, 28, 40, 280); g.CloseFigure();
    return g;
  }
  public static Bitmap Render(int size) {
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var gr = Graphics.FromImage(bmp)) {
      gr.SmoothingMode = SmoothingMode.AntiAlias; gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
      gr.Clear(Color.Transparent);
      gr.ScaleTransform(size / 256f, size / 256f);
      using (var tile = Tile()) {
        gr.FillPath(Brushes.White, tile);
        using (var pen = new Pen(Border, 4)) gr.DrawPath(pen, tile);
      }
      using (var red = new SolidBrush(Red)) gr.FillRectangle(red, 168, 78, 53, 12);
      using (var letters = Letters()) using (var grey = new SolidBrush(Grey)) gr.FillPath(grey, letters);
    }
    return bmp;
  }
}
"@

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $img = [PtcAppIcon]::Render($s)
    $ms = New-Object System.IO.MemoryStream
    $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $img.Dispose()
    , $ms.ToArray()
}

# ICO container: header, one 16-byte directory entry per size, then the PNG images.
$fs = [System.IO.File]::Create($OutIco)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }   # 0 means 256
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close(); $fs.Close()
Write-Host "Icon: $OutIco ($($sizes -join ', ') px)"

if ($Preview) {
    $canvas = New-Object System.Drawing.Bitmap 520, 300
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $row = 0
    foreach ($bg in @([System.Drawing.Color]::FromArgb(247, 247, 245), [System.Drawing.Color]::FromArgb(30, 31, 34))) {
        $y = $row * 150
        $g.FillRectangle((New-Object System.Drawing.SolidBrush $bg), 0, $y, 520, 150)
        $x = 16
        foreach ($s in 128, 48, 32, 24, 16) {
            $img = [PtcAppIcon]::Render($s); $g.DrawImage($img, $x, $y + 140 - $s, $s, $s); $img.Dispose(); $x += $s + 24
        }
        $row++
    }
    $canvas.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $canvas.Dispose()
    Write-Host "Preview: $Preview"
}
