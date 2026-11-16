using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Controls;

/// <summary>
/// The row of workspace tabs in the top bar. Each workspace is the same window arranged for one
/// job — laying out a scene, shading it, rendering it, debugging the frame.
/// </summary>
internal sealed class WorkspaceTabs : Control
{
    private readonly List<(string Name, Rectangle Bounds)> _tabs = [];
    private int _selected;
    private int _hover = -1;

    public WorkspaceTabs()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.TopBar;
        Font = Theme.UiFont;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? SelectedChanged;

    public string Selected => _tabs.Count > 0 ? _tabs[_selected].Name : string.Empty;

    public void SetTabs(params string[] names)
    {
        _tabs.Clear();

        var x = 4;

        foreach (var name in names)
        {
            var width = TextRenderer.MeasureText(name, Font).Width + 22;
            _tabs.Add((name, new Rectangle(x, 0, width, 0)));
            x += width + 2;
        }

        Width = x + 4;
        _selected = 0;
        Invalidate();
    }

    public void Select(string name)
    {
        var index = _tabs.FindIndex(tab => tab.Name == name);

        if (index < 0 || index == _selected)
        {
            return;
        }

        _selected = index;
        Invalidate();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    private int TabAt(Point point) => _tabs.FindIndex(tab => point.X >= tab.Bounds.Left && point.X < tab.Bounds.Right);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hover = TabAt(e.Location);

        if (hover != _hover)
        {
            _hover = hover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && TabAt(e.Location) is >= 0 and var index)
        {
            Select(_tabs[index].Name);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        for (var i = 0; i < _tabs.Count; i++)
        {
            var (name, bounds) = _tabs[i];
            var tab = new RectangleF(bounds.X, 4, bounds.Width, Height - 4);

            if (i == _selected)
            {
                // Rounded on top, square where it meets the editors below.
                using var path = Theme.RoundedRect(new RectangleF(tab.X, tab.Y, tab.Width, tab.Height + 6), 4);
                using var brush = new SolidBrush(Theme.Header);
                g.SetClip(new RectangleF(0, 0, Width, Height));
                g.FillPath(brush, path);
                g.ResetClip();
            }
            else if (i == _hover)
            {
                Theme.FillRounded(g, Color.FromArgb(48, 48, 48), new RectangleF(tab.X, tab.Y, tab.Width, tab.Height - 3), 4);
            }

            TextRenderer.DrawText(g, name, Font, Rectangle.Round(tab),
                i == _selected ? Theme.TextPrimary : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
