using SoftEngine.WinForms.Helpers;
using System.ComponentModel;

namespace SoftEngine.WinForms.Controls;

public sealed partial class SliderIn : Control 
{
    private const float Inc = 0.1f;

    private float _xHit;
    private float _xValueHit;

    private float _yHit;
    private float yValueHit;

    private float _value = 0;
    private float _pixelStep = 1;

    public event EventHandler? ValueChanged;
    public event EventHandler? PixelStepChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Orientation Orientation { get; set; }

    public SliderIn() 
    {
        InitializeComponent();
        DoubleBuffered = true;

        Orientation = Orientation.Horizontal;

        Paint += SuperSlider_Paint;

        MouseDown += SuperSlider_MouseDown;
        MouseMove += SuperSlider_MouseMove;
        MouseWheel += SuperSlider_MouseWheel;

        Layout += SuperSlider_Layout;
    }

    private void SuperSlider_Layout(object? sender, LayoutEventArgs e) 
    {
        Invalidate();
    }

    private void SuperSlider_MouseWheel(object? sender, MouseEventArgs e) 
    {
        float v = PixelStep;
        if (e.Delta > 0)
        {
            v += Inc;
        }
        else if (PixelStep > Inc)
        {
            v -= Inc;
        }

        PixelStep = (float)Math.Round(v, 2);

        Invalidate();
    }

    private void SuperSlider_MouseMove(object? sender, MouseEventArgs e) 
    {
        if (e.Button == MouseButtons.Left) 
        {
            switch (Orientation) 
            {
                case Orientation.Horizontal:
                    Value = Math.Min(Max, Math.Max(Min, _xValueHit - ((e.X - _xHit) * PixelStep)));
                    break;
                case Orientation.Vertical:
                    Value = Math.Min(Max, Math.Max(Min, yValueHit - ((e.Y - _yHit) * PixelStep)));
                    break;
                default:
                    break;
            };
        }
    }

    private void SuperSlider_MouseDown(object? sender, MouseEventArgs e) 
    {
        switch (Orientation) 
        {
            case Orientation.Horizontal:
                _xHit = e.X;
                _xValueHit = Value;
                break;
            case Orientation.Vertical:
                _yHit = e.Y;
                yValueHit = Value;
                break;
            default:
                break;
        }
    }

    private void DrawIndexH(float x, Graphics g) 
    {
        g.DrawLine(Pens.Red, new PointF(x - 1, 0), new PointF(x - 1, Height));
        g.DrawLine(Pens.Red, new PointF(x + 1, 0), new PointF(x + 1, Height));
    }

    private void DrawIndexV(float y, Graphics g)
    {
        g.DrawLine(Pens.Red, new PointF(0, y - 1), new PointF(Width, y - 1));
        g.DrawLine(Pens.Red, new PointF(0, y + 1), new PointF(Width, y + 1));
    }

    private float Transform(float v) 
    {
        return Orientation switch
        {
            Orientation.Horizontal => Width / 2f + v / PixelStep - Value / PixelStep,
            Orientation.Vertical => Height / 2f + v / PixelStep - Value / PixelStep,
            _ => throw new Exception(),
        };
    }

    private void SuperSlider_Paint(object? sender, PaintEventArgs e) 
    {
        var g = e.Graphics;
        using var f = new Font(Font.FontFamily, 7f);

        switch(Orientation) 
        {
            case Orientation.Horizontal:
                for (float i = Min; i <= Max; i += TickEvery)
                {
                    var x = Transform(i);
                    g.DrawLine(Pens.DarkGray, new PointF(x, 0), new PointF(x, 3));
                }

                using (var format = new StringFormat { Alignment = StringAlignment.Center })
                {
                    for (float i = Min; i <= Max; i += NumberEvery)
                    {
                        var label = $"{i}";
                        var x = Transform(i);
                        g.DrawString(label, f, Brushes.Gray, x, 12, format);
                    }
                }
                DrawIndexH(Transform(Value), g);
                break;

            case Orientation.Vertical:
                for (float i = Min; i <= Max; i += TickEvery)
                {
                    var y = Transform(i);
                    g.DrawLine(Pens.DarkGray, new PointF(0, y), new PointF(5, y));
                }

                using (var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    for (float i = Min; i <= Max; i += NumberEvery)
                    {
                        var label = $"{i}";
                        var y = Transform(i);
                        g.DrawString(label, f, Brushes.Gray, 12, y - 1, format);
                    }
                }

                DrawIndexV(Transform(Value), g);
                break;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float TickEvery { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float NumberEvery { get; set; } = 20;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Value 
    {
        get
        {
            return _value;
        }
        set
        {
            if (PropertyChangedHelper.ChangeValue(ref _value, value))
            {
                ValueChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float PixelStep
    {
        get
        {
            return _pixelStep;
        }
        set
        {
            if (PropertyChangedHelper.ChangeValue(ref _pixelStep, value))
            {
                PixelStepChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Min { get; set; } = -180;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Max { get; set; } = 180;
}
