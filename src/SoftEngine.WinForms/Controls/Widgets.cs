using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Controls;

/// <summary>
/// A check box drawn as the editor draws one: a small rounded square, blue with a tick when on.
/// It stays a <see cref="CheckBox"/>, so everything bound to Checked keeps working unchanged.
/// </summary>
internal sealed class ThemedCheckBox : CheckBox
{
    private bool _hover;

    public ThemedCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 22;
        Font = Theme.UiFont;
        ForeColor = Theme.TextPrimary;
        Margin = new Padding(0, 1, 0, 1);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        base.OnMouseEnter(eventargs);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        base.OnMouseLeave(eventargs);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var size = LogicalToDeviceUnits(14);
        var box = new RectangleF(2, (Height - size) / 2f, size, size);

        var fill = Checked
            ? (_hover ? Theme.AccentHover : Theme.Accent)
            : (_hover ? Theme.WidgetHover : Theme.Widget);

        if (!Enabled)
        {
            fill = Theme.Lerp(fill, Theme.Panel, 0.55f);
        }

        Theme.FillRounded(g, fill, box, 3);

        if (Checked)
        {
            var k = size / 14f;
            using var pen = new Pen(Enabled ? Color.White : Theme.TextSecondary, 1.8f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(pen, [new PointF(box.X + 3.2f * k, box.Y + 7.3f * k), new PointF(box.X + 6f * k, box.Y + 10f * k), new PointF(box.X + 11f * k, box.Y + 4f * k)]);
        }

        var indent = size + LogicalToDeviceUnits(8);
        var text = new Rectangle(indent, 0, Width - indent, Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? ForeColor : Theme.TextDisabled,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            Theme.DrawRounded(g, Theme.AccentHover, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 3);
        }
    }
}

/// <summary>
/// A radio button drawn as one row of an expanded enum: a full-width button that turns blue when
/// it is the chosen one. A column of them reads as a single list of mutually exclusive choices.
/// </summary>
internal sealed class ThemedRadioButton : RadioButton
{
    private bool _hover;

    public ThemedRadioButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        Height = 22;
        Font = Theme.UiFont;
        ForeColor = Theme.TextPrimary;
        Margin = new Padding(0, 0, 0, 1);
        Cursor = Cursors.Hand;
    }

    [DefaultValue(Glyph.None)]
    public Glyph Glyph { get; set; }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        base.OnMouseEnter(eventargs);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        base.OnMouseLeave(eventargs);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fill = Checked
            ? (_hover ? Theme.AccentHover : Theme.Accent)
            : (_hover ? Theme.WidgetHover : Theme.Widget);

        Theme.FillRounded(g, fill, new RectangleF(0, 0, Width - 1, Height - 1), 3);

        var left = LogicalToDeviceUnits(8);

        if (Glyph != Glyph.None)
        {
            var icon = LogicalToDeviceUnits(14);
            GlyphPainter.Draw(g, Glyph, new RectangleF(LogicalToDeviceUnits(6), (Height - icon) / 2f, icon, icon), ForeColor);
            left = LogicalToDeviceUnits(26);
        }

        TextRenderer.DrawText(g, Text, Font, new Rectangle(left, 0, Width - left - 4, Height),
            Enabled ? ForeColor : Theme.TextDisabled,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>A flat rounded push button in the widget grey.</summary>
internal sealed class ThemedButton : Button
{
    private bool _hover;
    private bool _pressed;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 24;
        Font = Theme.UiFont;
        ForeColor = Theme.TextPrimary;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 2, 0, 2);
    }

    [DefaultValue(Glyph.None)]
    public Glyph Glyph { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fill = _pressed ? Theme.Accent : _hover && Enabled ? Theme.WidgetHover : Theme.Widget;

        if (!Enabled)
        {
            fill = Theme.Lerp(fill, Theme.Panel, 0.55f);
        }

        Theme.FillRounded(g, fill, new RectangleF(0, 0, Width - 1, Height - 1), 3);

        var text = new Rectangle(0, 0, Width, Height);

        if (Glyph != Glyph.None)
        {
            var size = TextRenderer.MeasureText(Text, Font);
            var icon = LogicalToDeviceUnits(14);
            var gap = LogicalToDeviceUnits(4);
            var start = (Width - size.Width - icon - gap) / 2;
            GlyphPainter.Draw(g, Glyph, new RectangleF(start, (Height - icon) / 2f, icon, icon), Enabled ? ForeColor : Theme.TextDisabled);
            text = new Rectangle(start + icon + gap, 0, size.Width + 4, Height);
        }

        TextRenderer.DrawText(g, Text, Font, text, Enabled ? ForeColor : Theme.TextDisabled,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// A square icon button, optionally a toggle. Several placed edge to edge with
/// <see cref="Segment"/> set read as one segmented control, as the shading switch does.
/// </summary>
internal sealed class IconButton : Control
{
    private bool _hover;
    private bool _checked;

    public IconButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);
        Size = new Size(26, 22);
        Cursor = Cursors.Hand;
        ForeColor = Theme.TextPrimary;
        BackColor = Color.Transparent;
    }

    public event EventHandler? CheckedChanged;

    [DefaultValue(Glyph.None)]
    public Glyph Glyph { get; set; }

    [DefaultValue(false)]
    public bool Toggle { get; set; }

    /// <summary>Which corners are rounded when buttons sit in a row: -1 first, 1 last, 0 middle, 2 alone.</summary>
    [DefaultValue(2)]
    public int Segment { get; set; } = 2;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DefaultValue(null)]
    public Color? IdleColor { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        if (Toggle)
        {
            Checked = !Checked;
        }

        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fill = _checked ? (_hover ? Theme.AccentHover : Theme.Accent)
            : _hover ? Theme.WidgetHover
            : IdleColor ?? Theme.Widget;

        var bounds = new RectangleF(0, 0, Width - 1, Height - 1);

        using (var path = SegmentPath(bounds, 3, Segment))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        var icon = MathF.Min(Width, Height) - LogicalToDeviceUnits(8);
        GlyphPainter.Draw(g, Glyph, new RectangleF((Width - icon) / 2f, (Height - icon) / 2f, icon, icon),
            Enabled ? ForeColor : Theme.TextDisabled);
    }

    private static GraphicsPath SegmentPath(RectangleF r, float radius, int segment)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        var roundLeft = segment is -1 or 2;
        var roundRight = segment is 1 or 2;

        if (roundLeft)
        {
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
        }
        else
        {
            path.AddLine(r.Left, r.Top, r.Left, r.Top);
        }

        if (roundRight)
        {
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        }
        else
        {
            path.AddLine(r.Right, r.Top, r.Right, r.Bottom);
        }

        if (roundLeft)
        {
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        }
        else
        {
            path.AddLine(r.Left, r.Bottom, r.Left, r.Bottom);
        }

        path.CloseFigure();
        return path;
    }
}

