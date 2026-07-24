using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.WinForms.Interop;
using SoftEngine.WinForms.Utilities;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Text;

namespace SoftEngine.WinForms;

public partial class Panel3D : UserControl
{
    private const string Format = "Volumes:{0}\nTriangles:{1} - Back:{2} - Out:{3} - Behind:{4} - Clipped:{10}\nPixels:{9} drawn:{5} - Z behind:{6}\nCalc time:{7} - Paint time:{8}";

    private const float MoveInterval = 16f;

    /// <summary>One notch of a standard mouse wheel.</summary>
    private const int WheelNotch = 120;

    private readonly StringBuilder StatDisplay;
    private readonly System.Windows.Forms.Timer _moveTimer;
    private readonly HashSet<Keys> _heldKeys = [];

    private Size _bufferSize;
    private Bitmap? bmp;
    private float _referenceDistance = 1f;
    private int _wheelDelta;
    private Point? _selectedPixel;
    private Point _mouseDownAt;
    private bool _mouseDragged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Scene? Scene { get; set; }

    public RenderStats Stats { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RendererSettings RendererSettings
    {
        get => Renderer.Settings;
        set => Renderer.Settings = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IPainter? Painter { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IRenderer Renderer { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RenderDiagnostics Diagnostics => Renderer.Diagnostics;

    /// <summary>Draws the per-frame counters over the top-left of the viewport.</summary>
    [DefaultValue(true)]
    public bool ShowStatsOverlay { get; set; } = true;

    /// <summary>Raised after every rendered frame, on the UI thread.</summary>
    public event EventHandler? FrameRendered;

    public event EventHandler? ZoomChanged;

    public event EventHandler? SelectedPixelChanged;

    public Panel3D()
    {
        InitializeComponent();

        Renderer = new Renderer { Settings = new RendererSettings { BackFaceCulling = true } };

        Stats = Renderer.Stats;
        Painter = new GouraudPainter();

        ResizeRedraw = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;

        StatDisplay = new StringBuilder();

        // A timer rather than key-repeat: held keys must move the camera smoothly, and
        // the auto-repeat rate is a user setting we shouldn't inherit.
        _moveTimer = new System.Windows.Forms.Timer { Interval = (int)MoveInterval };
        _moveTimer.Tick += MoveCamera;

        Paint += Panel3D_Paint;
    }

    #region Zoom

    /// <summary>
    /// The camera distance that reads as 100% — the framing a world is loaded with.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float ReferenceDistance
    {
        get => _referenceDistance;
        set
        {
            _referenceDistance = MathF.Max(0.0001f, value);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// How much closer the camera is than the world's default framing: 100% is the view a
    /// world loads with, 200% is half the distance to it. Purely a readout — the scene is
    /// always rendered at one framebuffer pixel per screen pixel.
    /// </summary>
    public float Zoom => _referenceDistance / MathF.Max(0.0001f, CameraDistance);

    private float CameraDistance => Scene?.Camera is { } camera ? camera.Position.Length() : 1f;

    public void ZoomIn() => Dolly(1);

    public void ZoomOut() => Dolly(-1);

    /// <summary>Puts the camera back at the distance the current world was framed from.</summary>
    public void ZoomActualSize()
    {
        if (Scene?.Camera is not { } camera)
        {
            return;
        }

        camera.Position = new Vector3(0, 0, -_referenceDistance);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>
    /// Moves the camera along its view axis by a fraction of its current distance, so one
    /// notch covers as much ground on a 1500-unit elephant as on a 5-unit skull.
    /// </summary>
    private void Dolly(int notches)
    {
        if (Scene?.Camera is not { } camera)
        {
            return;
        }

        var step = MathF.Max(0.05f, CameraDistance * 0.1f) * notches;

        camera.Position += new Vector3(0, 0, step);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    #endregion

    #region Pixel selection

    /// <summary>The probed pixel, in render-target coordinates, or null when none is selected.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? SelectedPixel => _selectedPixel;

    /// <summary>The selected pixel as a 0..1 fraction of the render target, as the status bar shows it.</summary>
    public PointF? SelectedPixelNormalized =>
        _selectedPixel is { } pixel && _bufferSize.Width > 0 && _bufferSize.Height > 0
            ? new PointF(pixel.X / (float)_bufferSize.Width, pixel.Y / (float)_bufferSize.Height)
            : null;

    /// <summary>Size of the render target, which follows the size of the control.</summary>
    public Size BufferSize => _bufferSize;

    public void SelectPixel(Point? pixel)
    {
        if (pixel is { } p && (p.X < 0 || p.Y < 0 || p.X >= _bufferSize.Width || p.Y >= _bufferSize.Height))
        {
            pixel = null;
        }

        if (pixel == _selectedPixel)
        {
            return;
        }

        _selectedPixel = pixel;

        if (pixel is { } probe)
        {
            Diagnostics.SetProbe(probe.X, probe.Y);
        }
        else
        {
            Diagnostics.ClearProbe();
        }

        SelectedPixelChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void ClearSelectedPixel() => SelectPixel(null);

    /// <summary>Maps a point in the control to the render-target pixel drawn under it.</summary>
    private static Point ToBufferPixel(Point client) => client;

    #endregion

    #region Input

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E => true,
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape)
        {
            ClearSelectedPixel();
            return;
        }

        if (IsMovementKey(e.KeyCode) && _heldKeys.Add(e.KeyCode))
        {
            _moveTimer.Enabled = true;
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (_heldKeys.Remove(e.KeyCode) && _heldKeys.Count == 0)
        {
            _moveTimer.Enabled = false;
        }
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        _heldKeys.Clear();
        _moveTimer.Enabled = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        Focus();
        _mouseDownAt = e.Location;
        _mouseDragged = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (Math.Abs(e.X - _mouseDownAt.X) > 3 || Math.Abs(e.Y - _mouseDownAt.Y) > 3)
        {
            _mouseDragged = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // A left click that didn't orbit the camera picks the pixel under the cursor.
        if (e.Button == MouseButtons.Left && !_mouseDragged)
        {
            SelectPixel(ToBufferPixel(e.Location));
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        // Keep the notch out of the parent chain, so the viewport is the only thing it drives.
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }

        // Accumulate, so a high-resolution wheel that reports fractions of a notch dollies
        // once per notch rather than once per message.
        _wheelDelta += e.Delta;

        while (_wheelDelta >= WheelNotch)
        {
            _wheelDelta -= WheelNotch;
            ZoomIn();
        }

        while (_wheelDelta <= -WheelNotch)
        {
            _wheelDelta += WheelNotch;
            ZoomOut();
        }
    }

    private static bool IsMovementKey(Keys key) => key switch
    {
        Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E => true,
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        _ => false,
    };

    /// <summary>
    /// Flies the camera with WASD (+ Q/E for down/up). The view matrix translates by the
    /// camera position after rotating, so the position is already expressed along the
    /// camera's own axes — moving the camera one way shifts the world the other.
    /// </summary>
    private void MoveCamera(object? sender, EventArgs e)
    {
        if (Scene?.Camera is not { } camera || _heldKeys.Count == 0)
        {
            return;
        }

        var direction = Vector3.Zero;

        if (_heldKeys.Contains(Keys.W) || _heldKeys.Contains(Keys.Up)) { direction.Z += 1f; }
        if (_heldKeys.Contains(Keys.S) || _heldKeys.Contains(Keys.Down)) { direction.Z -= 1f; }
        if (_heldKeys.Contains(Keys.A) || _heldKeys.Contains(Keys.Left)) { direction.X += 1f; }
        if (_heldKeys.Contains(Keys.D) || _heldKeys.Contains(Keys.Right)) { direction.X -= 1f; }
        if (_heldKeys.Contains(Keys.E)) { direction.Y -= 1f; }
        if (_heldKeys.Contains(Keys.Q)) { direction.Y += 1f; }

        if (direction == Vector3.Zero)
        {
            return;
        }

        // Step with the scale of the scene: models here range from a 2-unit skull to a
        // 1500-unit elephant, and a fixed step would be useless for one of them.
        var speed = MathF.Max(0.02f, camera.Position.Length() * 0.015f);

        if (ModifierKeys.HasFlag(Keys.Shift))
        {
            speed *= 4f;
        }
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            speed *= 0.25f;
        }

        camera.Position += Vector3.Normalize(direction) * speed;
        Invalidate();
    }

    #endregion

    /// <summary>
    /// Saves the last rendered frame as a PNG. The capture is the bare render target:
    /// the stats overlay and the pixel-selection marker are drawn over the control, not
    /// into the framebuffer, so they never appear in the file. Returns false when no
    /// frame has been rendered yet.
    /// </summary>
    public bool SaveScreenshot(string path)
    {
        if (Scene?.Surface is not { } surface || surface.Width <= 0 || surface.Height <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var screen = surface.Screen;
        var pixels = new uint[surface.Width * surface.Height];

        // The framebuffer is packed ARGB (blue in the low byte); PngWriter wants packed
        // RGBA (red in the low byte). Swap R and B, and force alpha opaque — cleared
        // background pixels are 0x00000000, which would otherwise save as transparent.
        for (var i = 0; i < pixels.Length; i++)
        {
            var argb = (uint)screen[i];
            pixels[i] = 0xFF000000u
                | ((argb & 0x00FF0000) >> 16)  // R: bits 16-23 -> 0-7
                | (argb & 0x0000FF00)          // G stays in bits 8-15
                | ((argb & 0x000000FF) << 16); // B: bits 0-7 -> 16-23
        }

        PngWriter.Save(path, pixels, surface.Width, surface.Height);
        return true;
    }

    private void Panel3D_Paint(object? sender, PaintEventArgs e)
    {
        if (Scene is null)
        {
            return;
        }

        var bufferSize = new Size(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));

        if (bufferSize != _bufferSize)
        {
            Scene.Surface = new FrameBuffer(bufferSize.Width, bufferSize.Height) { Stats = Stats };

            bmp?.Dispose();
            bmp = new Bitmap(bufferSize.Width, bufferSize.Height, PixelFormat.Format32bppPArgb);

            _bufferSize = bufferSize;

            // The selection is in render-target space, which just changed under it.
            if (_selectedPixel is { } pixel &&
                (pixel.X >= bufferSize.Width || pixel.Y >= bufferSize.Height))
            {
                SelectPixel(null);
            }
        }

        if (bmp is null)
        {
            return;
        }

        var g = e.Graphics;

        Renderer.Render(Scene, Painter);
        BitmapBlitter.FillBitmap(bmp, Scene.Surface.Screen);

        g.DrawImage(bmp, Point.Empty);

        DrawSelectionMarker(g);

        if (ShowStatsOverlay)
        {
            DrawStats(g);
        }

        FrameRendered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Releases the render bitmap and the movement timer; called from the designer's Dispose.</summary>
    private void DisposeRenderResources()
    {
        _moveTimer.Dispose();
        bmp?.Dispose();
        bmp = null;
    }

    private void DrawSelectionMarker(Graphics g)
    {
        if (_selectedPixel is not { } pixel)
        {
            return;
        }

        // A single pixel is too small to see, so the marker is a box drawn around it.
        var box = new Rectangle(pixel.X - 3, pixel.Y - 3, 7, 7);

        g.SmoothingMode = SmoothingMode.None;

        using var outer = new Pen(Color.Black);
        using var inner = new Pen(Theme.Accent);

        g.DrawRectangle(outer, box);
        box.Inflate(1, 1);
        g.DrawRectangle(inner, box);
    }

    private void DrawStats(Graphics g)
    {
        StatDisplay.Clear();
        StatDisplay.Append($"Lights:{Scene!.World.Lights.Count}\n");
        StatDisplay.AppendFormat(Format,
            Scene.World.Meshes.Count,
            Stats.TotalTriangleCount,
            Stats.FacingBackTriangleCount,
            Stats.OutOfViewTriangleCount,
            Stats.BehindViewTriangleCount,
            Stats.DrawnPixelCount,
            Stats.BehindZPixelCount,
            Stats.CalculationTimeMs,
            Stats.PainterTimeMs,
            Stats.DrawnPixelCount + Stats.BehindZPixelCount,
            Stats.NearClippedTriangleCount
        );

        TextRenderer.DrawText(g, StatDisplay.ToString(), Font, new Point(10, 8), Theme.TextSecondary, TextFormatFlags.ExpandTabs);
    }
}
