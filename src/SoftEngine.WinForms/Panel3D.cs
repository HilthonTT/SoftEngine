using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Tracing;
using SoftEngine.Gpu;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Interop;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Text;

namespace SoftEngine.WinForms;

public partial class Panel3D : UserControl
{
    private const string Format = "Volumes:{0} - Hidden:{11} behind {12} occluder(s)\nTriangles:{1} - Back:{2} - Out:{3} - Behind:{4} - Clipped:{10}\nPixels:{9} drawn:{5} - Z behind:{6}\nCalc time:{7} - Paint time:{8}";

    private const float MoveInterval = 16f;

    private const float AnimationInterval = 16f;

    private const int WheelNotch = 120;


    private readonly StringBuilder StatDisplay;
    private readonly System.Windows.Forms.Timer _moveTimer;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly HashSet<Keys> _heldKeys = [];

    private readonly Stopwatch _animationClock = new();
    private TimeSpan _lastAnimationTick;

    private Size _bufferSize;
    private int _superSampling = 1;

    private bool _renderTargetStale;

    private int[] _resolved = [];

    private Bitmap? bmp;
    private float _referenceDistance = 1f;
    private int _wheelDelta;
    private Point? _selectedPixel;
    private Point _mouseDownAt;
    private bool _mouseDragged;

    private bool _endedTransform;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Scene? Scene { get; set; }

