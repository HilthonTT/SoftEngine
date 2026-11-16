namespace SoftEngine.WinForms.Debugging;

internal sealed class DockPanelHeader : Control
{
    public DockPanelHeader(string title)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        Text = title;
        Dock = DockStyle.Top;
        Height = 28;
        BackColor = Theme.Header;
        Font = Theme.UiFontBold;
    }

    // Drawn as every other editor's header is: a badge naming the kind of editor, then its title.
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Header);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var badge = Height - LogicalToDeviceUnits(9);
        var x = LogicalToDeviceUnits(5);
        Theme.FillRounded(g, Theme.Widget, new RectangleF(x, (Height - badge) / 2f, badge * 1.2f, badge), 3);
        var icon = badge * 0.68f;
        SoftEngine.WinForms.Controls.GlyphPainter.Draw(g, SoftEngine.WinForms.Controls.Glyph.Debug,
            new RectangleF(x + (badge * 1.2f - icon) / 2f, (Height - icon) / 2f, icon, icon), Theme.TextPrimary);

        var left = (int)(x + badge * 1.2f + LogicalToDeviceUnits(6));

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            new Rectangle(left, 0, Width - left - 2, Height),
            Theme.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        using var line = new Pen(Theme.Outline);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}
