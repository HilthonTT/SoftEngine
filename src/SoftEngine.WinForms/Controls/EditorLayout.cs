using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Controls;

/// <summary>
/// Lays its visible children out top to bottom at its own width, and sizes its height to fit
/// them. The column every properties page and panel body is built from.
/// </summary>
internal class StackPanel : Panel
{
    public StackPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    [DefaultValue(0)]
    public int Spacing { get; set; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        var y = Padding.Top;
        var width = ClientSize.Width - Padding.Horizontal;

        foreach (Control child in Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            child.SetBounds(Padding.Left + child.Margin.Left, y + child.Margin.Top,
                Math.Max(0, width - child.Margin.Horizontal), child.Height);

            y += child.Height + child.Margin.Vertical + Spacing;
        }

        var height = y - (Controls.Count > 0 ? Spacing : 0) + Padding.Bottom;

        if (Dock is not DockStyle.Fill and not DockStyle.Left and not DockStyle.Right && Height != height)
        {
            Height = height;
        }

        base.OnLayout(levent);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        e.Control!.VisibleChanged += (s, a) => PerformLayout();
        e.Control.Resize += (s, a) => PerformLayout();
    }
}

/// <summary>
/// One editor of the window — viewport, outliner, properties, timeline — with the header strip
/// that names it, a place in that header for its own controls, and a body under it.
/// </summary>
internal sealed class EditorArea : Panel
{
    private const int HeaderHeight = 28;

    private readonly Panel _header;

