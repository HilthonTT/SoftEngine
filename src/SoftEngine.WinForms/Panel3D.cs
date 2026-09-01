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

    private const float ZoomPerNotch = 1.15f;

    private const float MinCameraDistance = 0.001f;

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
        Scene?.Camera is { } camera ? MathF.Max(MinCameraDistance, MathF.Abs(camera.Position.Z)) : 1f;

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

        var distance = MathF.Max(MinCameraDistance, CameraDistance * MathF.Pow(ZoomPerNotch, -notches));
        var position = camera.Position;

        camera.Position = ClampToPivot(new Vector3(position.X, position.Y, MathF.CopySign(distance, position.Z)), position);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private static Vector3 ClampToPivot(Vector3 position, Vector3 previous)
    {
        var side = previous.Z > 0f ? 1f : -1f;

        return position.Z * side >= MinCameraDistance
            ? position
            : new Vector3(position.X, position.Y, side * MinCameraDistance);
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
        var hit = pixel is { } p && Scene is { Surface.Width: > 0, World: not null }
            ? ScenePicker.Pick(
                Scene,
                p.X * _superSampling + _superSampling / 2,
                p.Y * _superSampling + _superSampling / 2)
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
            Diagnostics.SetProbe(
                probe.X * _superSampling + _superSampling / 2,
                probe.Y * _superSampling + _superSampling / 2);
        }
        else
        {
            Diagnostics.ClearProbe();
        }
    }

    public void ClearSelectedPixel() => SelectPixel(null);

    private static Point ToBufferPixel(Point client) => client;

    #endregion

    #region Input

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E => true,
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        Keys.X or Keys.Y or Keys.Z or Keys.G or Keys.Delete => true,
        Keys.NumPad0 or Keys.NumPad1 or Keys.NumPad2 or Keys.NumPad3 or Keys.NumPad4 => true,
        Keys.NumPad5 or Keys.NumPad6 or Keys.NumPad7 or Keys.NumPad8 or Keys.NumPad9 => true,
        Keys.Home => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Transform is { IsActive: true })
        {
            HandleTransformKey(e.KeyCode);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            ClearSelectedPixel();
            return;
        }

        if (HandleEditKey(e.KeyCode, e.Shift, e.Control, e.Alt))
        {
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Home)
        {
            ZoomActualSize();
            e.Handled = true;
            return;
        }

        if (HandleViewKey(e.KeyCode, e.Shift, e.Control))
        {
            e.Handled = true;
            return;
        }

        if (IsMovementKey(e.KeyCode) && _heldKeys.Add(e.KeyCode))
        {
            _moveTimer.Enabled = true;
            e.Handled = true;
        }
    }

    private bool HandleEditKey(Keys key, bool shift, bool control, bool alt)
    {
        if (control || alt)
        {
            return false;
        }

        if (key == Keys.A && shift)
        {
            RequestAdd();
            return true;
        }

        if (shift || Picked?.Mesh is not { } mesh || Scene is null)
        {
            return false;
        }

        switch (key)
        {
            case Keys.G:
                return BeginTransform(mesh, GizmoMode.Translate);

            case Keys.S:
                return BeginTransform(mesh, GizmoMode.Scale);

            case Keys.X:
            case Keys.Delete:
                DeleteRequested?.Invoke(this, mesh);
                return true;

            default:
                return false;
        }
    }

    public event EventHandler<Point>? AddRequested;

    public event EventHandler<IMesh>? DeleteRequested;

    private void RequestAdd()
    {
        _heldKeys.Clear();
        _moveTimer.Enabled = false;

        var location = PointToClient(Cursor.Position);

        if (!ClientRectangle.Contains(location))
        {
            location = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        }

        AddRequested?.Invoke(this, PointToScreen(location));
    }

    #region Modal transform

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModalTransform? Transform { get; set; }

    private bool BeginTransform(IMesh mesh, GizmoMode mode)
    {
        if (Transform is not { } transform || Scene is not { } scene)
        {
            return false;
        }

        var pixel = CursorSamplePixel();

        if (!transform.Begin(scene, mesh, mode, pixel.X, pixel.Y))
        {
            return false;
        }

        SuspendCameraGestures(true);

        _heldKeys.Clear();
        _moveTimer.Enabled = false;

        GizmoChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();

        return true;
    }

    private void HandleTransformKey(Keys key)
    {
        if (Transform is not { IsActive: true } transform)
        {
            return;
        }

        switch (key)
        {
            case Keys.Escape:
                transform.Cancel();
                EndTransform();
                return;

            case Keys.Return:
            case Keys.Space:
                History?.Push(transform.Confirm());
                EndTransform();
                return;

            case Keys.X:
            case Keys.Y:
            case Keys.Z:
                transform.Constrain(key switch
                {
                    Keys.X => GizmoAxis.X,
                    Keys.Y => GizmoAxis.Y,
                    _ => GizmoAxis.Z,
                });

                UpdateTransform();
                return;

            default:
                return;
        }
    }

    private void UpdateTransform()
    {
        if (Transform is not { IsActive: true } transform || Scene is not { } scene)
        {
            return;
        }

        var pixel = CursorSamplePixel();
        transform.Update(scene, pixel.X, pixel.Y);

        GizmoChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void CancelTransform()
    {
        if (Transform is not { IsActive: true } transform)
        {
            return;
        }

        transform.Cancel();
        EndTransform();
    }

    private void EndTransform()
    {
        SuspendCameraGestures(false);

        GizmoChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private Point CursorSamplePixel()
    {
        var location = PointToClient(Cursor.Position);

        if (!ClientRectangle.Contains(location))
        {
            location = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        }

        return ToSamplePixel(location);
    }

    #endregion

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

        if (Transform is { IsActive: true } transform)
        {
            transform.Cancel();
            EndTransform();
        }

        _heldKeys.Clear();
        _moveTimer.Enabled = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        Focus();
        _mouseDownAt = e.Location;
        _mouseDragged = false;

        if (Transform is { IsActive: true } running)
        {
            if (e.Button == MouseButtons.Left)
            {
                History?.Push(running.Confirm());
            }
            else
            {
                running.Cancel();
            }

            _endedTransform = true;

            EndTransform();
            return;
        }

        if (e.Button == MouseButtons.Left && Gizmo is { IsActive: true } gizmo && Scene is { } scene)
        {
            var pixel = ToSamplePixel(e.Location);

            if (gizmo.Begin(scene, pixel.X, pixel.Y))
            {
                SuspendCameraGestures(true);
                Invalidate();
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (Math.Abs(e.X - _mouseDownAt.X) > 3 || Math.Abs(e.Y - _mouseDownAt.Y) > 3)
        {
            _mouseDragged = true;
        }

        if (Transform is { IsActive: true })
        {
            UpdateTransform();
            return;
        }

        if (Gizmo is not { IsActive: true } gizmo || Scene is not { } scene)
        {
            return;
        }

        var pixel = ToSamplePixel(e.Location);

        if (gizmo.IsDragging)
        {
            gizmo.Drag(scene, pixel.X, pixel.Y);
            GizmoChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        var before = gizmo.HoveredAxis;
        gizmo.Hover(scene, pixel.X, pixel.Y);

        if (gizmo.HoveredAxis != before)
        {
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_endedTransform)
        {
            _endedTransform = false;
            return;
        }

        if (e.Button == MouseButtons.Left && Gizmo is { IsDragging: true } gizmo)
        {
            History?.Push(gizmo.End());

            SuspendCameraGestures(false);

            GizmoChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Left && !_mouseDragged)
        {
            SelectPixel(ToBufferPixel(e.Location));
        }
    }

    private Point ToSamplePixel(Point client)
    {
        var pixel = ToBufferPixel(client);

        return new Point(
            pixel.X * _superSampling + _superSampling / 2,
            pixel.Y * _superSampling + _superSampling / 2);
    }

    private void SuspendCameraGestures(bool suspended)
    {
        if (ArcBall is { } camera)
        {
            camera.GesturesSuspended = suspended;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }

        _wheelDelta += e.Delta;

        var notch = SpeedModifier();

        while (_wheelDelta >= WheelNotch)
        {
            _wheelDelta -= WheelNotch;
            Dolly(notch);
        }

        while (_wheelDelta <= -WheelNotch)
        {
            _wheelDelta += WheelNotch;
            Dolly(-notch);
        }
    }

    private static float SpeedModifier()
    {
        var speed = 1f;

        if (ModifierKeys.HasFlag(Keys.Shift))
        {
            speed *= 4f;
        }

        if (ModifierKeys.HasFlag(Keys.Control))
        {
            speed *= 0.25f;
        }

        return speed;
    }

    private static bool IsMovementKey(Keys key) => key switch
    {
        Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E => true,
        Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
        _ => false,
    };

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

        var speed = MathF.Max(0.02f, CameraDistance * 0.015f) * SpeedModifier();

        var position = camera.Position;

        camera.Position = ClampToPivot(position + (Vector3.Normalize(direction) * speed), position);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    #endregion

    #region View orientation

    public const float RotationStep = 15f * MathF.PI / 180f;

    private ArcBallCamera? ArcBall => Scene?.Camera as ArcBallCamera;

    public void LookAlong(AxisView view)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.LookAlong(view);
        Invalidate();
    }

    public void FlipView()
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.FlipView();
        Invalidate();
    }

    public void RotateAroundWorldAxis(Vector3 axis, float radians)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.RotateAroundWorldAxis(axis, radians);
        Invalidate();
    }

    public void RotateInView(Vector3 axis, float radians)
    {
        if (ArcBall is not { } camera)
        {
            return;
        }

        camera.RotateInView(axis, radians);
        Invalidate();
    }

    private bool HandleViewKey(Keys key, bool shift, bool control)
    {
        if (ArcBall is null)
        {
            return false;
        }

        switch (key)
        {
            case Keys.NumPad1: LookAlong(control ? AxisView.Back : AxisView.Front); return true;
            case Keys.NumPad3: LookAlong(control ? AxisView.Left : AxisView.Right); return true;
            case Keys.NumPad7: LookAlong(control ? AxisView.Bottom : AxisView.Top); return true;
            case Keys.NumPad9: FlipView(); return true;

            case Keys.NumPad4: RotateAroundWorldAxis(Vector3.UnitY, RotationStep); return true;
            case Keys.NumPad6: RotateAroundWorldAxis(Vector3.UnitY, -RotationStep); return true;
            case Keys.NumPad8: RotateInView(Vector3.UnitX, RotationStep); return true;
            case Keys.NumPad2: RotateInView(Vector3.UnitX, -RotationStep); return true;
        }

        if (control)
        {
            return false;
        }

        var step = shift ? -RotationStep : RotationStep;

        switch (key)
        {
            case Keys.X: RotateAroundWorldAxis(Vector3.UnitX, step); return true;
            case Keys.Y: RotateAroundWorldAxis(Vector3.UnitY, step); return true;
            case Keys.Z: RotateAroundWorldAxis(Vector3.UnitZ, step); return true;
            default: return false;
        }
    }

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
