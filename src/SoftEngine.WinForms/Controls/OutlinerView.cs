using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Controls;

/// <summary>One row of the outliner.</summary>
internal sealed class OutlinerItem(string text, Glyph glyph, int depth, object? tag)
{
    public string Text { get; } = text;

    public Glyph Glyph { get; } = glyph;

    public int Depth { get; } = depth;

    public object? Tag { get; } = tag;

    /// <summary>Whether the row has an eye to toggle, and what it shows; null for none.</summary>
    public bool? Visible { get; set; }

    public bool Selectable { get; init; } = true;

    public string? Detail { get; init; }

    public Color? GlyphColor { get; init; }
}

/// <summary>
/// The scene as a tree of named rows — the collection, the camera, the lights, every mesh —
/// with an eye on each mesh to hide it and the selected object picked out in the editor's
/// active-object colour.
/// </summary>
internal sealed class OutlinerView : ScrollableControl
{
    private int RowHeight => LogicalToDeviceUnits(22);

    private int Indent => LogicalToDeviceUnits(18);

    private int EyeWidth => LogicalToDeviceUnits(26);

    private IReadOnlyList<OutlinerItem> _items = [];
    private int _hoverRow = -1;

    public OutlinerView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Surface;
        Font = Theme.UiFont;
        AutoScroll = true;
    }

    public event EventHandler<OutlinerItem>? ItemActivated;

    public event EventHandler<OutlinerItem>? VisibilityToggled;

    public event EventHandler<OutlinerItem>? ItemDoubleClicked;

    public object? SelectedTag { get; private set; }

    public IReadOnlyList<OutlinerItem> Items => _items;

    public void SetItems(IReadOnlyList<OutlinerItem> items)
    {
        _items = items;
        AutoScrollMinSize = new Size(0, items.Count * RowHeight + 4);
        Invalidate();
    }

    public void Select(object? tag)
    {
        if (ReferenceEquals(SelectedTag, tag))
        {
            return;
        }

        SelectedTag = tag;

        var index = IndexOf(tag);

        if (index >= 0)
        {
            EnsureVisible(index);
        }

        Invalidate();
    }

    private int IndexOf(object? tag)
    {
        if (tag is null)
        {
            return -1;
        }

        for (var i = 0; i < _items.Count; i++)
        {
            if (ReferenceEquals(_items[i].Tag, tag))
            {
                return i;
            }
        }

        return -1;
    }

    private void EnsureVisible(int index)
    {
        var top = index * RowHeight;
        var scroll = -AutoScrollPosition.Y;

        if (top < scroll)
        {
            AutoScrollPosition = new Point(0, top);
        }
        else if (top + RowHeight > scroll + ClientSize.Height)
        {
            AutoScrollPosition = new Point(0, top + RowHeight - ClientSize.Height);
        }
    }

    private int RowAt(int y)
    {
        var row = (y - AutoScrollPosition.Y - 2) / RowHeight;
        return row >= 0 && row < _items.Count ? row : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var row = RowAt(e.Y);

        if (row != _hoverRow)
        {
            _hoverRow = row;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverRow = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button != MouseButtons.Left || RowAt(e.Y) is not (>= 0 and var row))
        {
            return;
        }

        var item = _items[row];

        if (item.Visible is not null && e.X >= ClientSize.Width - EyeWidth)
        {
            VisibilityToggled?.Invoke(this, item);
            Invalidate();
            return;
        }

        if (!item.Selectable)
        {
            return;
        }

        SelectedTag = item.Tag;
        Invalidate();
        ItemActivated?.Invoke(this, item);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (RowAt(e.Y) is >= 0 and var row && e.X < ClientSize.Width - EyeWidth)
        {
            ItemDoubleClicked?.Invoke(this, _items[row]);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var step = e.KeyCode switch { Keys.Up => -1, Keys.Down => 1, _ => 0 };

        if (step == 0)
        {
            return;
        }

        var index = IndexOf(SelectedTag);

        for (var i = index + step; i >= 0 && i < _items.Count; i += step)
        {
            if (_items[i].Selectable)
            {
                SelectedTag = _items[i].Tag;
                EnsureVisible(i);
                Invalidate();
                ItemActivated?.Invoke(this, _items[i]);
                break;
            }
        }

        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var offset = AutoScrollPosition.Y + 2;
        var width = ClientSize.Width;

        using var guide = new Pen(Color.FromArgb(70, 70, 70));

        for (var i = 0; i < _items.Count; i++)
        {
            var y = offset + i * RowHeight;

            if (y + RowHeight < 0 || y > ClientSize.Height)
            {
                continue;
            }

            var item = _items[i];
            var selected = item.Tag is not null && ReferenceEquals(item.Tag, SelectedTag);

            var row = new Rectangle(0, y, width, RowHeight);

            if (selected)
            {
                using var brush = new SolidBrush(Theme.Selection);
                g.FillRectangle(brush, row);
            }
            else if (i == _hoverRow && item.Selectable)
            {
                using var brush = new SolidBrush(Color.FromArgb(56, 56, 56));
                g.FillRectangle(brush, row);
            }
            else if (i % 2 == 1)
            {
                using var brush = new SolidBrush(Color.FromArgb(45, 45, 45));
                g.FillRectangle(brush, row);
            }

            var pad = LogicalToDeviceUnits(8);
            var icon = LogicalToDeviceUnits(14);
            var x = pad + item.Depth * Indent;

            // Tree guide lines down from each parent.
            for (var d = 1; d <= item.Depth; d++)
            {
                var gx = pad + (d - 1) * Indent + icon / 2;
                g.DrawLine(guide, gx, y, gx, y + RowHeight);
            }

            var dimmed = item.Visible == false;
            var textColor = dimmed ? Theme.TextDisabled
                : selected ? (item.Tag is not null ? Theme.ActiveObject : Theme.TextPrimary)
                : Theme.TextPrimary;

            var glyphColor = dimmed ? Theme.TextDisabled : item.GlyphColor ?? Theme.TextSecondary;
            GlyphPainter.Draw(g, item.Glyph, new RectangleF(x, y + (RowHeight - icon) / 2f, icon, icon), glyphColor);

            var textLeft = x + icon + LogicalToDeviceUnits(6);
            var textRect = new Rectangle(textLeft, y, width - textLeft - (item.Visible is null ? 4 : EyeWidth + 4), RowHeight);
            TextRenderer.DrawText(g, item.Text, Font, textRect, textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (item.Detail is { Length: > 0 } detail)
            {
                var measured = TextRenderer.MeasureText(item.Text, Font).Width;
                var detailRect = new Rectangle(textRect.X + measured + 4, y, textRect.Width - measured - 4, RowHeight);

                if (detailRect.Width > 20)
                {
                    TextRenderer.DrawText(g, detail, Theme.SmallFont, detailRect, Theme.TextDisabled,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }

            if (item.Visible is { } visible)
            {
                var eye = LogicalToDeviceUnits(15);
                GlyphPainter.Draw(g, visible ? Glyph.Eye : Glyph.EyeClosed,
                    new RectangleF(width - EyeWidth + (EyeWidth - eye) / 2f, y + (RowHeight - eye) / 2f, eye, eye),
                    visible ? Theme.TextPrimary : Theme.TextDisabled);
            }
        }
    }
}
