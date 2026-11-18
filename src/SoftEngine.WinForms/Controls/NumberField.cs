using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace SoftEngine.WinForms.Controls;

/// <summary>
/// A number edited the way a 3D suite edits one: drag sideways across it to scrub the value,
/// or click without dragging to type a new one. Shift scrubs finer, Ctrl in whole steps.
/// </summary>
/// <remarks>
/// A drag reports <see cref="EditStarted"/> once, <see cref="ValueChanged"/> as the value moves,
/// and <see cref="EditCommitted"/> when the button comes up — so the owner can record the whole
/// gesture as one undoable edit, not one per pixel of travel.
/// </remarks>
internal sealed class NumberField : Control
{
    private const int DragThreshold = 3;

    private float _value;
    private bool _hover;
    private bool _pressed;
    private bool _dragging;
    private Point _pressPoint;
    private Point _lastPoint;
    private float _pressValue;
    private TextBox? _editor;

    /// <summary>Closes the open text box, committing or not; null when nothing is being typed.</summary>
    private Action<bool>? _finishTyping;

    public NumberField()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 22;
        Font = Theme.UiFont;
        ForeColor = Theme.TextPrimary;
        Cursor = Cursors.SizeWE;
        Margin = new Padding(0, 0, 0, 1);
    }

    public event EventHandler? EditStarted;

    public event EventHandler? ValueChanged;

    public event EventHandler? EditCommitted;

    public event EventHandler? EditCancelled;

    [DefaultValue("")]
    public string Label { get; set; } = string.Empty;

    [DefaultValue(null)]
    public Color? LabelColor { get; set; }

    /// <summary>How far the value travels per pixel dragged.</summary>
    [DefaultValue(0.01f)]
    public float Step { get; set; } = 0.01f;

    [DefaultValue("0.###")]
    public string Format { get; set; } = "0.###";

    [DefaultValue("")]
    public string Suffix { get; set; } = string.Empty;

    [DefaultValue(float.NegativeInfinity)]
    public float Minimum { get; set; } = float.NegativeInfinity;

    [DefaultValue(float.PositiveInfinity)]
    public float Maximum { get; set; } = float.PositiveInfinity;

    /// <summary>Which corners are rounded when fields are stacked: -1 top, 1 bottom, 0 middle, 2 alone.</summary>
    [DefaultValue(2)]
    public int Segment { get; set; } = 2;

    [DefaultValue(0f)]
    public float Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(float.IsFinite(value) ? value : 0f, Minimum, Maximum);

            if (_value.Equals(clamped))
            {
                return;
            }

            _value = clamped;
            Invalidate();
        }
    }

    public bool IsEditing => _dragging || _editor is not null;

    /// <summary>
    /// Drops what is being typed without committing it. For when what the field shows stops
    /// being true underneath it — another object selected, an undo — since committing then
    /// would write the typed number, and the stale numbers beside it, onto the wrong state.
    /// </summary>
    public void CancelTyping() => _finishTyping?.Invoke(false);

    private void SetFromUser(float value)
    {
        var before = _value;
        Value = value;

        if (!before.Equals(_value))
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.SizeWE : Cursors.Default;
        Invalidate();
    }

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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right && _dragging)
        {
            // Right-click during a drag puts the value back, as Escape does.
            CancelDrag();
            return;
        }

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        _pressed = true;
        _dragging = false;
        _pressPoint = e.Location;
        _lastPoint = e.Location;
        _pressValue = _value;
        Capture = true;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_pressed)
        {
            return;
        }

        if (!_dragging && Math.Abs(e.X - _pressPoint.X) >= DragThreshold)
        {
            _dragging = true;
            EditStarted?.Invoke(this, EventArgs.Empty);
        }

        if (!_dragging)
        {
            return;
        }

        var dx = e.X - _lastPoint.X;
        _lastPoint = e.Location;

        var step = Step;

        if ((ModifierKeys & Keys.Shift) != 0)
        {
            step *= 0.1f;
        }

        var next = _value + dx * step;

        if ((ModifierKeys & Keys.Control) != 0)
        {
            var coarse = Step * 10f;
            next = MathF.Round((_pressValue + (e.X - _pressPoint.X) * Step) / coarse) * coarse;
        }

        SetFromUser(next);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left || !_pressed)
        {
            return;
        }

        _pressed = false;
        Capture = false;

        if (_dragging)
        {
            _dragging = false;
            EditCommitted?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
        else
        {
            BeginTyping();
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Escape || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape && _dragging)
        {
            CancelDrag();
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Enter or Keys.F2 && !_dragging)
        {
            BeginTyping();
            e.Handled = true;
        }
    }

    private void CancelDrag()
    {
        _pressed = false;
        _dragging = false;
        Capture = false;
        SetFromUser(_pressValue);
        EditCancelled?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void BeginTyping()
    {
        if (_editor is not null || !Enabled)
        {
            return;
        }

        _editor = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.WidgetSunken,
            ForeColor = Theme.TextPrimary,
            Font = Font,
            Text = _value.ToString("0.#####", CultureInfo.InvariantCulture),
            TextAlign = HorizontalAlignment.Center,
        };

        _editor.SetBounds(6, (Height - _editor.PreferredHeight) / 2, Width - 12, _editor.PreferredHeight);

        var finished = false;

        void Finish(bool commit)
        {
            if (finished || _editor is not { } editor)
            {
                return;
            }

            finished = true;
            var text = editor.Text;
            _editor = null;
            _finishTyping = null;
            Controls.Remove(editor);
            editor.Dispose();

            // A number too large for a float parses as infinity, which the value would turn into 0.
            if (commit && float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var typed)
                       && float.IsFinite(typed) && !typed.Equals(_value))
            {
                EditStarted?.Invoke(this, EventArgs.Empty);
                SetFromUser(typed);
                EditCommitted?.Invoke(this, EventArgs.Empty);
            }

            Invalidate();
        }

        _editor.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Finish(commit: true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                Finish(commit: false);
            }
        };

        _editor.LostFocus += (s, e) => Finish(commit: true);
        _finishTyping = Finish;

        Controls.Add(_editor);
        _editor.Focus();
        _editor.SelectAll();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fill = _editor is not null ? Theme.WidgetSunken
            : _dragging ? Theme.Accent
            : _hover && Enabled ? Theme.WidgetHover
            : Theme.Widget;

        if (!Enabled)
        {
            fill = Theme.Lerp(fill, Theme.Panel, 0.55f);
        }

        var bounds = new RectangleF(0, 0, Width - 1, Height - 1);

        using (var path = SegmentPath(bounds, 3, Segment))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        if (_editor is not null)
        {
            return;
        }

        var color = Enabled ? ForeColor : Theme.TextDisabled;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        if (Label.Length > 0)
        {
            TextRenderer.DrawText(g, Label, Font, new Rectangle(8, 0, Width / 2, Height),
                Enabled ? LabelColor ?? Theme.TextSecondary : Theme.TextDisabled, flags | TextFormatFlags.Left);
        }

        var text = _value.ToString(Format, CultureInfo.InvariantCulture) + Suffix;
        TextRenderer.DrawText(g, text, Font, new Rectangle(0, 0, Width - 8, Height), color,
            flags | (Label.Length > 0 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter));
    }

    private static GraphicsPath SegmentPath(RectangleF r, float radius, int segment)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        var roundTop = segment is -1 or 2;
        var roundBottom = segment is 1 or 2;

        if (roundTop)
        {
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        }
        else
        {
            path.AddLine(r.Left, r.Top, r.Right, r.Top);
        }

        if (roundBottom)
        {
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        }
        else
        {
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        }

        path.CloseFigure();
        return path;
    }
}

