namespace PtcXaeMcp.WinForms.Theme;

/// <summary>
/// A <see cref="TabControl"/> that paints its whole surface from the theme palette. The stock
/// control only lets owner-draw paint the tab items; the strip behind them and the page border
/// stay in system colours, which left a white band across the dark theme.
/// </summary>
internal sealed class ThemedTabControl : TabControl
{
    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = ThemeManager.Palette;
        var g = e.Graphics;
        g.Clear(p.Background);

        Rectangle page = DisplayRectangle;
        int stripBottom = TabCount > 0 ? GetTabRect(0).Bottom : page.Top;

        using (var border = new Pen(p.Border))
        {
            // Line under the tab strip, interrupted below the selected tab.
            g.DrawLine(border, 0, stripBottom, Width - 1, stripBottom);

            for (int i = 0; i < TabCount; i++)
            {
                Rectangle r = GetTabRect(i);
                bool selected = i == SelectedIndex;
                if (!selected) r.Inflate(0, -1);
                using (var fill = new SolidBrush(selected ? p.Background : p.SurfaceAlt))
                    g.FillRectangle(fill, r);
                g.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - (selected ? 0 : 1));
                if (selected)
                    using (var bg = new Pen(p.Background))
                        g.DrawLine(bg, r.X + 1, stripBottom, r.Right - 2, stripBottom);
                TextRenderer.DrawText(g, TabPages[i].Text, Font, r, selected ? p.Text : p.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            // Frame around the page area.
            g.DrawRectangle(border, page.X - 1, stripBottom, page.Width + 1, Height - stripBottom - 1);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }
}