    public EditorArea(string title, Glyph glyph)
    {
        Title = title;
        Glyph = glyph;

        BackColor = Theme.Gap;
        Padding = new Padding(1);

        _header = new HeaderStrip(this) { Dock = DockStyle.Top, Height = HeaderHeight, BackColor = Theme.Header };

        HeaderTools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Header,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 4, 0),
        };

        HeaderRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Header,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 6, 0),
        };

        _header.Controls.Add(HeaderTools);
        _header.Controls.Add(HeaderRight);

        Body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };

        Controls.Add(Body);
        Controls.Add(_header);
    }

    public string Title { get; }

    public Glyph Glyph { get; }

    /// <summary>Controls after the editor's name, left to right.</summary>
    public FlowLayoutPanel HeaderTools { get; }

    /// <summary>Controls pinned to the header's right edge.</summary>
    public FlowLayoutPanel HeaderRight { get; }

    public Panel Body { get; }

    private sealed class HeaderStrip : Panel
    {
        private readonly EditorArea _owner;

        public HeaderStrip(EditorArea owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            UpdatePadding();
        }

        // Room for the editor-type badge and the name, painted rather than hosted.
        private void UpdatePadding() =>
            Padding = new Padding(LogicalToDeviceUnits(40) + TextRenderer.MeasureText(_owner.Title, Theme.UiFontBold).Width, 0, 0, 0);

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            UpdatePadding();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdatePadding();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var badge = Height - LogicalToDeviceUnits(9);
            var x = LogicalToDeviceUnits(5);
            Theme.FillRounded(g, Theme.Widget, new RectangleF(x, (Height - badge) / 2f, badge * 1.2f, badge), 3);
            var icon = badge * 0.68f;
            GlyphPainter.Draw(g, _owner.Glyph, new RectangleF(x + (badge * 1.2f - icon) / 2f, (Height - icon) / 2f, icon, icon), Theme.TextPrimary);

            var textLeft = (int)(x + badge * 1.2f + LogicalToDeviceUnits(6));
            TextRenderer.DrawText(g, _owner.Title, Theme.UiFontBold, new Rectangle(textLeft, 0, Width - textLeft, Height),
                Theme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            using var line = new Pen(Theme.Outline);
            g.DrawLine(line, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>
/// A collapsible panel in the properties editor: a header with a disclosure arrow over a column
/// of rows, on a rounded card a shade lighter than the editor behind it.
/// </summary>
internal sealed class PropertyPanel : Panel
{
    private const int LogicalHeaderHeight = 26;

    private int HeaderHeight => LogicalToDeviceUnits(LogicalHeaderHeight);

    private bool _expanded = true;
    private bool _hover;

    public PropertyPanel(string title)
    {
        Title = title;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
        Margin = new Padding(0, 0, 0, 4);

        Content = new StackPanel
        {
            BackColor = Theme.Panel,
            Padding = new Padding(10, 2, 10, 8),
            Spacing = 1,
        };

        Content.Resize += (s, e) => UpdateHeight();
        Controls.Add(Content);

        UpdateHeight();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateHeight();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateHeight();
    }

    public event EventHandler? ExpandedChanged;

    public string Title { get; }

    public StackPanel Content { get; }

    [DefaultValue(true)]
    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            Content.Visible = value;
            UpdateHeight();
            Invalidate();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public T Add<T>(T control) where T : Control
    {
        Content.Controls.Add(control);
        return control;
    }

    private void UpdateHeight()
    {
        var height = HeaderHeight + (_expanded ? Content.Height + 2 : 0);

        if (Height != height)
        {
            Height = height;
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        Content?.SetBounds(0, HeaderHeight, Width, Content.Height);
        base.OnLayout(levent);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hover = e.Y < HeaderHeight;

        if (hover != _hover)
        {
            _hover = hover;
            Cursor = hover ? Cursors.Hand : Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (e.Button == MouseButtons.Left && e.Y < HeaderHeight)
        {
            Expanded = !Expanded;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var card = new RectangleF(0, 0, Width - 1, Height - 3);
        Theme.FillRounded(g, Theme.Panel, card, 4);

        if (_expanded)
        {
            // Square off the header's bottom corners where the body continues beneath it.
            using var header = Theme.RoundedRect(new RectangleF(0, 0, Width - 1, HeaderHeight + 4), 4);
            using var clip = new Region(new RectangleF(0, 0, Width, HeaderHeight));
            g.SetClip(clip, CombineMode.Replace);
            using var brush = new SolidBrush(_hover ? Theme.Lerp(Theme.PanelHeader, Theme.WidgetHover, 0.3f) : Theme.PanelHeader);
            g.FillPath(brush, header);
            g.ResetClip();
        }
        else
        {
            Theme.FillRounded(g, _hover ? Theme.Lerp(Theme.PanelHeader, Theme.WidgetHover, 0.3f) : Theme.PanelHeader, card, 4);
        }

        // Disclosure arrow.
        using (var arrow = new SolidBrush(Theme.TextSecondary))
        {
            var k = DeviceDpi / 96f;
            var cx = 13f * k;
            var cy = HeaderHeight / 2f;

            PointF[] points = _expanded
                ? [new(cx - 4 * k, cy - 2 * k), new(cx + 4 * k, cy - 2 * k), new(cx, cy + 3 * k)]
                : [new(cx - 2 * k, cy - 4 * k), new(cx - 2 * k, cy + 4 * k), new(cx + 3 * k, cy)];

            g.FillPolygon(arrow, points);
        }

        var left = LogicalToDeviceUnits(24);
        TextRenderer.DrawText(g, Title, Theme.UiFont, new Rectangle(left, 0, Width - left - 4, HeaderHeight),
            Theme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }
}

/// <summary>A label on the left and a widget filling the rest of the row, as a property reads.</summary>
internal sealed class PropertyRow : Panel
{
    private readonly Label? _label;

    public PropertyRow(string label, Control field, int height = 24)
    {
        Height = height;
        BackColor = Theme.Panel;
        Margin = new Padding(0, 1, 0, 1);

        _label = new Label
        {
            Text = label,
            ForeColor = Theme.TextSecondary,
            Font = Theme.UiFont,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            BackColor = Theme.Panel,
        };

        Field = field;

        Controls.Add(_label);
        Controls.Add(field);
    }

    public Control? Field { get; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Setting Height in the constructor lays out before the children exist.
        if (_label is null || Field is null)
        {
            base.OnLayout(levent);
            return;
        }

        var split = (int)(Width * 0.4f);
        _label.SetBounds(0, 0, split - 6, Height);
        Field.SetBounds(split, (Height - Field.Height) / 2, Width - split, Field.Height);
        base.OnLayout(levent);
    }
}

/// <summary>A small grey caption between groups of rows inside one panel.</summary>
internal sealed class SubHeading : Label
{
    public SubHeading(string text)
    {
        Text = text;
        Height = 22;
        ForeColor = Theme.TextSecondary;
        Font = Theme.SmallFont;
        TextAlign = ContentAlignment.BottomLeft;
        BackColor = Theme.Panel;
        Margin = new Padding(0, 4, 0, 0);
    }
}

/// <summary>
/// The properties editor: a vertical strip of tabs — render, world, object, material, debug —
/// and the selected tab's page of panels beside it, scrolling when it runs long.
/// </summary>
internal sealed class PropertiesEditor : Panel
{
    private const int StripWidth = 32;

    private readonly Panel _strip;
    private readonly Panel _host;
    private readonly ToolTip _tips = new();
    private readonly List<(string Key, IconButton Button, StackPanel Page)> _tabs = [];

    public PropertiesEditor()
    {
        BackColor = Theme.Surface;

        _strip = new Panel { Dock = DockStyle.Left, Width = StripWidth, BackColor = Theme.Background, Padding = new Padding(3, 6, 3, 6) };

        _host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(6, 6, 8, 6) };
        _host.Resize += (s, e) => FitPages();

        Controls.Add(_host);
        Controls.Add(_strip);
    }

    public event EventHandler? SelectedTabChanged;

    public string? SelectedTab { get; private set; }

    public IEnumerable<string> Tabs => _tabs.Select(tab => tab.Key);

    public StackPanel AddTab(string key, Glyph glyph, string tooltip)
    {
        var page = new StackPanel { BackColor = Theme.Surface, Visible = false, Location = Point.Empty };

        var button = new IconButton
        {
            Glyph = glyph,
            IdleColor = Theme.Background,
        };

        _tips.SetToolTip(button, tooltip);
        button.Click += (s, e) => Select(key);

        _strip.Controls.Add(button);
        _host.Controls.Add(page);
        _tabs.Add((key, button, page));

        PlaceTabButtons();

        if (SelectedTab is null)
        {
            Select(key);
        }

        return page;
    }

    public void Select(string key)
    {
        if (SelectedTab == key)
        {
            return;
        }

        SelectedTab = key;

        foreach (var (tabKey, button, page) in _tabs)
        {
            var selected = tabKey == key;
            button.Checked = selected;
            page.Visible = selected;
        }

        _host.AutoScrollPosition = Point.Empty;
        FitPages();

        SelectedTabChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tab buttons are placed by hand, in device pixels, whenever the DPI may have moved.</summary>
    private void PlaceTabButtons()
    {
        var width = LogicalToDeviceUnits(StripWidth);
        _strip.Width = width;

        var size = width - LogicalToDeviceUnits(6);
        var y = LogicalToDeviceUnits(6);

        for (var i = 0; i < _tabs.Count; i++)
        {
            if (i == 5)
            {
                // A little air between the scene tabs and the debugging one, as between groups.
                y += LogicalToDeviceUnits(8);
            }

            _tabs[i].Button.SetBounds(LogicalToDeviceUnits(3), y, size, size);
            y += size + LogicalToDeviceUnits(4);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PlaceTabButtons();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PlaceTabButtons();
    }

    private void FitPages()
    {
        var width = _host.ClientSize.Width - _host.Padding.Horizontal;

        foreach (var (_, _, page) in _tabs)
        {
            page.SetBounds(_host.Padding.Left, _host.Padding.Top + _host.AutoScrollPosition.Y, Math.Max(80, width), page.Height);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }
}