/// <summary>Three <see cref="NumberField"/>s stacked as X, Y, Z — one vector property.</summary>
internal sealed class VectorField : StackPanel
{
    public VectorField(string caption, float step, string format = "0.###", string suffix = "")
    {
        BackColor = Theme.Panel;
        Margin = new Padding(0, 2, 0, 4);

        Caption = new SubHeading(caption) { Margin = new Padding(0, 0, 0, 2), Height = 18 };
        Controls.Add(Caption);

        Color[] colors = [Theme.AxisX, Theme.AxisY, Theme.AxisZ];
        string[] names = ["X", "Y", "Z"];

        Fields = new NumberField[3];

        for (var i = 0; i < 3; i++)
        {
            Fields[i] = new NumberField
            {
                Label = names[i],
                LabelColor = colors[i],
                Step = step,
                Format = format,
                Suffix = suffix,
                Segment = i switch { 0 => -1, 2 => 1, _ => 0 },
            };

            Controls.Add(Fields[i]);
        }
    }

    public SubHeading Caption { get; }

    public NumberField[] Fields { get; }

    public void CancelTyping()
    {
        foreach (var field in Fields)
        {
            field.CancelTyping();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Numerics.Vector3 Value
    {
        get => new(Fields[0].Value, Fields[1].Value, Fields[2].Value);
        set
        {
            if (Fields.Any(f => f.IsEditing))
            {
                // Don't fight the person dragging: the field already shows what they set.
                return;
            }

            Fields[0].Value = value.X;
            Fields[1].Value = value.Y;
            Fields[2].Value = value.Z;
        }
    }
}
