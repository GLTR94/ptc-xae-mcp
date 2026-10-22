namespace PtcXaeMcp.WinForms;

public partial class Form1
{
    // Issue #157 item 10: hand-written layout helpers that InitializeComponent() (the
    // Designer-generated part of Form1, in Form1.Designer.cs) calls, kept out of the Designer
    // file so it stays purely generated/regeneratable. Since #270 the header is laid out by
    // TableLayoutPanel/FlowLayoutPanel docking, so no Resize-driven positioning is left.

    /// <summary>One header status chip: a coloured dot and its text, vertically centred.</summary>
    private static void ConfigureStatusChip(
        FlowLayoutPanel chip, string chipName, Label dot, string dotName, Label text, string textName, string initialText)
    {
        chip.AutoSize = true;
        chip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        chip.WrapContents = false;
        chip.Margin = new Padding(0, 0, 20, 0);
        chip.Name = chipName;
        chip.Controls.Add(dot);
        chip.Controls.Add(text);

        ConfigureStatusDot(dot, dotName);
        dot.Anchor = AnchorStyles.Left; // centred against the text in the flow row
        dot.Margin = new Padding(0, 0, 6, 0);

        text.Anchor = AnchorStyles.Left;
        text.AutoSize = true;
        text.Margin = new Padding(0);
        text.Name = textName;
        text.Text = initialText;
        text.ForeColor = Theme.ThemeManager.Palette.Inactive;
    }

    private static void ConfigureStatusDot(Label dot, string name)
    {
        dot.AutoSize = false;
        dot.Size = new Size(10, 10);
        dot.Name = name;
        dot.Text = string.Empty;
        dot.BackColor = Theme.ThemeManager.Palette.Inactive;
        dot.BorderStyle = BorderStyle.None;
    }
}
