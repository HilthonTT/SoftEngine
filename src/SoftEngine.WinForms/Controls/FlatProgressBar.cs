using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Controls;

/// <summary>
/// A slim, flat, rounded progress bar. The system <see cref="ProgressBar"/> ignores custom
/// colors when visual styles are active and animates value changes, so it can't match the
/// dark theme nor keep up with fast progress reports.
/// </summary>
public sealed class FlatProgressBar : Control
{
    private int _maximum = 100;
    private int _value;

    public FlatProgressBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        Height = 6;
    }

    [DefaultValue(100)]
    public int Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(1, value); Invalidate(); }
    }

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, _maximum);
            if (clamped != _value)
            {
                _value = clamped;
                Invalidate();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TrackColor { get; set; } = Theme.Selection;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BarColor { get; set; } = Theme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var radius = Math.Max(1, Height / 2);

        using (var track = new SolidBrush(TrackColor))
        using (var trackPath = Theme.RoundedRect(ClientRectangle, radius))
        {
            g.FillPath(track, trackPath);
        }

        var barWidth = (int)(ClientRectangle.Width * (_value / (float)_maximum));
        if (barWidth >= Height)
        {
            using var bar = new SolidBrush(BarColor);
            using var barPath = Theme.RoundedRect(new Rectangle(0, 0, barWidth, Height), radius);
            g.FillPath(bar, barPath);
        }
    }
}