    public RenderStats Stats => Renderer.Stats;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RendererSettings RendererSettings
    {
        get => Renderer.Settings;
        set => Renderer.Settings = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IPainter? Painter { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PostProcessStack PostProcess { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IRenderer Renderer { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RenderDiagnostics Diagnostics => Renderer.Diagnostics;

    #region Backend

    private RenderBackend _backend = RenderBackend.Cpu;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RenderBackend Backend
    {
        get => _backend;
        set => SetBackend(value);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GpuAdapter? Adapter { get; private set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? BackendFallback { get; private set; }

    public string BackendDescription => _backend switch
    {
        RenderBackend.Gpu when Adapter is { } adapter => $"GPU · {adapter.Describe()}",
        RenderBackend.Trace when Renderer is PathTracer tracer =>
            $"CPU · path tracer · {tracer.AccumulatedSamples} spp",
        _ => "CPU · software rasterizer",
    };

    [DefaultValue(512)]
    public int TraceSampleTarget { get; set; } = 512;

    [DefaultValue(64)]
    public int TraceDenoiseUntil { get; set; } = 64;

    public event EventHandler? BackendChanged;

    private void SetBackend(RenderBackend requested)
    {
        if (requested == _backend && BackendFallback is null)
        {
            return;
        }

        var result = RenderBackends.Create(requested);

        if (result.Renderer is PathTracer tracer)
        {
            tracer.Trace.SamplesPerPixel = 2;
            tracer.Trace.MaxBounces = 2;
            tracer.Trace.Accumulate = true;
            tracer.Trace.Denoise = true;
        }

        var previous = Renderer;

        result.Renderer.Settings = previous.Settings;
        result.Renderer.PostProcess = previous.PostProcess;

        result.Renderer.Diagnostics.CaptureEvents = previous.Diagnostics.CaptureEvents;
        result.Renderer.Diagnostics.HistoryCapacity = previous.Diagnostics.HistoryCapacity;

        if (previous.Diagnostics.IsProbing)
        {
            result.Renderer.Diagnostics.SetProbe(previous.Diagnostics.ProbeX, previous.Diagnostics.ProbeY);
        }

        Renderer = result.Renderer;
        _backend = result.Backend;
        Adapter = result.Adapter;
        BackendFallback = result.Fallback;

        (previous as IDisposable)?.Dispose();

        _renderTargetStale = true;

        BackendChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    #endregion

    [DefaultValue(true)]
    public bool ShowStatsOverlay { get; set; } = true;

    [DefaultValue(1)]
    public int SuperSampling
    {
        get => _superSampling;
        set
        {
            var factor = SuperSampler.ClampFactor(value);

            if (factor == _superSampling)
            {
                return;
            }

            _superSampling = factor;
            _renderTargetStale = true;

            ApplyProbe();
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool Animate
    {
        get => _animate;
        set
        {
            if (_animate == value)
            {
                return;
            }

            _animate = value;
            SyncAnimationTimer();
        }
    }

    private bool _animate = true;

    public void SyncAnimationTimer()
    {
        var run = _animate && Scene?.World is { IsAnimated: true };

        if (run == _animationTimer.Enabled)
        {
            return;
        }

        if (run)
        {
            _animationClock.Restart();
            _lastAnimationTick = TimeSpan.Zero;
        }
        else
        {
            _animationClock.Stop();
        }

        _animationTimer.Enabled = run;
    }

    private void AdvanceAnimation(object? sender, EventArgs e)
    {
        if (Scene?.World is not { } world)
        {
            return;
        }

        var now = _animationClock.Elapsed;
        var delta = (float)(now - _lastAnimationTick).TotalSeconds;
        _lastAnimationTick = now;

        world.Update(MathF.Min(delta, 0.25f));

        Invalidate();
    }

    public event EventHandler? FrameRendered;

    public event EventHandler? ZoomChanged;

    public event EventHandler? SelectedPixelChanged;

    public Panel3D()
    {
        InitializeComponent();

        Renderer = new Renderer { Settings = new RendererSettings { BackFaceCulling = true } };

        PostProcess = PostProcessStack.CreateDefault();
        Renderer.PostProcess = PostProcess;

        Painter = new GouraudPainter();

        ResizeRedraw = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;

        StatDisplay = new StringBuilder();

        _moveTimer = new System.Windows.Forms.Timer { Interval = (int)MoveInterval };
        _moveTimer.Tick += MoveCamera;

        _animationTimer = new System.Windows.Forms.Timer { Interval = (int)AnimationInterval };
        _animationTimer.Tick += AdvanceAnimation;

        Paint += Panel3D_Paint;
    }

    #region Zoom

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

    public float Zoom => _referenceDistance / MathF.Max(0.0001f, CameraDistance);

    private float CameraDistance =>
        Scene?.Camera is { } camera ? MathF.Max(CameraMotion.MinCameraDistance, MathF.Abs(camera.Position.Z)) : 1f;

    public void ZoomIn() => Dolly(1f);

    public void ZoomOut() => Dolly(-1f);

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

    private void Dolly(float notches)
    {
        if (Scene?.Camera is not { } camera)
        {
            return;
        }

        var distance = CameraMotion.DollyDistance(CameraDistance, notches);
        var position = camera.Position;

        camera.Position = CameraMotion.ClampToPivot(new Vector3(position.X, position.Y, MathF.CopySign(distance, position.Z)), position);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }


    #endregion

    #region Pixel selection

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? SelectedPixel => _selectedPixel;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PickHit? Picked { get; private set; }

    public event EventHandler? PickedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TransformGizmo? Gizmo
    {
        get => RendererSettings.Gizmo;
        set => RendererSettings.Gizmo = value;
    }

    public event EventHandler? GizmoChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public EditHistory? History { get; set; }

    public PointF? SelectedPixelNormalized =>
        _selectedPixel is { } pixel && _bufferSize.Width > 0 && _bufferSize.Height > 0
            ? new PointF(pixel.X / (float)_bufferSize.Width, pixel.Y / (float)_bufferSize.Height)
            : null;

    public Size BufferSize => _bufferSize;

    public void SelectPixel(Point? pixel)
    {
        if (pixel is { } p && (p.X < 0 || p.Y < 0 || p.X >= _bufferSize.Width || p.Y >= _bufferSize.Height))
        {
            pixel = null;
        }

        UpdatePick(pixel);

        if (pixel == _selectedPixel)
        {
            return;
        }

        _selectedPixel = pixel;

        ApplyProbe();

        SelectedPixelChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void UpdatePick(Point? pixel)
    {
        var hit = pixel is { } p && Scene is { Surface.Width: > 0, World: not null } && SampleOf(p) is var sample
            ? ScenePicker.Pick(Scene, sample.X, sample.Y)
            : null;

        var changed = hit?.MeshIndex != Picked?.MeshIndex || hit?.TriangleIndex != Picked?.TriangleIndex;

        Picked = hit;
        RendererSettings.HighlightedMesh = hit?.MeshIndex ?? -1;

        if (changed)
        {
            PickedChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public void SelectMesh(IMesh? mesh)
    {
        var index = mesh is not null && Scene?.World is { } world ? world.Meshes.IndexOf(mesh) : -1;

        if (mesh is null || index < 0)
        {
            ClearPick();
            return;
        }

        Picked = new PickHit(mesh, index, -1, 0f, mesh.WorldMatrix.Translation, Vector3.Zero);
        RendererSettings.HighlightedMesh = index;

        PickedChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void ClearPick()
    {
        RendererSettings.HighlightedMesh = -1;

        CancelTransform();

        if (Gizmo is { } gizmo)
        {
            gizmo.Cancel();
            gizmo.Target = null;

            SuspendCameraGestures(false);
        }

        if (Picked is null)
        {
            return;
        }

        Picked = null;
        PickedChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void ApplyProbe()
    {
        if (_selectedPixel is { } probe)
        {
            var sample = SampleOf(probe);
            Diagnostics.SetProbe(sample.X, sample.Y);
        }
        else
        {
            Diagnostics.ClearProbe();
        }
    }

    public void ClearSelectedPixel() => SelectPixel(null);

    private static Point ToBufferPixel(Point client) => client;

    private Point SampleOf(Point pixel) => new(
        pixel.X * _superSampling + _superSampling / 2,
        pixel.Y * _superSampling + _superSampling / 2);

    #endregion

    public bool SaveScreenshot(string path)
    {
        if (Scene?.Surface is not { Width: > 0, Height: > 0 } || _bufferSize.Width <= 0 || _bufferSize.Height <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var width = _bufferSize.Width;
        var height = _bufferSize.Height;

        var screen = PresentablePixels();
        var pixels = new int[width * height];

        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = screen[i] | unchecked((int)0xFF000000);
        }

        PngCodec.Save(path, pixels, width, height);
        return true;
    }

    private void Panel3D_Paint(object? sender, PaintEventArgs e)
    {
        if (Scene is null)
        {
            return;
        }

        var bufferSize = new Size(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));

        if (bufferSize != _bufferSize || _renderTargetStale)
        {
            _renderTargetStale = false;

            Scene.Surface = new FrameBuffer(bufferSize.Width * _superSampling, bufferSize.Height * _superSampling) { Stats = Stats };

            bmp?.Dispose();
            bmp = new Bitmap(bufferSize.Width, bufferSize.Height, PixelFormat.Format32bppPArgb);

            _resolved = _superSampling > 1 ? new int[bufferSize.Width * bufferSize.Height] : [];

            _bufferSize = bufferSize;

            ResetTemporalHistory();

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

        if (Renderer is PathTracer refining)
        {
            refining.Trace.Denoise = refining.AccumulatedSamples < TraceDenoiseUntil;
        }

        Renderer.Render(Scene, Painter);
        BitmapBlitter.FillBitmap(bmp, PresentablePixels());

        g.DrawImage(bmp, Point.Empty);

        DrawSelectionMarker(g);

        if (ShowStatsOverlay)
        {
            DrawStats(g);
        }

        FrameRendered?.Invoke(this, EventArgs.Empty);

        KeepRefining();
    }

    public void ResetTemporalHistory()
    {
        (Renderer as Renderer)?.ResetHistory();
        (Renderer as PathTracer)?.Reset();
    }

    private void KeepRefining()
    {
        if (Renderer is not PathTracer tracer ||
            !tracer.Trace.Accumulate ||
            tracer.AccumulatedSamples >= TraceSampleTarget ||
            !IsHandleCreated ||
            IsDisposed)
        {
            return;
        }

        BeginInvoke(Invalidate);
    }

    private int[] PresentablePixels()
    {
        if (Scene?.Surface is not { } surface)
        {
            return [];
        }

        if (_superSampling <= 1)
        {
            return surface.Screen;
        }

        SuperSampler.Resolve(surface, _resolved, _bufferSize.Width, _bufferSize.Height, _superSampling);
        return _resolved;
    }

    private void DisposeRenderResources()
    {
        _moveTimer.Dispose();
        _animationTimer.Dispose();
        bmp?.Dispose();
        bmp = null;

        (Renderer as IDisposable)?.Dispose();
    }

    private void DrawSelectionMarker(Graphics g)
    {
        if (_selectedPixel is not { } pixel)
        {
            return;
        }

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
            Stats.NearClippedTriangleCount,
            Stats.OccludedMeshCount,
            Stats.OccluderMeshCount
        );

        TextRenderer.DrawText(g, StatDisplay.ToString(), Font, new Point(10, 8), Theme.TextSecondary, TextFormatFlags.ExpandTabs);
    }
}
