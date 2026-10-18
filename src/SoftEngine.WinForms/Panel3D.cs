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

    /// <summary>
    /// How often the animation clock ticks. The renderer will not keep up with 60 Hz on a
    /// dense scene, and that is fine: the clip is advanced by elapsed wall-clock time rather
    /// than per tick, so a slow frame skips ahead instead of playing in slow motion.
    /// </summary>
    private const float AnimationInterval = 16f;

    /// <summary>One notch of a standard mouse wheel.</summary>
    private const int WheelNotch = 120;


    private readonly StringBuilder StatDisplay;
    private readonly System.Windows.Forms.Timer _moveTimer;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly HashSet<Keys> _heldKeys = [];

    // Wall-clock, so playback runs at the clip's authored speed however long a frame takes.
    // A software renderer's frame time swings by an order of magnitude between a 200-triangle
    // scene and a 30k one, and stepping the clip by a fixed amount per tick would make the
    // same animation play at a different speed in each.
    private readonly Stopwatch _animationClock = new();
    private TimeSpan _lastAnimationTick;

    private Size _bufferSize;
    private int _superSampling = 1;

    // Set when the render target has to be rebuilt for a reason the control's size doesn't
    // show — a change of sample count, which resizes the target but not the viewport.
    private bool _renderTargetStale;

    // The frame at display resolution, when it is rendered at a higher one. Reused across
    // frames, and left empty while supersampling is off — the render target is presentable
    // as it stands then.
    private int[] _resolved = [];

    private Bitmap? bmp;
    private float _referenceDistance = 1f;
    private int _wheelDelta;
    private Point? _selectedPixel;
    private Point _mouseDownAt;
    private bool _mouseDragged;

    /// <summary>Set when a press ended a modal gesture, so its release is not also read as a pick.</summary>
    private bool _endedTransform;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Scene? Scene { get; set; }

    /// <summary>
    /// The counters for the frame just drawn. Read off the current renderer rather than
    /// captured once, because switching backends replaces it.
    /// </summary>
    public RenderStats Stats => Renderer.Stats;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RendererSettings RendererSettings
    {
        get => Renderer.Settings;
        set => Renderer.Settings = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IPainter? Painter { get; set; }

    /// <summary>The full-screen effects applied after every frame; toggled individually.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PostProcessStack PostProcess { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IRenderer Renderer { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RenderDiagnostics Diagnostics => Renderer.Diagnostics;

    #region Backend

    private RenderBackend _backend = RenderBackend.Cpu;

    /// <summary>
    /// Which rasterizer draws the viewport: this engine's own, on the CPU, or a graphics
    /// adapter through OpenGL.
    ///
    /// <para>
    /// Setting it rebuilds the renderer, carrying the settings, the post-process stack and
    /// the debugger's own switches across — switching backends is a statement about where the
    /// triangles are filled, and nothing else about the viewport should move. Read it back
    /// afterwards: asking for the GPU on a machine that has none leaves this on
    /// <see cref="RenderBackend.Cpu"/>, and <see cref="BackendFallback"/> says why.
    /// </para>
    ///
    /// <para>
    /// It defaults to the CPU rather than to whatever is available. The viewer is a
    /// demonstration of a software rasterizer, and starting it on the graphics card would
    /// quietly show you something else.
    /// </para>
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RenderBackend Backend
    {
        get => _backend;
        set => SetBackend(value);
    }

    /// <summary>The adapter the viewport is rendering on, or null on the CPU.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GpuAdapter? Adapter { get; private set; }

    /// <summary>Why the last GPU request fell back to the CPU, or null when nothing did.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? BackendFallback { get; private set; }

    /// <summary>One line naming what the viewport is being drawn by.</summary>
    public string BackendDescription => _backend switch
    {
        RenderBackend.Gpu when Adapter is { } adapter => $"GPU · {adapter.Describe()}",
        RenderBackend.Trace when Renderer is PathTracer tracer =>
            $"CPU · path tracer · {tracer.AccumulatedSamples} spp",
        _ => "CPU · software rasterizer",
    };

    /// <summary>
    /// Paths per pixel the viewport refines to before it stops redrawing itself.
    ///
    /// A path-traced viewport cannot produce a finished frame in the time a paint has, so it
    /// produces a noisy one and then keeps averaging more samples into it for as long as nothing
    /// moves — which is what makes an unusably slow renderer usable to look at. This is where it
    /// gives up and leaves the image alone.
    /// </summary>
    [DefaultValue(512)]
    public int TraceSampleTarget { get; set; } = 512;

    /// <summary>Raised after <see cref="Backend"/> settles, whether or not it is what was asked for.</summary>
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
            // A handful of paths per paint, averaged into what is already there. Enough to see the
            // frame take shape immediately, and small enough that dragging the camera stays
            // responsive — every drag throws the accumulation away anyway.
            tracer.Trace.SamplesPerPixel = 2;
            tracer.Trace.MaxBounces = 2;
            tracer.Trace.Accumulate = true;
        }

        var previous = Renderer;

        // Carried across rather than left behind: these are the viewport's state, not the
        // renderer's, and a mode switch that reset the wireframe overlay or forgot that the
        // event log was being recorded would read as a bug.
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

        // The old one may hold an OpenGL context, a window and a pile of buffers.
        (previous as IDisposable)?.Dispose();

        // The render target carries a reference to the old renderer's counters.
        _renderTargetStale = true;

        BackendChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    #endregion

    /// <summary>Draws the per-frame counters over the top-left of the viewport.</summary>
    [DefaultValue(true)]
    public bool ShowStatsOverlay { get; set; } = true;

    /// <summary>
    /// Samples per screen pixel along each axis. 1 renders one framebuffer pixel per screen
    /// pixel; 2 renders four and averages them down, which anti-aliases everything the
    /// pipeline produces — silhouettes, highlights and texture detail alike — for four times
    /// the fill. Everything the control exposes stays in screen pixels either way.
    /// </summary>
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

    /// <summary>
    /// Whether the world's animations advance. Turning it off holds the current pose rather
    /// than resetting it, so a frame can be inspected in the debugger while it is stopped.
    /// Has no effect on a world with nothing to animate.
    /// </summary>
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

    /// <summary>
    /// Starts or stops the animation clock to match the current world and
    /// <see cref="Animate"/>. Call after loading a world.
    /// </summary>
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

        // A long stall — the window dragged, a model loaded — should not fling the clip
        // forward by seconds; capping the step keeps a resumed animation continuous.
        world.Update(MathF.Min(delta, 0.25f));

        Invalidate();
    }

    /// <summary>Raised after every rendered frame, on the UI thread.</summary>
    public event EventHandler? FrameRendered;

    public event EventHandler? ZoomChanged;

    public event EventHandler? SelectedPixelChanged;

    public Panel3D()
    {
        InitializeComponent();

        Renderer = new Renderer { Settings = new RendererSettings { BackFaceCulling = true } };

        // Created up front and left empty of enabled effects, so the front-end can toggle
        // one on without having to rebuild the chain.
        PostProcess = PostProcessStack.CreateDefault();
        Renderer.PostProcess = PostProcess;

        Painter = new GouraudPainter();

        ResizeRedraw = true;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;

        StatDisplay = new StringBuilder();

        // A timer rather than key-repeat: held keys must move the camera smoothly, and
        // the auto-repeat rate is a user setting we shouldn't inherit.
        _moveTimer = new System.Windows.Forms.Timer { Interval = (int)MoveInterval };
        _moveTimer.Tick += MoveCamera;

        _animationTimer = new System.Windows.Forms.Timer { Interval = (int)AnimationInterval };
        _animationTimer.Tick += AdvanceAnimation;

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

    /// <summary>
    /// How far the camera stands off the scene, measured along the view axis rather than as
    /// the length of its position: panning slides the camera sideways without bringing it any
    /// closer, and the zoom readout shouldn't claim otherwise.
    /// </summary>
    private float CameraDistance =>
        Scene?.Camera is { } camera ? MathF.Max(CameraMotion.MinCameraDistance, MathF.Abs(camera.Position.Z)) : 1f;

    public void ZoomIn() => Dolly(1f);

    public void ZoomOut() => Dolly(-1f);

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
    /// Moves the camera along its view axis, scaling its distance rather than stepping it: one
    /// notch covers as much ground on a 1500-unit elephant as on a 5-unit skull, the approach
    /// slows as the camera closes in, and it can never overshoot through what it is looking at.
    /// </summary>
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

    /// <summary>The probed pixel, in render-target coordinates, or null when none is selected.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? SelectedPixel => _selectedPixel;

    /// <summary>
    /// What the last selected pixel's ray ran into, or null when it hit nothing.
    ///
    /// Selecting a pixel asks two different questions at once, and both are worth answering:
    /// the probe says what the renderer <em>did</em> at that pixel, and this says what is
    /// <em>there</em>. They can disagree — a mesh switched off in the object table is still
    /// geometry, and a pixel the depth test rejected still has a history — and where they do,
    /// the disagreement is usually the bug.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PickHit? Picked { get; private set; }

    /// <summary>Raised when the picked mesh changes, including when a click hits nothing.</summary>
    public event EventHandler? PickedChanged;

    /// <summary>
    /// The transform handles, or null when none are being offered. Held here rather than only
    /// on the renderer settings because the same object answers three questions — what to
    /// draw, what a press grabs, and what a drag moves — and they have to agree.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TransformGizmo? Gizmo
    {
        get => RendererSettings.Gizmo;
        set => RendererSettings.Gizmo = value;
    }

    /// <summary>Raised while a gizmo drag moves the mesh, so a status bar can follow it.</summary>
    public event EventHandler? GizmoChanged;

    /// <summary>
    /// Where completed gizmo drags are recorded so they can be undone, or null to leave them
    /// unrecorded. The viewport reports edits rather than owning the history: undo is an
    /// application-wide gesture with a menu item and a shortcut attached to it, and this control
    /// is only one of the things that can produce an edit.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public EditHistory? History { get; set; }

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

        // Before the early-out below: the same pixel can be over something different once
        // the camera has moved, so a second click on it is still a question worth asking.
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

    /// <summary>
    /// Casts a ray through the selected pixel and records what it hits, outlining that mesh
    /// in the viewport. Under supersampling a screen pixel is a block of samples, so the ray
    /// goes through the one nearest its centre — the same sample the probe follows.
    /// </summary>
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

    /// <summary>
    /// Selects a mesh without a click — the same selection a pick produces, for the times the
    /// choice was made somewhere other than under the cursor: a primitive just added, or one an
    /// undo has put back.
    ///
    /// <para>
    /// The hit it records names no triangle and no distance, because no ray was cast to find one.
    /// Anything that reports the pick as a <em>ray result</em> has to allow for that; everything
    /// that only asks <em>which mesh</em> — the gizmo, the object table, the outline — reads the
    /// same fields either way. A mesh that is not in the current world clears the selection
    /// instead, since the hit addresses it by its position in that world's list.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Forgets what was picked. A selection is a statement about the meshes in front of you,
    /// so it cannot outlive them: the index it holds addresses the world's mesh list by
    /// position, and a world swapped in under it would leave the highlight on whatever
    /// happens to sit at that position now.
    /// </summary>
    public void ClearPick()
    {
        RendererSettings.HighlightedMesh = -1;

        // A gesture on a mesh that is no longer selected has nothing left to confirm, and the
        // world it was moving something in may be on its way out.
        CancelTransform();

        // The gizmo holds the mesh itself rather than an index, so it would happily go on
        // drawing handles on a mesh from a world that is no longer being rendered — and
        // dragging one would move geometry nothing can see.
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

    /// <summary>
    /// Points the renderer's probe at the selected screen pixel. Under supersampling a screen
    /// pixel is a block of samples, so the probe takes the one nearest its centre — the
    /// history then describes a sample that actually contributed to what was clicked.
    /// </summary>
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

    /// <summary>Maps a point in the control to the render-target pixel drawn under it.</summary>
    private static Point ToBufferPixel(Point client) => client;

    private Point SampleOf(Point pixel) => new(
        pixel.X * _superSampling + _superSampling / 2,
        pixel.Y * _superSampling + _superSampling / 2);

    #endregion

    /// <summary>
    /// Saves the last rendered frame as a PNG. The capture is the bare render target:
    /// the stats overlay and the pixel-selection marker are drawn over the control, not
    /// into the framebuffer, so they never appear in the file. Returns false when no
    /// frame has been rendered yet.
    /// </summary>
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

        // The resolved frame rather than the render target: a supersampled capture should be
        // the image on screen, not the larger one it was averaged down from.
        var width = _bufferSize.Width;
        var height = _bufferSize.Height;

        var screen = PresentablePixels();
        var pixels = new int[width * height];

        // The codec takes the framebuffer's own packed ARGB and does the swizzle to PNG's byte
        // order itself, so all that is left here is forcing alpha opaque: cleared background
        // pixels are 0x00000000, which would otherwise save as transparent.
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

            // A history buffer of a different size is not a history of this frame.
            ResetTemporalHistory();

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

    /// <summary>
    /// Forgets what the previous frame looked like, for the renderers that remember one.
    ///
    /// Anything that changes the picture without moving the camera or the geometry — a new world, a
    /// change of shading, a setting toggled — leaves a history that is of a different image, and a
    /// temporal pass would spend the next few frames blending it in.
    /// </summary>
    public void ResetTemporalHistory()
    {
        (Renderer as Renderer)?.ResetHistory();
        (Renderer as PathTracer)?.Reset();
    }

    /// <summary>
    /// Asks for another paint while the path tracer still has samples worth adding.
    ///
    /// Posted rather than called: invalidating from inside a paint handler would recurse, and the
    /// point is to let the message loop run — so a click, a drag or a resize is handled between two
    /// passes instead of after all of them.
    /// </summary>
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

    /// <summary>
    /// The last rendered frame at display resolution: the render target itself, or the
    /// average of each block of samples when it was drawn larger than the control.
    /// </summary>
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

    /// <summary>Releases the render bitmap and the timers; called from the designer's Dispose.</summary>
    private void DisposeRenderResources()
    {
        _moveTimer.Dispose();
        _animationTimer.Dispose();
        bmp?.Dispose();
        bmp = null;

        // The GPU renderer owns an OpenGL context and a window; the software one owns nothing.
        (Renderer as IDisposable)?.Dispose();
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
            Stats.NearClippedTriangleCount,
            Stats.OccludedMeshCount,
            Stats.OccluderMeshCount
        );

        TextRenderer.DrawText(g, StatDisplay.ToString(), Font, new Point(10, 8), Theme.TextSecondary, TextFormatFlags.ExpandTabs);
    }
}
