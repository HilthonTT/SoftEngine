using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SoftEngine.WinForms.Controls;

/// <summary>
/// The timeline's track: frame numbers along the top, a highlighted span for the clip, and a
/// playhead that follows the animation and can be dragged to scrub it.
/// </summary>
internal sealed class TimelineRuler : Control
{
    public const float FramesPerSecond = 24f;

    private int Inset => LogicalToDeviceUnits(14);

    private float _duration;
    private float _time;
    private bool _scrubbing;

    public TimelineRuler()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Font = Theme.SmallFont;
    }

    /// <summary>Raised while the playhead is dragged, with the time it was dragged to.</summary>
    public event EventHandler<float>? Scrubbed;

    public event EventHandler? ScrubStarted;

    public event EventHandler? ScrubEnded;

    public bool IsScrubbing => _scrubbing;

    /// <summary>The clip's length in seconds; zero when nothing in the scene is animated.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Duration
    {
        get => _duration;
        set
        {
            if (_duration.Equals(value))
            {
                return;
            }

            _duration = MathF.Max(0f, value);
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Time
    {
        get => _time;
        set
        {
            if (_time.Equals(value))
            {
                return;
            }

            _time = value;
            Invalidate();
        }
    }

    private int LastFrame => Math.Max(1, (int)MathF.Ceiling(_duration * FramesPerSecond));

    private float XOf(float frame) => Inset + frame / LastFrame * (Width - 2 * Inset);

    private float TimeAt(int x) =>
        Math.Clamp((x - Inset) / (float)Math.Max(1, Width - 2 * Inset), 0f, 1f) * _duration;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || _duration <= 0f)
        {
            return;
        }

        _scrubbing = true;
        Capture = true;
        ScrubStarted?.Invoke(this, EventArgs.Empty);
        Scrub(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_scrubbing)
        {
            Scrub(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_scrubbing)
        {
            return;
        }

        _scrubbing = false;
        Capture = false;
        ScrubEnded?.Invoke(this, EventArgs.Empty);
    }

    private void Scrub(int x)
    {
        // Snap to whole frames, as a timeline does.
        var frame = MathF.Round(TimeAt(x) * FramesPerSecond);
        Time = MathF.Min(frame / FramesPerSecond, _duration);
        Scrubbed?.Invoke(this, Time);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        var rulerHeight = LogicalToDeviceUnits(20);

        using (var ruler = new SolidBrush(Theme.Background))
        {
            g.FillRectangle(ruler, 0, 0, Width, rulerHeight);
        }

        if (_duration <= 0f)
        {
            TextRenderer.DrawText(g, "Nothing in this scene is animated", Theme.UiFont,
                new Rectangle(0, rulerHeight, Width, Height - rulerHeight), Theme.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // The clip's span, a shade lighter than outside it.
        using (var span = new SolidBrush(Color.FromArgb(52, 52, 52)))
        {
            g.FillRectangle(span, XOf(0), rulerHeight, XOf(LastFrame) - XOf(0), Height - rulerHeight);
        }

        // Pick a label spacing that leaves room for the numbers.
        var pixelsPerFrame = (Width - 2f * Inset) / LastFrame;
        int[] steps = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000];
        var spacing = LogicalToDeviceUnits(40);
        var labelStep = steps.FirstOrDefault(step => step * pixelsPerFrame >= spacing, 1000);

        using var major = new Pen(Color.FromArgb(70, 70, 70));
        using var minor = new Pen(Color.FromArgb(56, 56, 56));

        for (var frame = 0; frame <= LastFrame; frame += Math.Max(1, labelStep / 5))
        {
            var x = XOf(frame);
            var labelled = frame % labelStep == 0;

            g.DrawLine(labelled ? major : minor, x, rulerHeight, x, Height);

            if (labelled)
            {
                TextRenderer.DrawText(g, frame.ToString(System.Globalization.CultureInfo.InvariantCulture), Font,
                    new Rectangle((int)x - 20, 0, 40, rulerHeight), Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // Playhead: a blue line and a badge with the frame number on the ruler.
        var current = _time * FramesPerSecond;
        var px = XOf(current);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var line = new Pen(Theme.Accent, 2f))
        {
            g.DrawLine(line, px, rulerHeight, px, Height);
        }

        var label = ((int)MathF.Round(current)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var size = TextRenderer.MeasureText(label, Theme.UiFont);
        var badge = new RectangleF(px - size.Width / 2f - 5, 1, size.Width + 10, rulerHeight - 2);

        Theme.FillRounded(g, Theme.Accent, badge, 4);
        TextRenderer.DrawText(g, label, Theme.UiFont, Rectangle.Round(badge), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