/// <summary>Menus and tool strips in the editor's greys, with the blue highlight.</summary>
internal sealed class EditorMenuRenderer : ToolStripProfessionalRenderer
{
    public EditorMenuRenderer() : base(new EditorColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextSecondary;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new RectangleF(e.ImageRectangle.X + 1, e.ImageRectangle.Y + 1, 14, 14);
        Theme.FillRounded(g, Theme.Accent, r, 3);

        using var pen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, [new PointF(r.X + 3.2f, r.Y + 7.3f), new PointF(r.X + 6f, r.Y + 10f), new PointF(r.X + 11f, r.Y + 4f)]);
    }

    private sealed class EditorColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Theme.TopBar;
        public override Color MenuStripGradientEnd => Theme.TopBar;
        public override Color ToolStripDropDownBackground => Theme.Background;
        public override Color ImageMarginGradientBegin => Theme.Background;
        public override Color ImageMarginGradientMiddle => Theme.Background;
        public override Color ImageMarginGradientEnd => Theme.Background;
        public override Color MenuBorder => Theme.Outline;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuItemSelected => Theme.Accent;
        public override Color MenuItemSelectedGradientBegin => Theme.Accent;
        public override Color MenuItemSelectedGradientEnd => Theme.Accent;
        public override Color MenuItemPressedGradientBegin => Theme.Header;
        public override Color MenuItemPressedGradientMiddle => Theme.Header;
        public override Color MenuItemPressedGradientEnd => Theme.Header;
        public override Color SeparatorDark => Theme.Widget;
        public override Color SeparatorLight => Theme.Background;
        public override Color CheckBackground => Theme.Accent;
        public override Color CheckSelectedBackground => Theme.AccentHover;
        public override Color CheckPressedBackground => Theme.Accent;
        public override Color StatusStripGradientBegin => Theme.TopBar;
        public override Color StatusStripGradientEnd => Theme.TopBar;
        public override Color ToolStripBorder => Theme.TopBar;
        public override Color ToolStripGradientBegin => Theme.Header;
        public override Color ToolStripGradientMiddle => Theme.Header;
        public override Color ToolStripGradientEnd => Theme.Header;
        public override Color ButtonSelectedHighlight => Theme.WidgetHover;
        public override Color ButtonSelectedGradientBegin => Theme.WidgetHover;
        public override Color ButtonSelectedGradientEnd => Theme.WidgetHover;
        public override Color ButtonSelectedBorder => Theme.WidgetHover;
    }
}
