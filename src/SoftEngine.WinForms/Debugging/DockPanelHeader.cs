namespace SoftEngine.WinForms.Debugging;

/// <summary>The title bar every docked debugger panel wears.</summary>
internal sealed class DockPanelHeader : Control
{
    public DockPanelHeader(string title)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        Text = title;
        Dock = DockStyle.Top;
        Height = 26;
        BackColor = Theme.Surface;
        Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        using var back = new SolidBrush(Theme.Surface);
        g.FillRectangle(back, ClientRectangle);

        using var accent = new SolidBrush(Theme.Accent);
        g.FillRectangle(accent, 0, 6, 3, Height - 12);

        TextRenderer.DrawText(
            g,
            Text.ToUpperInvariant(),
            Font,
            new Rectangle(10, 0, Width - 12, Height),
            Theme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        using var line = new Pen(Theme.Background);
        g.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}
