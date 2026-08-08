using SoftEngine.Core.Animation;
using SoftEngine.Core.Baking;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Editing;
using SoftEngine.Core.Geometry.Import;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Import.Gltf;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Gizmos;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Math;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Graph;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Scenes.Serialization;
using SoftEngine.Core.Textures;
using SoftEngine.Gpu;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Controls;
using SoftEngine.WinForms.Debugging;
using SoftEngine.WinForms.Dialogs;
using SoftEngine.WinForms.Interop;
using System.Numerics;
using System.Text.Json;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen : Form
{
    /// <summary>
    /// The vertical field of view every world is rendered with. The camera solves its pan
    /// against this too, so the two have to stay the same number.
    /// </summary>
    private const float FieldOfView = 40f * MathF.PI / 180f;

    /// <summary>The bundled worlds offered by the model picker.</summary>
    private static readonly DemoEntry[] Demos =
    [
        new("Skull", "skull"),
        new("Parrot", "parrot"),
        new("Parrot rig (animated)", "parrotanim"),
        new("Bone chain (skinned)", "bonechain"),
        new("Juliet (skinned)", "julietskin"),
        new("Elefant", "elefant"),
        new("Teapot", "teapot"),
        new("Juliet", "Juliet"),
        new("Cubes", "cubes"),
        new("Spheres", "spheres"),
        new("Little town", "littletown"),
        new("Town", "town"),
        new("Big town", "bigtown"),
        new("Cube", "cube"),
        new("Big cube", "bigcube"),
        new("Textured cube", "texturedcube"),
        new("Primitives", "primitives"),
        new("Transparency", "transparency"),
        new("Shadows", "shadows"),
        new("Cascaded shadows", "cascades"),
        new("Normal mapping", "normalmapping"),
        new("PBR spheres", "pbrspheres"),
        new("Empty", "empty"),
    ];

    private sealed record WorldSetup(SimpleWorld World, Vector3 CameraPosition, PerspectiveProjection? Projection)
    {
        /// <summary>
        /// Length of a joint's axis tick in the skeleton gizmo. Worlds are authored anywhere
        /// from 2 to 1500 units across, and one fixed size is either invisible on the large
        /// ones or swamps the small ones.
        /// </summary>
        public float SkeletonTickSize { get; init; } = 1f;
    }

    private readonly Label lblLoading;
    private readonly FlatProgressBar prgLoading;

    /// <summary>The generated sky, and the sun direction and range it was generated around.</summary>
    private CubeMap? _sky;
    private Vector3 _skySunDirection;
    private bool _skyIsHighDynamicRange;

    /// <summary>A loaded panorama and where it came from, or null until one is opened.</summary>
    private CubeMap? _panorama;
    private string? _panoramaPath;

    /// <summary>The last bake of the current world, or null when nothing has been measured yet.</summary>
    private Core.Shading.IrradianceVolume? _bakedLight;

    /// <summary>Set by every rendered frame, cleared when the debugger panels have caught up.</summary>
    private bool _frameDirty;

    private SceneObjectCatalog _catalog = SceneObjectCatalog.Empty;

    /// <summary>Id of the bundled world on screen, so the picker reopens on it.</summary>
    private string _currentDemoId = "skull";

    /// <summary>Path of the model file on screen, when the world came from one rather than from a demo.</summary>
    private string? _modelPath;

    /// <summary>
    /// The switches that outlive the session. Read once, here, because a field initializer runs
    /// before the constructor body — and the backend has to be restored while the menu is being
    /// wired, not after somebody has already seen the wrong item ticked.
    /// </summary>
    private readonly ViewerSettings _settings = ViewerSettings.Load();

    public MainScreen()
    {
        InitializeComponent();
        ApplyTheme();

        CenterToScreen();

        lblLoading = new Label
        {
            Text = "Loading…",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            ForeColor = Theme.Accent,
            BackColor = panel3D1.BackColor,
            Visible = false,
        };
        panel3D1.Controls.Add(lblLoading);

        prgLoading = new FlatProgressBar
        {
            Size = new Size(280, 6),
            Maximum = 1000,
        };
        lblLoading.Controls.Add(prgLoading);
        lblLoading.Resize += (s, e) => CenterLoadingProgress();
        CenterLoadingProgress();

        btnLoadModel.Click += async (s, e) => await ShowModelPickerAsync();

        rdbNoneShading.Checked = panel3D1.Painter is null;
        rdbClassicShading.Checked = panel3D1.Painter is ClassicPainter;
        rdbFlatShading.Checked = panel3D1.Painter is FlatPainter;
        rdbGouraudShading.Checked = panel3D1.Painter is GouraudPainter;
        rdbPhongShading.Checked = panel3D1.Painter is PhongPainter;
        rdbTexturedShading.Checked = panel3D1.Painter is TexturedPainter;
        rdbMaterialShading.Checked = panel3D1.Painter is MaterialPainter;
        rdbPbrShading.Checked = panel3D1.Painter is PbrPainter;

        rdbNoneShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = null;
            panel3D1.Invalidate();
        };
        rdbClassicShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new ClassicPainter();
            panel3D1.Invalidate();
        };
        rdbFlatShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new FlatPainter();
            panel3D1.Invalidate();
        };
        rdbGouraudShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new GouraudPainter();
            panel3D1.Invalidate();
        };
        rdbPhongShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = new PhongPainter();
            panel3D1.Invalidate();
        };
        rdbTexturedShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = CreateTexturedPainter();
            panel3D1.Invalidate();
        };
        rdbMaterialShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = CreateMaterialPainter();
            panel3D1.Invalidate();
        };
        rdbPbrShading.CheckedChanged += (s, e) =>
        {
            if (s is not RadioButton { Checked: true })
            {
                return;
            }
            panel3D1.Painter = CreatePbrPainter();
            panel3D1.Invalidate();
        };

        chkShowTriangles.Checked = panel3D1.RendererSettings.ShowTriangles;
        chkShowBackFacesCulling.Checked = panel3D1.RendererSettings.BackFaceCulling;
        chkShowXZGrid.Checked = panel3D1.RendererSettings.ShowXZGrid;
        chkShowAxes.Checked = panel3D1.RendererSettings.ShowAxes;

        chkShowTriangles.CheckedChanged += (s, e) => { panel3D1.RendererSettings.ShowTriangles = chkShowTriangles.Checked; panel3D1.Invalidate(); };
        chkShowBackFacesCulling.CheckedChanged += (s, e) => { panel3D1.RendererSettings.BackFaceCulling = chkShowBackFacesCulling.Checked; panel3D1.Invalidate(); };
        chkShowXZGrid.CheckedChanged += (s, e) => { panel3D1.RendererSettings.ShowXZGrid = chkShowXZGrid.Checked; panel3D1.Invalidate(); };
        chkShowAxes.CheckedChanged += (s, e) => { panel3D1.RendererSettings.ShowAxes = chkShowAxes.Checked; panel3D1.Invalidate(); };
        chkShowSkeleton.CheckedChanged += (s, e) => { panel3D1.RendererSettings.ShowSkeleton = chkShowSkeleton.Checked; panel3D1.Invalidate(); };
        chkAnimate.CheckedChanged += (s, e) => { panel3D1.Animate = chkAnimate.Checked; panel3D1.Invalidate(); };

        panel3D1.Scene = new Scene()
        {
            Projection = new PerspectiveProjection(FieldOfView, .01f, 500f),
            Camera = new ArcBallCamera(panel3D1) { Position = new Vector3(0, 0, -60), FieldOfView = FieldOfView },
            GammaCorrect = true,
            HighDynamicRange = true,
        };

        chkGammaCorrect.Checked = panel3D1.Scene.GammaCorrect;
        chkHighDynamicRange.Checked = panel3D1.Scene.HighDynamicRange;
        chkSky.Checked = true;
        chkTextureFiltering.Checked = true;

        // Trilinear is a refinement of filtering rather than an alternative to it: with filtering
        // off there is no mip chain to blend between, so the box has nothing to say.
        chkTrilinear.Enabled = chkTextureFiltering.Checked;

        chkFog.CheckedChanged += (s, e) => ApplyFog();
        chkShadows.CheckedChanged += (s, e) => ApplyShadows();
        chkSky.CheckedChanged += (s, e) =>
        {
            ApplySky(panel3D1.Scene?.World);
            panel3D1.Invalidate();
        };
        chkHdrSky.CheckedChanged += (s, e) =>
        {
            ApplySky(panel3D1.Scene?.World);
            panel3D1.Invalidate();
        };
        chkPanorama.CheckedChanged += (s, e) =>
        {
            ApplySky(panel3D1.Scene?.World);
            panel3D1.Invalidate();
        };
        btnPanorama.Click += async (s, e) => await LoadPanoramaAsync();
        chkBakedLight.CheckedChanged += (s, e) =>
        {
            ApplyBakedLight();
            panel3D1.Invalidate();
        };
        btnBake.Click += async (s, e) => await BakeIndirectLightAsync();
        chkGammaCorrect.CheckedChanged += (s, e) =>
        {
            if (panel3D1.Scene is { } scene)
            {
                scene.GammaCorrect = chkGammaCorrect.Checked;
                panel3D1.Invalidate();
            }
        };
        chkHighDynamicRange.CheckedChanged += (s, e) =>
        {
            if (panel3D1.Scene is { } scene)
            {
                scene.HighDynamicRange = chkHighDynamicRange.Checked;
                panel3D1.Invalidate();
            }
        };
        chkTextureFiltering.CheckedChanged += (s, e) =>
        {
            chkTrilinear.Enabled = chkTextureFiltering.Checked;
            ApplyTextureFiltering(panel3D1.Painter);
            panel3D1.Invalidate();
        };
        chkTrilinear.CheckedChanged += (s, e) =>
        {
            ApplyTextureFiltering(panel3D1.Painter);
            panel3D1.Invalidate();
        };
        chkSuperSampling.CheckedChanged += (s, e) => panel3D1.SuperSampling = chkSuperSampling.Checked ? 2 : 1;

        chkTemporalAntiAliasing.Checked = panel3D1.RendererSettings.TemporalAntiAliasing;
        chkMotionBlur.Checked = panel3D1.RendererSettings.MotionBlur;
        chkOrderIndependentTransparency.Checked = panel3D1.RendererSettings.OrderIndependentTransparency;

        chkOrderIndependentTransparency.CheckedChanged += (s, e) =>
        {
            panel3D1.RendererSettings.OrderIndependentTransparency = chkOrderIndependentTransparency.Checked;
            panel3D1.Invalidate();
        };

        chkTemporalAntiAliasing.CheckedChanged += (s, e) =>
        {
            panel3D1.RendererSettings.TemporalAntiAliasing = chkTemporalAntiAliasing.Checked;

            // Whatever was accumulated was accumulated against different settings, and a temporal
            // pass that starts from it spends a few frames blending in a picture of the old ones.
            panel3D1.ResetTemporalHistory();
            panel3D1.Invalidate();
        };
        chkMotionBlur.CheckedChanged += (s, e) =>
        {
            panel3D1.RendererSettings.MotionBlur = chkMotionBlur.Checked;
            panel3D1.ResetTemporalHistory();
            panel3D1.Invalidate();
        };

        InitializePostProcessing();

        InitializeBufferViews();
        InitializeCascades();
        InitializeGizmo();

        InitializeDebugger();

        // Last, because it restores the window and the panel layout over whatever the designer
        // and the wiring above left behind — and because the sidebar sections it builds have to
        // exist before a saved workspace can roll any of them up.
        InitializeWorkspace();

        _ = PrepareWorldAsync("skull");
    }

    /// <summary>
    /// Puts the sidebar back at the top once the window is up.
    ///
    /// <para>
    /// It does not start there on its own. The sidebar scrolls, and setting
    /// <see cref="RadioButton.Checked"/> on a control inside a scrolling panel focuses it,
    /// which WinForms answers by scrolling it into view — so wiring up the shading buttons at
    /// startup leaves the panel parked partway down, with the title and the load button above
    /// the fold. Scrolling back has to happen after the first layout, because before it there
    /// is no scroll range to move within.
    /// </para>
    ///
    /// The viewport takes the focus for the same reason: it is what the arrow keys and the
    /// WASD fly controls belong to, and leaving it on a sidebar control both steals those and
    /// invites the next scroll.
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        pnlSidebar.AutoScrollPosition = Point.Empty;
        panel3D1.Focus();
    }

    #region Render backend

    /// <summary>
    /// Wires View → Rendered by, and the status readout that says which one won.
    ///
    /// <para>
    /// The GPU item is offered whether or not there is a GPU, and reports what happened
    /// afterwards rather than being greyed out beforehand. Finding out costs an OpenGL
    /// context, and a menu item that is simply missing on a machine with a graphics card in
    /// it — because a driver is out of date, say — tells you nothing about why.
    /// </para>
    /// </summary>
    private void InitializeBackendMenu()
    {
        mnuRenderCpu.Click += (s, e) => SelectBackend(RenderBackend.Cpu);
        mnuRenderGpu.Click += (s, e) => SelectBackend(RenderBackend.Gpu);
        mnuRenderTrace.Click += (s, e) => SelectBackend(RenderBackend.Trace);

        panel3D1.BackendChanged += (s, e) => UpdateBackendMenu();

        // The tracer's sample count climbs frame by frame while it refines, and the status bar is
        // where anyone would look to see whether it is still working.
        panel3D1.FrameRendered += (s, e) =>
        {
            if (panel3D1.Backend == RenderBackend.Trace)
            {
                lblBackendStatus.Text = panel3D1.BackendDescription;
            }
        };

        RestoreBackend();

        UpdateBackendMenu();
    }

    private void SelectBackend(RenderBackend backend)
    {
        if (panel3D1.Backend == backend && panel3D1.BackendFallback is null)
        {
            return;
        }

        using (new WaitCursorScope(this))
        {
            panel3D1.Backend = backend;
        }

        RememberBackend();

        if (panel3D1.BackendFallback is { } fallback)
        {
            MessageBox.Show(
                this,
                $"{fallback}\n\nThe viewport is still being rendered on the CPU.",
                "No graphics adapter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// Puts the viewport back on the backend it was left on.
    ///
    /// <para>
    /// A request that falls back says so through the status bar rather than through a dialog. A
    /// modal box in front of a window that has not appeared yet is a poor way to find out that a
    /// driver is missing, and unlike a menu click nobody has just asked a question that is waiting
    /// for an answer.
    /// </para>
    /// </summary>
    private void RestoreBackend()
    {
        // The panel is already on the CPU, and building the renderer it is already using would cost
        // a rebuild to arrive back where it started.
        if (_settings.Backend == RenderBackend.Cpu)
        {
            return;
        }

        using (new WaitCursorScope(this))
        {
            panel3D1.Backend = _settings.Backend;
        }

        RememberBackend();
    }

    /// <summary>
    /// Records the backend that <em>settled</em>, which is not always the one that was asked for.
    ///
    /// Saving the request instead would leave a machine whose graphics driver has gone missing
    /// probing for an OpenGL context on every launch, and the file claiming a backend the menu is
    /// not showing as ticked. What is remembered is what is on screen.
    /// </summary>
    private void RememberBackend()
    {
        if (_settings.Backend == panel3D1.Backend)
        {
            return;
        }

        _settings.Backend = panel3D1.Backend;
        _settings.Save();
    }

    private void UpdateBackendMenu()
    {
        var backend = panel3D1.Backend;

        mnuRenderCpu.Checked = backend == RenderBackend.Cpu;
        mnuRenderGpu.Checked = backend == RenderBackend.Gpu;
        mnuRenderTrace.Checked = backend == RenderBackend.Trace;

        lblBackendStatus.Text = panel3D1.BackendDescription;

        // The adapter's own name, which is the only way to tell an integrated part from the
        // discrete one a laptop may also have.
        lblBackendStatus.ToolTipText = backend switch
        {
            RenderBackend.Gpu when panel3D1.Adapter is { } adapter =>
                $"{adapter.Vendor} · {adapter.Renderer}\nOpenGL {adapter.Version}",
            RenderBackend.Trace =>
                "Light traced through the scene on the CPU, refining for as long as nothing moves.",
            _ => "Every triangle rasterized on the CPU by this engine's own scanline filler.",
        };

        // A request that fell back explains itself here. It is the only account a restored choice
        // gets — nobody clicked anything at startup, so there is no dialog to have answered.
        if (panel3D1.BackendFallback is { } fallback)
        {
            lblBackendStatus.ToolTipText = $"{fallback}\n\n{lblBackendStatus.ToolTipText}";
        }
    }

    /// <summary>An hourglass for as long as it is held. Building a GL context is not instant.</summary>
    private readonly struct WaitCursorScope : IDisposable
    {
        private readonly Control _control;
        private readonly Cursor _previous;

        public WaitCursorScope(Control control)
        {
            _control = control;
            _previous = control.Cursor;
            control.Cursor = Cursors.WaitCursor;
        }

        public void Dispose() => _control.Cursor = _previous;
    }

    #endregion

    #region Graphics debugger

    /// <summary>
    /// Wires the debugger panels to the viewport. The renderer records its event list every
    /// frame, but the panels only pull from it on a timer: a drag repaints far faster than a
    /// list view can usefully be rebuilt.
    /// </summary>
    private void InitializeDebugger()
    {
        panel3D1.Diagnostics.CaptureEvents = mnuRecordEvents.Checked;
        panel3D1.ShowStatsOverlay = mnuStatsOverlay.Checked;

        panel3D1.FrameRendered += (s, e) => _frameDirty = true;
        panel3D1.ZoomChanged += (s, e) => UpdateStatus();
        panel3D1.SelectedPixelChanged += (s, e) => UpdateStatus();

        // A click asks two questions of the same pixel: the probe records what the renderer
        // did there, and the ray says which mesh is actually under it. The second is what
        // selects the row in the object table — the same obj:N the event list would name.
        panel3D1.PickedChanged += (s, e) =>
        {
            if (panel3D1.Picked is { } hit && panel3D1.Scene?.World is { } world)
            {
                objectTablePanel.SelectObject(SceneObjectIds.Mesh(world.Lights.Count, hit.MeshIndex));
            }

            UpdateStatus();
        };

        tmrDebugRefresh.Tick += (s, e) => RefreshDebugPanels();
        tmrDebugRefresh.Start();

        objectTablePanel.ActiveChanged += (s, e) => panel3D1.Invalidate();

        // Clicking a write in the pixel history reveals the event and the object behind it.
        pixelHistoryPanel.WriteSelected += (s, write) =>
        {
            eventListPanel.SelectEvent(write.EventIndex);

            if (write.ObjectId >= 0)
            {
                objectTablePanel.SelectObject(write.ObjectId);
            }
        };

        eventListPanel.EventSelected += (s, graphicsEvent) =>
        {
            if (graphicsEvent.ObjectId >= 0)
            {
                objectTablePanel.SelectObject(graphicsEvent.ObjectId);
            }
        };

        mnuLoadModel.Click += async (s, e) => await ShowModelPickerAsync();
        mnuOpenModel.Click += async (s, e) => await OpenModelAsync();
        mnuOpenScene.Click += async (s, e) => await OpenSceneAsync();
        mnuSaveScene.Click += (s, e) => SaveScene();
        mnuScreenshot.Click += (s, e) => SaveScreenshot();
        lblScreenshotHint.Click += (s, e) => SaveScreenshot();
        mnuExit.Click += (s, e) => Close();

        mnuPixelHistory.CheckedChanged += (s, e) => splitLeft.Panel2Collapsed = !mnuPixelHistory.Checked;
        mnuObjectTable.CheckedChanged += (s, e) => splitCenter.Panel2Collapsed = !mnuObjectTable.Checked;
        mnuEventList.CheckedChanged += (s, e) => splitRight.Panel2Collapsed = !mnuEventList.Checked;

        mnuStatsOverlay.CheckedChanged += (s, e) =>
        {
            panel3D1.ShowStatsOverlay = mnuStatsOverlay.Checked;
            panel3D1.Invalidate();
        };

        mnuRecordEvents.CheckedChanged += (s, e) =>
        {
            panel3D1.Diagnostics.CaptureEvents = mnuRecordEvents.Checked;
            panel3D1.Invalidate();
        };

        InitializeBackendMenu();

        mnuViewFront.Click += (s, e) => panel3D1.LookAlong(AxisView.Front);
        mnuViewBack.Click += (s, e) => panel3D1.LookAlong(AxisView.Back);
        mnuViewRight.Click += (s, e) => panel3D1.LookAlong(AxisView.Right);
        mnuViewLeft.Click += (s, e) => panel3D1.LookAlong(AxisView.Left);
        mnuViewTop.Click += (s, e) => panel3D1.LookAlong(AxisView.Top);
        mnuViewBottom.Click += (s, e) => panel3D1.LookAlong(AxisView.Bottom);
        mnuViewOpposite.Click += (s, e) => panel3D1.FlipView();

        mnuTurnX.Click += (s, e) => panel3D1.RotateAroundWorldAxis(Vector3.UnitX, Panel3D.RotationStep);
        mnuTurnY.Click += (s, e) => panel3D1.RotateAroundWorldAxis(Vector3.UnitY, Panel3D.RotationStep);
        mnuTurnZ.Click += (s, e) => panel3D1.RotateAroundWorldAxis(Vector3.UnitZ, Panel3D.RotationStep);

        mnuZoomIn.Click += (s, e) => panel3D1.ZoomIn();
        mnuZoomOut.Click += (s, e) => panel3D1.ZoomOut();
        mnuZoomActual.Click += (s, e) => panel3D1.ZoomActualSize();
        mnuClearPixel.Click += (s, e) => panel3D1.ClearSelectedPixel();

        InitializeFrameHistory();

        UpdateStatus();
    }

    /// <summary>Pulls the last frame's capture into the three panels — at most once per timer tick.</summary>
    private void RefreshDebugPanels()
    {
        // A pinned frame is not going to change, so there is nothing to pull until the pin moves
        // — but the panels still have to be filled the first time it is set, which is what the
        // dirty flag is raised for there too.
        if (!_frameDirty)
        {
            return;
        }

        _frameDirty = false;

        var scene = panel3D1.Scene;
        var signature = SceneObjectCatalog.SignatureOf(scene, panel3D1.Painter, panel3D1.PostProcess);

        if (_catalog.Signature != signature)
        {
            _catalog = SceneObjectCatalog.Build(scene, panel3D1.Painter, panel3D1.PostProcess);
            objectTablePanel.SetCatalog(_catalog);
        }

        var pinned = PinnedFrame();

        if (!splitRight.Panel2Collapsed)
        {
            if (pinned is { } capture)
            {
                eventListPanel.SetEvents(capture.Events);
            }
            else
            {
                eventListPanel.SetEvents(panel3D1.Diagnostics.Events);
            }
        }

        if (!splitLeft.Panel2Collapsed)
        {
            pixelHistoryPanel.SetHistory(pinned?.PixelHistory ?? panel3D1.Diagnostics.PixelHistory, _catalog);
        }

        // The kept-frame count climbs as frames arrive, and the step items become reachable the
        // moment there is a first one — both belong on the timer rather than on every frame.
        UpdateFrameHistoryMenu();

        UpdateStatus();
    }

    #region Frame history

    /// <summary>How many finished frames are kept when the history is switched on.</summary>
    private const int FrameHistoryDepth = 60;

    /// <summary>
    /// Which frame the panels are showing, by its own number, or -1 to follow whatever was
    /// rendered last.
    ///
    /// <para>
    /// The number rather than a position in the kept list, because the list is a window that
    /// slides: the viewport goes on rendering while a frame is pinned, and every new capture
    /// drops the oldest. An index would quietly come to mean a different frame each time that
    /// happened — the panels would creep forward through history while claiming to stand still,
    /// which is worse than either following or stopping.
    /// </para>
    /// </summary>
    private long _pinnedFrameNumber = -1;

    /// <summary>
    /// Where the pinned frame sits in the kept list, or -1 when the panels are following the
    /// renderer.
    /// </summary>
    /// <remarks>
    /// A pinned frame can age out of the window while it is being looked at. The oldest frame
    /// still kept is the closest thing to what was asked for, and the status bar names whichever
    /// frame is actually on screen — so the slip is visible rather than silent.
    /// </remarks>
    private int PinnedIndex()
    {
        if (_pinnedFrameNumber < 0)
        {
            return -1;
        }

        var frames = panel3D1.Diagnostics.Frames;

        if (frames.Count == 0)
        {
            return -1;
        }

        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (frames[i].FrameNumber == _pinnedFrameNumber)
            {
                return i;
            }
        }

        return 0;
    }

    private FrameCapture? PinnedFrame()
    {
        var index = PinnedIndex();

        return index >= 0 ? panel3D1.Diagnostics.Frames[index] : null;
    }

    private void InitializeFrameHistory()
    {
        mnuKeepFrames.CheckedChanged += (s, e) =>
        {
            panel3D1.Diagnostics.HistoryCapacity = mnuKeepFrames.Checked ? FrameHistoryDepth : 0;

            if (!mnuKeepFrames.Checked)
            {
                panel3D1.Diagnostics.ClearHistory();
                GoLive();
            }

            UpdateFrameHistoryMenu();
        };

        mnuPreviousFrame.Click += (s, e) => StepFrame(-1);
        mnuNextFrame.Click += (s, e) => StepFrame(+1);
        mnuLatestFrame.Click += (s, e) => GoLive();

        UpdateFrameHistoryMenu();
    }

    /// <summary>
    /// Moves the pin one frame. Stepping back from live starts at the newest kept frame, and
    /// stepping forward past it returns to following the renderer — so the two ends of the
    /// history behave the way a person expects rather than stopping dead.
    /// </summary>
    private void StepFrame(int direction)
    {
        var frames = panel3D1.Diagnostics.Frames;

        if (frames.Count == 0)
        {
            return;
        }

        var index = PinnedIndex();

        if (index < 0)
        {
            // Following the renderer. Back pins the newest frame captured; forward has nowhere
            // to go, since the newest frame is the one already on screen.
            if (direction < 0)
            {
                PinFrame(frames[^1].FrameNumber);
            }

            return;
        }

        var next = index + direction;

        if (next >= frames.Count)
        {
            GoLive();
            return;
        }

        PinFrame(frames[Math.Max(next, 0)].FrameNumber);
    }

    private void PinFrame(long frameNumber)
    {
        _pinnedFrameNumber = frameNumber;

        // The panels read the pin on their next tick, and there may not be another rendered
        // frame to raise the flag — a still camera repaints nothing.
        _frameDirty = true;

        UpdateFrameHistoryMenu();
        RefreshDebugPanels();
    }

    private void GoLive()
    {
        if (_pinnedFrameNumber < 0)
        {
            return;
        }

        _pinnedFrameNumber = -1;
        _frameDirty = true;

        UpdateFrameHistoryMenu();
        RefreshDebugPanels();
    }

    private void UpdateFrameHistoryMenu()
    {
        var keeping = mnuKeepFrames.Checked;
        var frames = panel3D1.Diagnostics.Frames.Count;
        var index = PinnedIndex();

        mnuPreviousFrame.Enabled = keeping && frames > 0 && index != 0;
        mnuNextFrame.Enabled = keeping && index >= 0;
        mnuLatestFrame.Enabled = _pinnedFrameNumber >= 0;

        mnuKeepFrames.Text = keeping
            ? $"&Keep recent frames ({frames}/{FrameHistoryDepth})"
            : "&Keep recent frames";
    }

    #endregion

    private void UpdateStatus()
    {
        // 100% is the framing the world loaded with; the wheel and W/S move away from it.
        var buffer = panel3D1.BufferSize;
        lblZoomStatus.Text = $"Zoom: {panel3D1.Zoom * 100f:0}%  ·  {buffer.Width} × {buffer.Height}";

        if (panel3D1.SelectedPixel is { } pixel && panel3D1.SelectedPixelNormalized is { } normalized)
        {
            // What the ray found under the same pixel, when it found anything — the mesh's
            // own identifier, so it can be looked up in the object table and the event list.
            var picked = string.Empty;

            if (panel3D1.Picked is { } hit && panel3D1.Scene?.World is { } world)
            {
                var objectId = SceneObjectIds.Mesh(world.Lights.Count, hit.MeshIndex);

                // A selection made without a ray — adding a primitive, or an undo putting one
                // back — names no triangle, and reporting "tri:-1 at 0" would read as a pick that
                // went wrong rather than as one that was never cast.
                var where = hit.TriangleIndex >= 0 ? $" tri:{hit.TriangleIndex} at {hit.Distance:0.##}" : string.Empty;

                picked = $"  ·  picked obj:{objectId} ({hit.Mesh.GetType().Name}){where}";
            }

            lblPixelStatus.Text =
                $"Selected pixel X: {pixel.X} ({normalized.X:0.000}) Y: {pixel.Y} ({normalized.Y:0.000}){picked}";
        }
        else
        {
            lblPixelStatus.Text = "Selected pixel: none — click the viewport to probe and pick one";
        }

        // Only ever offered for something that is there to delete.
        mnuDelete.Enabled = panel3D1.Picked is not null;

        // A modal gesture has no handle on screen to show what it is doing, so the status bar is
        // the whole of its feedback — what it is, which axis it is pressed against, and the two
        // keys that end it. Ahead of the gizmo's own line because only one of them ever runs.
        if (_transform is { IsActive: true })
        {
            lblPixelStatus.Text =
                $"{_transform.Describe()}  ·  X / Y / Z to constrain  ·  click or Enter to confirm, Esc to cancel";
        }

        // A drag has to say what it did in numbers as well as in pixels: eyeballing a mesh
        // into place is exactly the case where you then want to know where "place" was.
        else if (_gizmo is { IsActive: true, Target: { } target })
        {
            var what = _gizmo.Mode switch
            {
                GizmoMode.Rotate => $"rotation ({Degrees(target.Rotation.XPitch)}, {Degrees(target.Rotation.YYaw)}, {Degrees(target.Rotation.ZRoll)})",
                GizmoMode.Scale => $"scale ({target.Scale.X:0.###}, {target.Scale.Y:0.###}, {target.Scale.Z:0.###})",
                _ => $"position ({target.Position.X:0.###}, {target.Position.Y:0.###}, {target.Position.Z:0.###})",
            };

            lblPixelStatus.Text += $"  ·  {what}";

            // The increment has to be visible, or a drag that lands on a round number reads as
            // the renderer having quietly rounded it.
            if (_gizmo.Snap.Enabled)
            {
                var step = _gizmo.Mode switch
                {
                    GizmoMode.Rotate => Degrees(_gizmo.Snap.RotateStep),
                    GizmoMode.Scale => $"{_gizmo.Snap.ScaleStep:0.###}×",
                    _ => $"{_gizmo.Snap.TranslateStep:0.###}",
                };

                lblPixelStatus.Text += $"  ·  snap {step}";
            }
        }

        if (panel3D1.Scene?.Camera is { } camera)
        {
            var position = camera.Position;

            // The named view, when the camera is lined up with one: worth saying, because
            // that is the difference between a view you can reason about and one that is
            // merely close to it.
            var view = camera is ArcBallCamera { CurrentAxisView: { } axisView } ? $" · {axisView}" : string.Empty;

            lblCameraStatus.Text = $"Camera: ({position.X:0.##}, {position.Y:0.##}, {position.Z:0.##}){view}";
        }

        // A pinned frame reports its own numbers. Showing the live ones beside a pinned event
        // list would put two different frames on the same status bar, which is the one thing a
        // history must not do.
        if (PinnedFrame() is { } pinned)
        {
            lblFrameStatus.Text =
                $"Frame #{pinned.FrameNumber} · {pinned.Stats.TotalTimeMs} ms · pinned (live is #{panel3D1.Diagnostics.FrameNumber})";
        }
        else
        {
            var stats = panel3D1.Stats;
            lblFrameStatus.Text = $"Frame #{panel3D1.Diagnostics.FrameNumber} · {stats.CalculationTimeMs + stats.PainterTimeMs} ms";
        }
    }

    /// <summary>A mesh's Euler angles are stored in radians; nobody reads a pose in radians.</summary>
    private static string Degrees(float radians) => $"{radians * 180f / MathF.PI:0.#}°";

    #endregion

    /// <summary>Opens the model picker: the bundled worlds, or a file from the machine.</summary>
    private async Task ShowModelPickerAsync()
    {
        using var dialog = new ModelPickerDialog(Demos, _currentDemoId);

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Choice is not { } choice)
        {
            return;
        }

        if (choice.FilePath is { } path)
        {
            await PrepareWorldFromFileAsync(path);
        }
        else if (choice.DemoId is { } id)
        {
            await PrepareWorldAsync(id);
        }
    }

    private async Task OpenModelAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open 3D model",
            Filter = "3D models (*.obj;*.dae;*.gltf;*.glb)|*.obj;*.dae;*.gltf;*.glb"
                   + "|Wavefront OBJ (*.obj)|*.obj"
                   + "|Collada (*.dae)|*.dae"
                   + "|glTF 2.0 (*.gltf;*.glb)|*.gltf;*.glb"
                   + "|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await PrepareWorldFromFileAsync(dialog.FileName);
        }
    }

    private void SaveScreenshot()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save screenshot",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = "png",
            FileName = $"{_currentDemoId}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!panel3D1.SaveScreenshot(dialog.FileName))
            {
                MessageBox.Show(this, "Nothing has been rendered yet.", "Save screenshot",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Failed to save the screenshot:\n{exception.Message}", "Save screenshot",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #region Scenes

    private const string SceneFilter = "SoftEngine scene (*.scene.json)|*.scene.json|JSON (*.json)|*.json|All files (*.*)|*.*";

    /// <summary>
    /// True while a document is being written onto the viewport. The handlers that <em>derive</em>
    /// state from the world — fog distances from the framing, the SSAO radius from the world's
    /// scale — sit out that window: the document carries the numbers those would recompute, and
    /// having them fire as each control is synchronised would overwrite what was just loaded with
    /// what the world would have defaulted to.
    /// </summary>
    private bool _applyingScene;

    /// <summary>Where the current scene was last saved or opened, so a re-save offers the same name.</summary>
    private string? _scenePath;

    private void SaveScene()
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save scene",
            Filter = SceneFilter,
            DefaultExt = "scene.json",
            FileName = Path.GetFileName(_scenePath) ?? $"{(_currentDemoId is { Length: > 0 } id ? id : "scene")}.scene.json",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var document = SceneSerializer.Capture(scene, panel3D1.RendererSettings, panel3D1.PostProcess);

            DescribeFrontEnd(document);

            SceneSerializer.Save(dialog.FileName, document);

            _scenePath = dialog.FileName;
            lblCurrentModel.Text = Path.GetFileName(dialog.FileName);

            RememberRecentFile(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(this, $"Failed to save the scene:\n{exception.Message}", "Save scene",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Fills in the parts of a document only the application can know: which world this was built
    /// on, which painter is drawing it, and how the camera is oriented. The engine has no
    /// vocabulary for any of the three — a demo id is a name the front-end assigns, and
    /// <see cref="Core.Scenes.Cameras.ICamera"/> promises a view matrix rather than a rotation.
    /// </summary>
    private void DescribeFrontEnd(SceneDocument document)
    {
        document.World = _currentDemoId is { Length: > 0 } demo
            ? new WorldSource { Demo = demo }
            : new WorldSource { File = _modelPath };

        if (document.Camera is { } camera)
        {
            camera.ReferenceDistance = panel3D1.ReferenceDistance;

            if (panel3D1.Scene?.Camera is ArcBallCamera arcBall)
            {
                camera.Orientation = arcBall.Rotation;
            }
        }

        document.Rendering ??= new RenderState();
        document.Rendering.Painter = PainterName(panel3D1.Painter);
        document.Rendering.SuperSampling = chkSuperSampling.Checked ? 2 : 1;
        document.Rendering.TextureFiltering = chkTextureFiltering.Checked;
        document.Rendering.TrilinearFiltering = chkTrilinear.Checked;
        document.Rendering.Animate = chkAnimate.Checked;

        document.Environment ??= new EnvironmentState();
        document.Environment.ShowSky = chkSky.Checked;

        // The path, not the pixels — a panorama is an asset on disk for the same reason the model
        // is, and inlining six cube faces of floats would dwarf the document a hundred times over.
        document.Environment.Panorama = chkPanorama.Checked ? _panoramaPath : null;
    }

    private async Task OpenSceneAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open scene",
            Filter = SceneFilter,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await LoadSceneAsync(dialog.FileName);
    }

    /// <summary>
    /// Opens a scene document by path, wherever the path came from — the dialog above, the recent
    /// list, or a file dropped on the window.
    /// </summary>
    private async Task LoadSceneAsync(string path)
    {
        SceneDocument document;

        try
        {
            document = SceneSerializer.Load(path);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Failed to read the scene:\n{exception.Message}", "Open scene",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // The geometry first, then everything the document says about it. The order is forced: a
        // mesh transform is addressed by its position in the world's mesh list, so there has to
        // be a world for it to address.
        if (document.World is { Demo: { Length: > 0 } demo })
        {
            await PrepareWorldAsync(demo);
        }
        else if (document.World is { File: { Length: > 0 } file })
        {
            if (!File.Exists(file))
            {
                MessageBox.Show(this,
                    $"The scene refers to a model that is not there:\n{file}\n\nEverything else in it will still be applied.",
                    "Open scene", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                await PrepareWorldFromFileAsync(file);
            }
        }

        // Before the document is applied, so the checkbox it syncs has something to point at.
        if (document.Environment is { Panorama: { Length: > 0 } panorama })
        {
            if (File.Exists(panorama))
            {
                await LoadPanoramaAsync(panorama, announceFailure: true);
            }
            else
            {
                MessageBox.Show(this,
                    $"The scene refers to a panorama that is not there:\n{panorama}\n\nIt will be lit by the procedural sky instead.",
                    "Open scene", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        ApplyScene(document);

        _scenePath = path;
        lblCurrentModel.Text = Path.GetFileName(path);

        RememberRecentFile(path);
    }

    /// <summary>
    /// Writes a document onto the viewport and brings the sidebar into agreement with it. Both
    /// halves are needed: a checkbox left saying "off" over a setting the document turned on is
    /// worse than not loading the setting at all, because the next click on it toggles to the
    /// value it already has and appears to do nothing.
    /// </summary>
    private void ApplyScene(SceneDocument document)
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        _applyingScene = true;

        try
        {
            // The controls go first and the document second, so where the two disagree — a
            // checkbox handler that recomputes a derived value, say — the document wins.
            SyncControlsToScene(document);

            SceneSerializer.Apply(document, scene, panel3D1.RendererSettings, panel3D1.PostProcess);

            if (document.Camera is { } camera)
            {
                if (camera.ReferenceDistance is { } reference and > 0f)
                {
                    panel3D1.ReferenceDistance = reference;
                }

                if (camera.Orientation is { } orientation && scene.Camera is ArcBallCamera arcBall)
                {
                    arcBall.Rotation = orientation;
                }
            }

            // The sky is a cube map rather than a flag, so it has to be rebuilt here — Apply can
            // only set whether one is shown, not generate one.
            ApplySky(scene.World);
        }
        finally
        {
            _applyingScene = false;
        }

        // The meshes have moved, so anything holding one is stale — including a temporal history of
        // where they used to be.
        panel3D1.ResetTemporalHistory();

        _history.Clear();
        panel3D1.ClearPick();
        panel3D1.ClearSelectedPixel();

        panel3D1.SyncAnimationTimer();
        panel3D1.Invalidate();

        UpdateStatus();
    }

    /// <summary>Points every sidebar control at what the document says, without firing derived recomputation.</summary>
    private void SyncControlsToScene(SceneDocument document)
    {
        if (document.Rendering is { } rendering)
        {
            SelectPainter(rendering.Painter);

            chkShowTriangles.Checked = rendering.ShowTriangles;
            chkShowBackFacesCulling.Checked = rendering.BackFaceCulling;
            chkShowXZGrid.Checked = rendering.ShowXZGrid;
            chkShowAxes.Checked = rendering.ShowAxes;
            chkShowSkeleton.Checked = rendering.ShowSkeleton;
            chkGammaCorrect.Checked = rendering.GammaCorrect;
            chkHighDynamicRange.Checked = rendering.HighDynamicRange;
            chkTextureFiltering.Checked = rendering.TextureFiltering;
            chkTrilinear.Checked = rendering.TrilinearFiltering;
            chkTrilinear.Enabled = rendering.TextureFiltering;
            chkSuperSampling.Checked = rendering.SuperSampling > 1;
            chkAnimate.Checked = rendering.Animate;
            chkTemporalAntiAliasing.Checked = rendering.TemporalAntiAliasing;
            chkMotionBlur.Checked = rendering.MotionBlur;
            chkOrderIndependentTransparency.Checked = rendering.OrderIndependentTransparency;

            SelectItem(cboBufferView, choice =>
                choice is BufferViewChoice view &&
                string.Equals(view.View.ToString(), rendering.DebugView, StringComparison.OrdinalIgnoreCase));
        }

        if (document.Fog is { } fog)
        {
            chkFog.Checked = fog.Enabled;
        }

        if (document.Shadows is { } shadows)
        {
            chkShadows.Checked = shadows.Enabled;

            SelectItem(cboCascades, choice => choice is CascadeChoice cascade && cascade.Count == shadows.CascadeCount);
        }

        if (document.Environment is { } environment)
        {
            chkSky.Checked = environment.ShowSky;

            // Only if the file it named actually loaded — a scene that points at a panorama which
            // is not there falls back to the procedural sky rather than to no environment at all.
            chkPanorama.Checked = environment.Panorama is { Length: > 0 } && _panorama is not null;
        }

        if (document.Post is { } post)
        {
            chkReflections.Checked = post.Ssr?.Enabled ?? chkReflections.Checked;
            chkSsao.Checked = post.Ssao?.Enabled ?? chkSsao.Checked;
            chkBloom.Checked = post.Bloom?.Enabled ?? chkBloom.Checked;
            chkToneMap.Checked = post.ToneMap?.Enabled ?? chkToneMap.Checked;
            chkFxaa.Checked = post.Fxaa?.Enabled ?? chkFxaa.Checked;
            chkVignette.Checked = post.Vignette?.Enabled ?? chkVignette.Checked;
        }
    }

    /// <summary>Selects the first item a predicate accepts, leaving the box alone when none does.</summary>
    private static void SelectItem(ComboBox box, Func<object?, bool> matches)
    {
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (matches(box.Items[i]))
            {
                box.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>The name a painter is written to a scene file under.</summary>
    private static string PainterName(IPainter? painter) => painter switch
    {
        ClassicPainter => "Classic",
        FlatPainter => "Flat",
        GouraudPainter => "Gouraud",
        PhongPainter => "Phong",
        PbrPainter => "Pbr",
        MaterialPainter => "Material",
        TexturedPainter => "Textured",
        null => "None",
        _ => "Gouraud",
    };

    /// <summary>
    /// Checks the shading radio a name refers to, which is what actually constructs the painter.
    /// Going through the radio rather than assigning the painter directly keeps the one path that
    /// applies the texture-filtering settings to it.
    /// </summary>
    private void SelectPainter(string? name)
    {
        // An unrecognised name falls back to Gouraud rather than throwing: a scene written by a
        // build with a painter this one does not have should still open.
        var radio = name?.ToLowerInvariant() switch
        {
            "none" => rdbNoneShading,
            "classic" => rdbClassicShading,
            "flat" => rdbFlatShading,
            "phong" => rdbPhongShading,
            "textured" => rdbTexturedShading,
            "material" => rdbMaterialShading,
            "pbr" or "physicallybased" => rdbPbrShading,
            _ => rdbGouraudShading,
        };

        radio.Checked = true;
    }

    #endregion

    /// <summary>A textured painter configured from the "Texture filtering" checkbox.</summary>
    private TexturedPainter CreateTexturedPainter()
    {
        var painter = new TexturedPainter();
        ApplyTextureFiltering(painter);
        return painter;
    }

    /// <summary>A material painter configured from the "Texture filtering" checkbox.</summary>
    private MaterialPainter CreateMaterialPainter()
    {
        var painter = new MaterialPainter();
        ApplyTextureFiltering(painter);
        return painter;
    }

    /// <summary>A physically-based painter configured from the "Texture filtering" checkbox.</summary>
    private PbrPainter CreatePbrPainter()
    {
        var painter = new PbrPainter();
        ApplyTextureFiltering(painter);
        return painter;
    }

    /// <summary>Applies the filtering checkboxes to whichever painter samples textures, if any.</summary>
    private void ApplyTextureFiltering(IPainter? painter)
    {
        var filtering = (chkTextureFiltering.Checked, chkTrilinear.Checked) switch
        {
            (true, true) => TextureFiltering.Trilinear,
            (true, false) => TextureFiltering.Bilinear,
            _ => TextureFiltering.Nearest,
        };

        switch (painter)
        {
            case TexturedPainter textured:
                textured.Filtering = filtering;
                textured.UseMipMaps = chkTextureFiltering.Checked;
                break;

            case MaterialPainter material:
                material.Filtering = filtering;
                material.UseMipMaps = chkTextureFiltering.Checked;
                break;

            case PbrPainter pbr:
                pbr.Filtering = filtering;
                pbr.UseMipMaps = chkTextureFiltering.Checked;
                break;
        }
    }

    /// <summary>One entry of the buffer-view selector; the label is what the list shows.</summary>
    private sealed record BufferViewChoice(string Label, DebugView View)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Fills the buffer-view selector and points it at the renderer. Each entry is one of the
    /// frame's own buffers presented in place of the shaded image.
    /// </summary>
    private void InitializeBufferViews()
    {
        cboBufferView.Items.AddRange(
        [
            new BufferViewChoice("Shaded image", DebugView.Off),
            new BufferViewChoice("Depth", DebugView.Depth),
            new BufferViewChoice("Normals", DebugView.Normals),
            new BufferViewChoice("Overdraw", DebugView.Overdraw),
            new BufferViewChoice("Shadow map", DebugView.ShadowMap),
            new BufferViewChoice("Occlusion buffer", DebugView.OcclusionBuffer),
            new BufferViewChoice("Velocity", DebugView.Velocity),
            new BufferViewChoice("Mip level", DebugView.MipLevel),
        ]);

        cboBufferView.SelectedIndex = 0;

        cboBufferView.SelectedIndexChanged += (s, e) =>
        {
            if (cboBufferView.SelectedItem is BufferViewChoice choice)
            {
                panel3D1.RendererSettings.DebugView = choice.View;
                panel3D1.Invalidate();
            }
        };
    }

    /// <summary>
    /// Fills the shadow-cascade selector. One cascade is a single map fitted to the whole
    /// world, which is what the engine did before cascades existed; more splits the camera's
    /// view distance so the near slice gets a buffer of its own.
    /// </summary>
    private void InitializeCascades()
    {
        cboCascades.Items.AddRange(
        [
            new CascadeChoice("1 — one map over the world", 1),
            new CascadeChoice("2 cascades", 2),
            new CascadeChoice("3 cascades", 3),
            new CascadeChoice("4 cascades", 4),
        ]);

        cboCascades.SelectedIndex = 0;

        cboCascades.SelectedIndexChanged += (s, e) => ApplyShadows();
    }

    private sealed record CascadeChoice(string Label, int Count)
    {
        public override string ToString() => Label;
    }

    /// <summary>The gizmo the viewport draws and drags. One object, so what is drawn is what is grabbed.</summary>
    private readonly TransformGizmo _gizmo = new();

    /// <summary>Completed drags, so they can be taken back. Cleared whenever a world is replaced.</summary>
    private readonly EditHistory _history = new();

    /// <summary>
    /// Fills the transform-gizmo selector and attaches the gizmo to whatever is picked.
    ///
    /// The gizmo needs a target and picking already produces one, so the two are wired
    /// together rather than given separate selections — clicking a mesh is how you say which
    /// mesh the handles belong to, and it is the gesture that already means that.
    /// </summary>
    private void InitializeGizmo()
    {
        cboGizmo.Items.AddRange(
        [
            new GizmoChoice("Off", GizmoMode.Off),
            new GizmoChoice("Move", GizmoMode.Translate),
            new GizmoChoice("Rotate", GizmoMode.Rotate),
            new GizmoChoice("Scale", GizmoMode.Scale),
        ]);

        cboGizmo.SelectedIndex = 0;

        panel3D1.Gizmo = _gizmo;

        cboGizmo.SelectedIndexChanged += (s, e) =>
        {
            if (cboGizmo.SelectedItem is GizmoChoice choice)
            {
                _gizmo.Mode = choice.Mode;
                panel3D1.Invalidate();
            }
        };

        panel3D1.PickedChanged += (s, e) =>
        {
            _gizmo.Target = panel3D1.Picked?.Mesh;
            panel3D1.Invalidate();
        };

        panel3D1.GizmoChanged += (s, e) => UpdateStatus();

        InitializeEditing();
    }

    /// <summary>
    /// Wires the edit history and the snapping toggle.
    ///
    /// <para>
    /// The two belong together: snapping is what makes a drag land on a number worth keeping,
    /// and undo is what makes trying one cheap. A gizmo without either is a control you can only
    /// commit with.
    /// </para>
    /// </summary>
    private void InitializeEditing()
    {
        panel3D1.History = _history;

        _history.Changed += (s, e) => UpdateEditMenu();

        // The gesture goes first. Ctrl+Z is a menu shortcut and so is dispatched before the
        // viewport's own key handling, which means it arrives even mid-drag — and undoing onto a
        // mesh that a half-finished gesture is still writing to would leave the history's version
        // of the transform and the mesh's disagreeing.
        mnuUndo.Click += (s, e) =>
        {
            panel3D1.CancelTransform();
            StepHistory(_history.Undo());
        };

        mnuRedo.Click += (s, e) =>
        {
            panel3D1.CancelTransform();
            StepHistory(_history.Redo());
        };

        // Two controls for one setting, because they answer different questions: the sidebar
        // checkbox is next to the gizmo selector and so is where you look for it, and the menu
        // item is where the keyboard shortcut can live.
        chkSnap.CheckedChanged += (s, e) => ApplySnapping(chkSnap.Checked);
        mnuSnap.CheckedChanged += (s, e) => ApplySnapping(mnuSnap.Checked);

        InitializePrimitives();

        UpdateEditMenu();
    }

    /// <summary>
    /// Turns snapping on or off, keeping the two controls that say so in agreement. Each one
    /// writes through the other, so the guard is what stops the pair ringing.
    /// </summary>
    private void ApplySnapping(bool enabled)
    {
        if (_gizmo.Snap.Enabled == enabled && chkSnap.Checked == enabled && mnuSnap.Checked == enabled)
        {
            return;
        }

        _gizmo.Snap.Enabled = enabled;
        chkSnap.Checked = enabled;
        mnuSnap.Checked = enabled;

        UpdateStatus();
    }

    /// <summary>
    /// Follows an undo or a redo: the mesh it changed becomes the selection, so the handles are on
    /// the thing that just moved rather than wherever they were left. Nothing happens when the
    /// stack was empty, which is the case the menu items are greyed out for anyway.
    /// </summary>
    private void StepHistory(IEditCommand? command)
    {
        switch (command)
        {
            case null:
                return;

            case TransformEdit edit:
                _gizmo.Target = edit.Mesh;
                break;

            // A mesh that has just left the world cannot stay selected: the pick addresses it by
            // its position in the world's mesh list, and that position now holds something else
            // or nothing at all. Reselecting one that has come back is the same rule the other
            // way round — either way the selection ends up on what the step changed.
            case MeshListEdit list:
                panel3D1.SelectMesh(list.Mesh);
                panel3D1.ResetTemporalHistory();
                break;
        }

        UpdateStatus();
        panel3D1.Invalidate();
    }

    /// <summary>
    /// Re-labels the two menu items from the stacks. Naming the edit — "Undo Move Cube" — is
    /// what tells you whether the next Ctrl+Z is the one you meant before you press it.
    /// </summary>
    private void UpdateEditMenu()
    {
        mnuUndo.Enabled = _history.CanUndo;
        mnuRedo.Enabled = _history.CanRedo;

        mnuUndo.Text = _history.NextUndo is { } undo ? $"&Undo {undo}" : "&Undo";
        mnuRedo.Text = _history.NextRedo is { } redo ? $"&Redo {redo}" : "&Redo";
    }

    /// <summary>
    /// Scales the snap increments to the world just loaded. A grid step is a world distance and
    /// the demos span three orders of magnitude of them: one unit is a sensible grid on a 2-unit
    /// skull and a meaningless one on a 1500-unit elephant, where a drag would snap to the same
    /// place it started from every time. The rotation step is an angle and needs no such help.
    /// </summary>
    private void ApplySnapScale()
    {
        var reference = panel3D1.ReferenceDistance;

        if (reference <= 0f)
        {
            return;
        }

        // A round number near a fiftieth of the framing distance, so the step is always
        // something a person would have typed: 0.1, 1, 10, 100.
        var rough = reference * 0.02f;
        var magnitude = MathF.Pow(10f, MathF.Round(MathF.Log10(rough)));

        _gizmo.Snap.TranslateStep = MathF.Max(magnitude, 0.001f);
    }

    private sealed record GizmoChoice(string Label, GizmoMode Mode)
    {
        public override string ToString() => Label;
    }

    /// <summary>Points each post-processing checkbox at its effect in the viewport's stack.</summary>
    private void InitializePostProcessing()
    {
        Bind(chkReflections, panel3D1.PostProcess.Find<SsrEffect>());
        Bind(chkSsao, panel3D1.PostProcess.Find<SsaoEffect>());
        Bind(chkBloom, panel3D1.PostProcess.Find<BloomEffect>());
        Bind(chkToneMap, panel3D1.PostProcess.Find<ToneMapEffect>());
        Bind(chkFxaa, panel3D1.PostProcess.Find<FxaaEffect>());
        Bind(chkVignette, panel3D1.PostProcess.Find<VignetteEffect>());

        void Bind(CheckBox box, IPostEffect? effect)
        {
            if (effect is null)
            {
                box.Enabled = false;
                return;
            }

            box.Checked = effect.Enabled;
            box.CheckedChanged += (s, e) =>
            {
                effect.Enabled = box.Checked;
                panel3D1.Invalidate();
            };
        }
    }

    /// <summary>
    /// Turns shadow mapping on or off. The map's resolution scales with the viewport, so a
    /// larger window doesn't end up with visibly coarser shadows than a small one.
    /// </summary>
    private void ApplyShadows()
    {
        // A scene file carries the resolution and the cascade count it was saved with, and this
        // would derive both from the viewport instead.
        if (_applyingScene || panel3D1.Scene is not { } scene)
        {
            return;
        }

        scene.Shadows.Enabled = chkShadows.Checked;
        scene.Shadows.Resolution = panel3D1.BufferSize.Width > 1280 ? 2048 : 1024;

        if (cboCascades.SelectedItem is CascadeChoice cascades)
        {
            scene.Shadows.CascadeCount = cascades.Count;
        }

        panel3D1.Invalidate();
    }

    /// <summary>
    /// Turns the scene's fog on or off, scaled to the distance the current world is
    /// framed from and fading into the viewport background colour.
    /// </summary>
    private void ApplyFog()
    {
        // The document's fog distances were chosen against the scene it was saved from; deriving
        // them again from the framing would throw that away.
        if (_applyingScene || panel3D1.Scene is not { } scene)
        {
            return;
        }

        var fog = scene.Fog;
        fog.Enabled = chkFog.Checked;

        if (fog.Enabled)
        {
            var distance = panel3D1.ReferenceDistance;
            fog.Mode = FogMode.Linear;
            fog.Start = distance * 0.5f;
            fog.End = distance * 4f;
            fog.Color = new ColorRGB(Theme.Viewport.R, Theme.Viewport.G, Theme.Viewport.B);
        }

        panel3D1.Invalidate();
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;

        // The scrolling host takes the sidebar's colour too, or the strip below the controls
        // shows through in the system default.
        pnlSidebar.BackColor = Theme.Surface;
        tlpSidebar.BackColor = Theme.Surface;
        lblTitle.ForeColor = Theme.TextPrimary;
        lblDisplayHeader.ForeColor = Theme.TextSecondary;
        lblShadingHeader.ForeColor = Theme.TextSecondary;
        lblPostHeader.ForeColor = Theme.TextSecondary;
        lblBufferHeader.ForeColor = Theme.TextSecondary;
        lblCascadeHeader.ForeColor = Theme.TextSecondary;
        lblGizmoHeader.ForeColor = Theme.TextSecondary;

        cboBufferView.BackColor = Theme.Selection;
        cboBufferView.ForeColor = Theme.TextPrimary;

        lblModelHeader.ForeColor = Theme.TextSecondary;
        lblCurrentModel.ForeColor = Theme.TextPrimary;

        foreach (var button in new[] { btnLoadModel, btnPanorama })
        {
            button.BackColor = Theme.Selection;
            button.ForeColor = Theme.TextPrimary;
            button.FlatAppearance.BorderColor = Theme.Accent;
            button.FlatAppearance.MouseOverBackColor = Theme.Accent;
        }

        foreach (Control control in flpDisplay.Controls)
        {
            control.ForeColor = Theme.TextPrimary;
        }

        // The button in the display panel is themed as a button, not as a label like its
        // neighbours — the loop above them all would otherwise repaint its text over its fill.
        btnPanorama.ForeColor = Theme.TextPrimary;
        btnBake.ForeColor = Theme.TextPrimary;
        foreach (Control control in flpShading.Controls)
        {
            control.ForeColor = Theme.TextPrimary;
        }
        foreach (Control control in flpPost.Controls)
        {
            control.ForeColor = Theme.TextPrimary;
        }

        pnlViewport.BackColor = Theme.Background;
        panel3D1.BackColor = Theme.Viewport;

        menuStrip.BackColor = Theme.Surface;
        menuStrip.ForeColor = Theme.TextPrimary;

        statusStrip.BackColor = Theme.Surface;
        statusStrip.ForeColor = Theme.TextSecondary;

        foreach (SplitContainer split in new[] { splitMain, splitLeft, splitRight, splitCenter })
        {
            split.BackColor = Theme.Background;
            split.Panel1.BackColor = Theme.Background;
            split.Panel2.BackColor = Theme.Background;
        }
    }

    /// <summary>Places the progress bar just below the centered "Loading…" text.</summary>
    private void CenterLoadingProgress() =>
        prgLoading.Location = new Point(
            (lblLoading.ClientSize.Width - prgLoading.Width) / 2,
            lblLoading.ClientSize.Height / 2 + 40);

    private Task PrepareWorldAsync(string id)
    {
        _currentDemoId = id;
        _modelPath = null;

        string label = Demos.FirstOrDefault(demo => demo.Id == id)?.Display ?? id;

        return PrepareWorldCoreAsync(progress => BuildWorld(id, progress), label);
    }

    private Task PrepareWorldFromFileAsync(string path)
    {
        _currentDemoId = string.Empty;
        _modelPath = path;

        // Recorded on the attempt rather than on the result. A file that fails to import is still
        // one somebody went and found, and the recent list is the shortest way back to it after
        // whatever went wrong has been dealt with.
        RememberRecentFile(path);

        return PrepareWorldCoreAsync(progress => BuildWorldFromFile(path, progress), Path.GetFileName(path));
    }

    /// <summary>
    /// Gives the scene its environment: a loaded panorama when there is one and it is selected,
    /// and otherwise a procedural sky with the sun placed where the world's first directional
    /// light points — a sky whose sun is somewhere other than where the shadows come from is the
    /// one thing that reads as obviously wrong.
    /// </summary>
    private void ApplySky(IWorld? world)
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        if (chkPanorama.Checked && _panorama is { } panorama)
        {
            // A panorama stays the scene's environment whether or not it is drawn: the sky
            // checkbox decides only whether it is *visible*, so unticking it leaves the scene lit
            // from off screen, which is what a studio backdrop is for. The procedural path below
            // drops the environment entirely instead, which is what it has always done.
            scene.Environment = panorama;
            scene.ShowSky = chkSky.Checked;
            return;
        }

        scene.ShowSky = true;

        var sunDirection = world?.Lights.OfType<DirectionalLight>().FirstOrDefault()?.Direction
            ?? new Vector3(-0.35f, -0.6f, -1f);

        // Generating the cube map walks six faces of texels, so it is kept until the sun it was
        // built around moves or the range it was built in changes.
        if (_sky is null || _skySunDirection != sunDirection || _skyIsHighDynamicRange != chkHdrSky.Checked)
        {
            _sky = chkHdrSky.Checked
                ? SkyBox.HighDynamicRangeGradient(sunDirection)
                : SkyBox.Gradient(sunDirection);

            _skySunDirection = sunDirection;
            _skyIsHighDynamicRange = chkHdrSky.Checked;
        }

        scene.Environment = chkSky.Checked ? _sky : null;
    }

    /// <summary>
    /// Opens a panorama and makes it the environment.
    ///
    /// Projecting an equirectangular image onto six cube faces walks every texel of all six and
    /// supersamples each, and the PBR painter then convolves the result once per roughness — so
    /// this is seconds of work on a large sky, and it runs off the UI thread with the progress bar
    /// showing rather than freezing the window.
    /// </summary>
    private async Task LoadPanoramaAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load panorama",
            Filter = "Panoramas (*.hdr;*.pic;*.png;*.jpg;*.jpeg;*.bmp)|*.hdr;*.pic;*.png;*.jpg;*.jpeg;*.bmp" +
                     "|Radiance HDR (*.hdr;*.pic)|*.hdr;*.pic" +
                     "|Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" +
                     "|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await LoadPanoramaAsync(dialog.FileName, announceFailure: true);
    }

    private async Task LoadPanoramaAsync(string path, bool announceFailure)
    {
        btnPanorama.Enabled = false;
        UseWaitCursor = true;

        try
        {
            var loaded = await Task.Run(() => LoadPanorama(path));

            if (loaded is null)
            {
                if (announceFailure)
                {
                    MessageBox.Show(this,
                        $"Could not read a panorama from:\n{path}",
                        "Load panorama", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                return;
            }

            _panorama = loaded;
            _panoramaPath = path;

            chkPanorama.Text = Path.GetFileName(path);
            chkPanorama.Enabled = true;
            toolTip1.SetToolTip(chkPanorama, loaded.IsHighDynamicRange
                ? $"{path}\nLinear floats: the range above white survived the load"
                : $"{path}\nEight-bit source: nothing above white to reflect");

            // Ticking the box is what puts it on screen, and having just been asked for it is as
            // clear a signal as there is that it should be.
            if (chkPanorama.Checked)
            {
                ApplySky(panel3D1.Scene?.World);
                panel3D1.Invalidate();
            }
            else
            {
                chkPanorama.Checked = true;
            }
        }
        finally
        {
            UseWaitCursor = false;
            btnPanorama.Enabled = true;
        }
    }

    /// <summary>
    /// Traces the world's bounce light into a grid of probes, off the UI thread.
    ///
    /// It is seconds of work — a few hundred probes at a hundred paths each — and unlike everything
    /// else on this panel it is not a setting but a measurement, so it has a button rather than a
    /// checkbox. The checkbox beside it decides whether the measurement is used.
    /// </summary>
    private async Task BakeIndirectLightAsync()
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        btnBake.Enabled = false;
        UseWaitCursor = true;

        try
        {
            // Resolution follows the viewport's own budget: this runs while somebody waits, and a
            // 24³ grid is fourteen thousand probes.
            var volume = await Task.Run(() => IrradianceBaker.Bake(scene, new BakeSettings
            {
                Resolution = 12,
                Rays = 128,
                Bounces = 2,
            }));

            _bakedLight = volume;

            chkBakedLight.Text = $"Baked light ({volume.ValidCount} probes)";
            chkBakedLight.Enabled = true;

            toolTip1.SetToolTip(chkBakedLight,
                $"{volume.CountX}×{volume.CountY}×{volume.CountZ} probes, {volume.ValidCount} of them " +
                "outside geometry.\nRead by the software rasterizer; the GPU backend and the path " +
                "tracer ignore it.");

            if (chkBakedLight.Checked)
            {
                ApplyBakedLight();
                panel3D1.Invalidate();
            }
            else
            {
                chkBakedLight.Checked = true;
            }
        }
        finally
        {
            UseWaitCursor = false;
            btnBake.Enabled = true;
        }
    }

    /// <summary>
    /// Hands the scene its baked light, or takes it away again.
    ///
    /// Nothing here rebakes. A volume describes one arrangement of a world, and a drag or a new
    /// model leaves it describing a room that is no longer there — which is why loading a world
    /// throws it away outright rather than quietly lighting the next one with the last one's light.
    /// </summary>
    private void ApplyBakedLight()
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        scene.Irradiance = chkBakedLight.Checked ? _bakedLight : null;
    }

    /// <summary>Forgets the bake, for when the world it measured is not the world any more.</summary>
    private void ClearBakedLight()
    {
        _bakedLight = null;

        chkBakedLight.Checked = false;
        chkBakedLight.Enabled = false;
        chkBakedLight.Text = "No baked light";

        if (panel3D1.Scene is { } scene)
        {
            scene.Irradiance = null;
        }
    }

    /// <summary>
    /// Decodes a panorama and projects it onto a cube. Radiance files go through the engine's own
    /// codec because no platform image library has a type for what is in one; everything else goes
    /// through GDI+, which reads every 8-bit format Windows knows and no HDR one.
    /// </summary>
    private static CubeMap? LoadPanorama(string path)
    {
        var extension = Path.GetExtension(path);

        try
        {
            if (extension.Equals(".hdr", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".pic", StringComparison.OrdinalIgnoreCase))
            {
                return Equirectangular.ToCubeMap(RadianceHdrCodec.Load(path));
            }

            return ImageTexture.Load(path) is { } texture
                ? Equirectangular.ToCubeMap(texture)
                : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scales the occlusion radius to the world just loaded. It is a distance in world
    /// units, and the demos span three orders of magnitude of them — a radius that finds
    /// the creases in a 2-unit skull sees nothing at all on a 1500-unit elephant. Deriving
    /// it from the distance the camera was framed at makes it one number for all of them.
    /// </summary>
    private void ApplyAmbientOcclusion()
    {
        // The radius is in the document too, for the same reason it is derived at all: it is a
        // world-space distance, and the saved one was chosen against this world.
        if (_applyingScene || panel3D1.PostProcess.Find<SsaoEffect>() is not { } ssao)
        {
            return;
        }

        var reference = panel3D1.ReferenceDistance;

        ssao.Radius = reference > 0f ? reference * 0.02f : 0.5f;
        ssao.Bias = ssao.Radius * 0.04f;
    }

    /// <summary>
    /// Scales the reflection march to the world just loaded, for the same reason and from the
    /// same reference: how far a reflected ray may travel and how thick the depth buffer's one
    /// recorded layer is taken to be are both world-space distances, and a march tuned to a
    /// skull walks off the end of an elephant in three steps.
    /// </summary>
    private void ApplyReflections()
    {
        if (_applyingScene || panel3D1.PostProcess.Find<SsrEffect>() is not { } ssr)
        {
            return;
        }

        var reference = panel3D1.ReferenceDistance;

        ssr.MaxDistance = reference > 0f ? reference : 40f;
        ssr.Thickness = ssr.MaxDistance * 0.04f;
    }

    /// <summary>
    /// True from the moment a load is asked for until its world is in the scene.
    ///
    /// <para>
    /// The load itself is awaited, so the message loop keeps running while the importer works on
    /// its own thread — and every menu item that can start another one is still live during that
    /// window. Two loads in flight interleave: each posts a camera, a framing, a projection and a
    /// world back to the UI thread independently, so the frame ends up assembled out of both, and
    /// whichever finishes first re-enables the controls for a load that is still running.
    /// Disabling the entries is what a user sees; this is what actually holds.
    /// </para>
    /// </summary>
    private bool _loading;

    private async Task PrepareWorldCoreAsync(Func<IProgress<float>?, WorldSetup> build, string label)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;

        btnLoadModel.Enabled = false;
        mnuLoadModel.Enabled = false;
        mnuOpenModel.Enabled = false;
        mnuOpenScene.Enabled = false;
        prgLoading.Value = 0;
        lblLoading.Visible = true;
        lblLoading.BringToFront();
        UseWaitCursor = true;

        try
        {
            // Progress<T> is created on the UI thread, so reports from the worker
            // are marshalled back here automatically.
            var progress = new Progress<float>(f =>
                prgLoading.Value = Math.Clamp((int)(f * prgLoading.Maximum), 0, prgLoading.Maximum));

            var setup = await Task.Run(() => build(progress));

            // Start every demo from the canonical view — without this, a previous
            // arc-ball drag stays baked into the camera orbit.
            if (panel3D1.Scene?.Camera is ArcBallCamera arcBall)
            {
                arcBall.Rotation = Quaternion.Identity;
            }
            panel3D1.Scene?.Camera.Position = setup.CameraPosition;

            // The distance a world is framed from is what the zoom readout calls 100%.
            panel3D1.ReferenceDistance = setup.CameraPosition.Length();

            // Fog distances and the shadow map's resolution are both relative to the world's
            // framing and the viewport, either of which may have changed.
            ApplyFog();
            ApplyShadows();

            // The sky is built from the new world's own key light, so the sun in it lines
            // up with the direction the scene is actually lit from.
            ApplySky(setup.World);

            ApplyAmbientOcclusion();
            ApplyReflections();

            // The grid a drag snaps to is measured in the world's own units, so it is scaled to
            // the world the same way the fog distances and the occlusion radius are.
            ApplySnapScale();

            // The edits on the stack move meshes that are about to leave the scene. Undoing one
            // then would quietly transform an object nothing draws — a change with no visible
            // effect, which is the worst kind for a history to offer.
            _history.Clear();

            // Every load sets a projection: either the demo's own, or one whose far plane
            // is derived from the world's extent — a far plane closer than the world's
            // farthest geometry visibly slices models while they are orbited, and the
            // previous world's projection must not leak into this one.
            panel3D1.Scene?.Projection = setup.Projection ?? ProjectionFor(setup);

            // Before the world goes in, not after: the pick addresses meshes by their position
            // in the list that is about to be replaced.
            panel3D1.ClearPick();

            panel3D1.Scene?.World = setup.World;

            // The probes measured the light in the world that just left. Keeping them would light
            // this one with the last one's bounce light, which is wrong in a way that looks like a
            // shading bug rather than like stale data.
            ClearBakedLight();

            panel3D1.RendererSettings.SkeletonTickSize = setup.SkeletonTickSize;

            // The clock only runs for a world that has something to play, so a static model
            // costs nothing — and an animated one starts moving the moment it is loaded.
            panel3D1.SyncAnimationTimer();

            lblCurrentModel.Text = label;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load '{label}': {ex.Message}", "Load error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            lblLoading.Visible = false;
            btnLoadModel.Enabled = true;
            mnuLoadModel.Enabled = true;
            mnuOpenModel.Enabled = true;
            mnuOpenScene.Enabled = true;

            _loading = false;

            // The world changed under any selected pixel, and its history with it.
            panel3D1.ClearSelectedPixel();
            panel3D1.Invalidate();
        }
    }

    /// <summary>
    /// A projection whose far plane contains the whole world from anywhere on the camera's
    /// orbit: the camera distance plus the world's farthest geometry, with headroom so
    /// dollying out a little doesn't immediately clip.
    /// </summary>
    private static PerspectiveProjection ProjectionFor(WorldSetup setup)
    {
        var worldRadius = 0f;
        foreach (var mesh in setup.World.Meshes)
        {
            var scale = Math.Max(Math.Abs(mesh.Scale.X), Math.Max(Math.Abs(mesh.Scale.Y), Math.Abs(mesh.Scale.Z)));
            var reach = mesh.Position.Length() + mesh.BoundingRadius * scale;

            if (!float.IsNaN(reach) && !float.IsInfinity(reach))
            {
                worldRadius = Math.Max(worldRadius, reach);
            }
        }

        var far = Math.Max(500f, (setup.CameraPosition.Length() + worldRadius) * 2f);

        return new PerspectiveProjection(FieldOfView, .01f, far);
    }

    /// <summary>
    /// Bundled models are copied next to the executable, so resolve them from the install
    /// directory — the process working directory is whatever the app was launched from.
    /// </summary>
    private static string ModelPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Models", fileName);

    /// <summary>
    /// A sway for Juliet, built against the joint names her rig actually uses. Her file has a
    /// skin but no animation — which is the common case for a downloaded character — so this
    /// is what a clip authored for an imported rig looks like.
    ///
    /// Every key is the joint's <em>rest</em> orientation with the sway composed on top. A
    /// clip read from a file holds absolute orientations because the file authored all of
    /// them; one written by hand against someone else's rig must not, or it would discard the
    /// pose she was modelled in and fold her into a heap.
    /// </summary>
    private static AnimationClip JulietPose(SceneNode root)
    {
        const float period = 4.5f;
        const int keyCount = 32;

        (string Joint, Vector3 Axis, float Degrees, float Phase)[] motions =
        [
            ("spineAJT", Vector3.UnitZ, 4f, 0f),
            ("spineBJT", Vector3.UnitZ, 5f, 0.35f),
            ("spineCJT", Vector3.UnitZ, 5f, 0.7f),
            ("neckJT", Vector3.UnitZ, 4f, 1.05f),
            ("armJTL", Vector3.UnitZ, 12f, 0.5f),
            ("elbowJTL", Vector3.UnitZ, 14f, 1.1f),
            ("armJTR", Vector3.UnitZ, -12f, 0.5f),
            ("elbowJTR", Vector3.UnitZ, -14f, 1.1f),
        ];

        var channels = new List<NodeChannel>(motions.Length);

        foreach (var (jointName, axis, degrees, phase) in motions)
        {
            if (root.Find(jointName) is not { } joint)
            {
                continue;
            }

            var rest = joint.Rotation;
            var amplitude = degrees * MathF.PI / 180f;

            var times = new float[keyCount + 1];
            var rotations = new Quaternion[keyCount + 1];

            for (var key = 0; key <= keyCount; key++)
            {
                times[key] = period * key / keyCount;

                var angle = amplitude * MathF.Sin(MathF.Tau * key / keyCount + phase);

                rotations[key] = Quaternion.Concatenate(rest, Quaternion.CreateFromAxisAngle(axis, angle));
            }

            channels.Add(new NodeChannel(joint.Name)
            {
                Rotation = new QuaternionTrack(times, rotations),
            });
        }

        return new AnimationClip("Sway", channels);
    }

    /// <summary>
    /// The scale a marker mesh needs to come out <paramref name="size"/> units across when it
    /// is parented to <paramref name="node"/>.
    ///
    /// A child inherits everything its parent's transform does, scale included — and exported
    /// rigs routinely carry a unit conversion on their top node, a factor of 100 in the
    /// parrot's case. A marker that ignores that is a hundred times too big on exactly the
    /// nodes it is meant to label. Dividing the node's own scale back out is what makes a
    /// marker mean "here", rather than "here, at whatever size this branch happens to use".
    /// </summary>
    private static Vector3 MarkerScale(SceneNode node, float size)
    {
        if (!Matrix4x4.Decompose(node.WorldMatrix, out var scale, out _, out _))
        {
            return new Vector3(size);
        }

        return new Vector3(
            size / MathF.Max(MathF.Abs(scale.X), 1e-4f),
            size / MathF.Max(MathF.Abs(scale.Y), 1e-4f),
            size / MathF.Max(MathF.Abs(scale.Z), 1e-4f));
    }

    private static WorldSetup BuildWorld(string id, IProgress<float>? progress)
    {
        var world = new SimpleWorld();
        var cameraPosition = new Vector3(0, 0, -60);
        PerspectiveProjection? projection = null;

        switch (id)
        {
            case "skull":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("skull.dae"), progress));
                cameraPosition = new Vector3(0, 0, -5);
                break;

            case "parrot":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("parrot.dae"), progress));
                cameraPosition = new Vector3(0, 0, -500);

                // A warm key and a cool fill from the other side — the classic two-light
                // setup, and the clearest demonstration that lights sum and carry colour.
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(150, 200, 400),
                    Color = new ColorRGB(255, 236, 205),
                });
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(-300, 100, -200),
                    Color = new ColorRGB(120, 170, 255),
                    Intensity = 0.55f,
                });
                break;

            case "teapot":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("teapot.dae"), progress));
                break;

            case "elefant":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("elefant.dae"), progress));
                cameraPosition = new Vector3(0, 0, -1500);
                projection = new PerspectiveProjection(FieldOfView, .01f, 65535f);
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(500, 800, 1200),
                    Color = new ColorRGB(255, 240, 214),
                });
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(-900, 300, -600),
                    Color = new ColorRGB(130, 175, 255),
                    Intensity = 0.5f,
                });
                break;

            case "Juliet":
                world.Meshes.AddRange(ColladaImporter.HackyImportCollada(ModelPath("Juliet.dae"), progress));
                cameraPosition = new Vector3(0, 0, -500);
                world.Lights.Add(new PointLight { Position = new Vector3(150, 200, 400) });
                break;

            case "bonechain":
            {
                // Nothing is loaded: the geometry, the rig and the clip are all generated, so
                // this demo shows the skinning path with no importer between it and the eye.
                const int bones = 7;

                var rig = BoneChain.Create(bones, boneLength: 2.2f, radius: 0.75f, sides: 20);

                world.Root = rig.Root;
                world.Meshes.Add(rig.Mesh);
                world.Players.Add(new AnimationPlayer(rig.Root, BoneChain.Wave(bones)));

                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(12, 20, -18),
                    Color = new ColorRGB(255, 238, 210),
                });
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(-16, 6, 14),
                    Color = new ColorRGB(130, 175, 255),
                    Intensity = 0.5f,
                });

                cameraPosition = new Vector3(0, 8, -34);
                return new WorldSetup(world, cameraPosition, null) { SkeletonTickSize = 0.9f };
            }

            case "julietskin":
            {
                // A real 55,000-vertex skin off a real file — 205 joints, weights painted by
                // whoever rigged her. The file carries no animation, so the clip that bends
                // her is generated against the joint names the rig actually uses.
                var scene = ColladaImporter.ImportScene(ModelPath("Juliet.dae"), progress);

                world.Root = scene.Root;
                world.Meshes.AddRange(scene.Meshes);
                world.Players.Add(new AnimationPlayer(scene.Root, JulietPose(scene.Root)));

                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(150, 200, 400),
                    Color = new ColorRGB(255, 240, 220),
                });
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(-250, 120, -200),
                    Color = new ColorRGB(140, 180, 255),
                    Intensity = 0.5f,
                });

                cameraPosition = new Vector3(0, 0, -320);
                return new WorldSetup(world, cameraPosition, null) { SkeletonTickSize = 3f };
            }

            case "parrotanim":
            {
                // The parrot's file has the opposite half of the problem from Juliet's: a
                // twelve-second clip over a sixty-node rig, and no skin binding the mesh to
                // any of it — so there is nothing for the pose to deform, and the bird itself
                // would stand still while its skeleton danced inside it.
                //
                // A cube on each joint makes the hierarchy the model. Every cube is placed by
                // its node and nothing else, which is the scene graph doing its whole job:
                // move a wing joint and the four cubes below it go with it.
                var scene = ColladaImporter.ImportScene(ModelPath("parrot.dae"), progress);

                world.Root = scene.Root;

                foreach (var node in scene.Root.SelfAndDescendants())
                {
                    if (node.Kind is SceneNodeKind.Light or SceneNodeKind.Camera || ReferenceEquals(node, scene.Root))
                    {
                        continue;
                    }

                    world.Meshes.Add(new Cube { Parent = node, Scale = MarkerScale(node, 2.2f) });
                }

                foreach (var clip in scene.Clips)
                {
                    world.Players.Add(new AnimationPlayer(scene.Root, clip));
                }

                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(150, 200, 400),
                    Color = new ColorRGB(255, 236, 205),
                });
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(-300, 100, -200),
                    Color = new ColorRGB(120, 170, 255),
                    Intensity = 0.55f,
                });

                cameraPosition = new Vector3(0, 0, -230);
                return new WorldSetup(world, cameraPosition, null) { SkeletonTickSize = 5f };
            }

            case "empty":
                break;

            case "town":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 50;
                var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "littletown":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 10;
                var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "bigtown":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.6f, -1f, -0.8f) });
                var d = 200;
                var s = 2;
                for (var x = -d; x <= d; x += s)
                {
                    for (var z = -d; z <= d; z += s)
                    {
                        world.Meshes.Add(new Cube()
                        {
                            Position = new Vector3(x, 0, z),
                            // Scale = new Vector3(1, r.Next(1, 50), 1)
                        });
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "cube":
                world.Meshes.Add(new Cube());
                break;

            case "bigcube":
                world.Meshes.Add(new Cube() { Scale = new Vector3(100, 100, 100) });
                break;

            case "texturedcube":
                world.Meshes.Add(new TexturedCube
                {
                    Scale = new Vector3(20, 20, 20),
                    Rotation = new Rotation3D(25, 35, 0).ToRad(),
                });
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });
                break;

            case "primitives":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.8f, 0.4f) });

                // One texture across all of them, because the point of the scene is the UVs:
                // a checker shows a stretched pole, a mirrored seam or a twisted cap at a
                // glance, and a flat colour hides all three.
                var checker = Texture.Checkerboard(256, 8, new ColorRGB(225, 225, 230), new ColorRGB(98, 88, 158));

                world.Meshes.Add(new PlaneMesh(48f, 48f, 8, 8, uvScale: 12f)
                {
                    Position = new Vector3(0, -3f, 0),
                    Texture = checker,
                });

                world.Meshes.Add(new UvSphere(1.6f) { Position = new Vector3(-7.5f, -1.4f, 0), Texture = checker });
                world.Meshes.Add(new Cylinder(1.4f, 3.2f) { Position = new Vector3(-2.5f, -1.4f, 0), Texture = checker });
                world.Meshes.Add(new Cone(1.5f, 3.2f) { Position = new Vector3(2.5f, -1.4f, 0), Texture = checker });
                world.Meshes.Add(new Torus(1.5f, 0.5f)
                {
                    Position = new Vector3(7.5f, -1f, 0),
                    Rotation = new Rotation3D(70, 0, 0).ToRad(),
                    Texture = checker,
                });

                cameraPosition = new Vector3(0, 2f, -16f);
                break;
            }

            case "transparency":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.7f, -1f) });

                var floor = new Cube { Position = new Vector3(0, -3.5f, 0), Scale = new Vector3(14, 0.5f, 14) };
                Array.Fill(floor.TriangleColors, ColorRGB.Gray);
                world.Meshes.Add(floor);

                var solid = new IcoSphere(2) { Position = new Vector3(0, 0, 2.5f), Scale = new Vector3(1.5f, 1.5f, 1.5f) };
                Array.Fill(solid.TriangleColors, new ColorRGB(220, 60, 50));
                world.Meshes.Add(solid);

                var glass = new IcoSphere(2) { Position = new Vector3(-1.8f, 0, 0), Scale = new Vector3(2, 2, 2), Opacity = 0.55f };
                Array.Fill(glass.TriangleColors, new ColorRGB(70, 200, 120));
                world.Meshes.Add(glass);

                var mist = new IcoSphere(2) { Position = new Vector3(1.8f, 0, -1f), Scale = new Vector3(2, 2, 2), Opacity = 0.35f };
                Array.Fill(mist.TriangleColors, new ColorRGB(80, 140, 255));
                world.Meshes.Add(mist);

                cameraPosition = new Vector3(0, 0, -12);
                break;
            }

            case "shadows":
            {
                // Nearly overhead and tilted toward the camera, so a caster's shadow lands
                // in front of it rather than behind it where it cannot be seen.
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.3f, -1f, 0.35f) });

                var ground = new Cube { Position = new Vector3(0, -4f, 0), Scale = new Vector3(26, 0.5f, 26) };
                Array.Fill(ground.TriangleColors, new ColorRGB(190, 188, 182));
                world.Meshes.Add(ground);

                var pillar = new Cube { Position = new Vector3(-5.5f, -1.2f, -1f), Scale = new Vector3(1.4f, 5f, 1.4f) };
                Array.Fill(pillar.TriangleColors, new ColorRGB(150, 120, 90));
                world.Meshes.Add(pillar);

                // Everything else floats well clear of the ground: a caster resting on the
                // floor hides its own shadow under itself.
                var beam = new Cube
                {
                    Position = new Vector3(1f, 3f, -1f),
                    Scale = new Vector3(9f, 0.5f, 0.6f),
                    Rotation = new Rotation3D(0, 0, 10).ToRad(),
                };
                Array.Fill(beam.TriangleColors, new ColorRGB(150, 120, 90));
                world.Meshes.Add(beam);

                var ball = new IcoSphere(3) { Position = new Vector3(2f, 0.2f, 3f), Scale = new Vector3(1.8f, 1.8f, 1.8f) };
                Array.Fill(ball.TriangleColors, new ColorRGB(200, 70, 60));
                world.Meshes.Add(ball);

                var small = new IcoSphere(3) { Position = new Vector3(-2f, -0.8f, 1.5f) };
                Array.Fill(small.TriangleColors, new ColorRGB(70, 150, 210));
                world.Meshes.Add(small);

                cameraPosition = new Vector3(0, 0, -24);
                break;
            }

            case "cascades":
            {
                // A colonnade running away from the eye for three hundred units — the case one
                // shadow map cannot serve. Fitted to the whole scene, its texels are metres
                // across and the near pillars' shadows come out as staircases; split into
                // cascades, the first buffer covers only the few units in front of the camera
                // and the same resolution lands where the pixels are.
                //
                // Switch the cascade count in the sidebar and watch the nearest shadow edge;
                // the Shadow map buffer view shows each cascade's own square beside the others.
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -1f, -0.15f) });

                world.Meshes.Add(ColoredBox(
                    new Vector3(0, -6f, -150f),
                    new Vector3(60f, 1f, 340f),
                    new ColorRGB(190, 188, 182)));

                for (var i = 0; i < 24; i++)
                {
                    var z = -8f - i * 13f;

                    // The far pillars are drawn in the same colours as the near ones, so any
                    // difference down the row is the shadowing rather than the shading.
                    world.Meshes.Add(ColoredBox(
                        new Vector3(-9f, -1.5f, z),
                        new Vector3(2.4f, 8f, 2.4f),
                        new ColorRGB(150, 120, 90)));

                    world.Meshes.Add(ColoredBox(
                        new Vector3(9f, -1.5f, z),
                        new Vector3(2.4f, 8f, 2.4f),
                        new ColorRGB(150, 120, 90)));

                    world.Meshes.Add(ColoredBox(
                        new Vector3(0f, 3f, z),
                        new Vector3(22f, 1f, 1.6f),
                        new ColorRGB(170, 140, 110)));
                }

                cameraPosition = new Vector3(0, -1f, 16f);
                break;
            }

            case "normalmapping":
            {
                world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.4f, -0.35f, -1f) });

                // The same albedo on both cubes, so the only difference on screen is the
                // normal map — the point being that it costs no extra geometry.
                var albedo = Texture.Checkerboard(256, 8, new ColorRGB(210, 205, 195), new ColorRGB(150, 145, 138));
                var normals = NormalMapBuilder.FromHeight(Texture.Bumps(256, 8), 3f);

                var bumpy = new TexturedCube
                {
                    Position = new Vector3(-1.2f, 0, 0),
                    Scale = new Vector3(18, 18, 18),
                    Rotation = new Rotation3D(20, 30, 0).ToRad(),
                };
                bumpy.Material.DiffuseMap = albedo;
                bumpy.Material.NormalMap = normals;
                bumpy.Material.SpecularStrength = 0.5f;
                world.Meshes.Add(bumpy);

                var flat = new TexturedCube
                {
                    Position = new Vector3(24f, 0, 0),
                    Scale = new Vector3(18, 18, 18),
                    Rotation = new Rotation3D(20, 30, 0).ToRad(),
                };
                flat.Material.DiffuseMap = albedo;
                flat.Material.SpecularStrength = 0.5f;
                world.Meshes.Add(flat);

                cameraPosition = new Vector3(-11f, 0, -70);
                break;
            }

            case "pbrspheres":
            {
                // The chart every physically-based renderer is checked against: one albedo,
                // one lighting setup, and the two parameters that describe the surface varied
                // across the grid. Roughness runs left to right, metalness bottom to top.
                //
                // What it is for is that the two rows are supposed to look like different
                // *materials* rather than like the same material at two brightnesses — the
                // metals lose their diffuse entirely and tint what they reflect, and every
                // sphere on the top row is showing you the sky rather than the lights.
                const int columns = 6;
                const int rows = 3;
                const float spacing = 2.6f;

                var albedo = new ColorRGB(222, 180, 140);

                for (var row = 0; row < rows; row++)
                {
                    for (var column = 0; column < columns; column++)
                    {
                        var sphere = new IcoSphere(3)
                        {
                            Position = new Vector3(
                                (column - (columns - 1) / 2f) * spacing,
                                (row - (rows - 1) / 2f) * spacing,
                                0f),
                        };

                        sphere.Material.Diffuse = albedo;
                        sphere.Material.Metallic = rows == 1 ? 0f : row / (float)(rows - 1);

                        // Away from 0 at the smooth end: a perfect mirror lit by point lights
                        // shows no highlight at all, which reads as a bug rather than as the
                        // consequence of a light with no area that it is.
                        sphere.Material.Roughness = 0.06f + 0.94f * column / (columns - 1);

                        world.Meshes.Add(sphere);
                    }
                }

                // A key and a fill, so the highlights have somewhere to be. Most of what the
                // metals show, though, comes from the environment rather than from these.
                world.Lights.Add(new DirectionalLight
                {
                    Direction = new Vector3(-0.4f, -0.5f, 1f),
                    Color = new ColorRGB(255, 244, 224),
                });
                world.Lights.Add(new PointLight
                {
                    Position = new Vector3(-14f, 6f, -14f),
                    Color = new ColorRGB(150, 185, 255),
                    Intensity = 0.5f,
                });

                cameraPosition = new Vector3(0, 0, -24f);
                break;
            }

            case "spheres":
            {
                int d = 5;
                int s = 2;
                for (int x = -d; x <= d; x += s)
                {
                    for (int y = -d; y <= d; y += s)
                    {
                        for (int z = -d; z <= d; z += s)
                        {
                            world.Meshes.Add(new IcoSphere(2)
                            {
                                Position = new Vector3(x, y, z)
                            });
                        }
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }

            case "cubes":
            {
                var d = 20;
                var s = 2;
                var r = new Random();
                for (int x = -d; x <= d; x += s)
                {
                    for (int y = -d; y <= d; y += s)
                    {
                        for (int z = -d; z <= d; z += s)
                        {
                            world.Meshes.Add(new Cube()
                            {
                                Position = new Vector3(x, y, z),
                                Rotation = new Rotation3D(
                                    r.Next(-90, 90),
                                    r.Next(-90, 90),
                                    r.Next(-90, 90)).ToRad()
                            });
                        }
                    }
                    progress?.Report((x + d) / (float)(2 * d));
                }
                break;
            }
        }

        return new WorldSetup(world, cameraPosition, projection);
    }

    /// <summary>
    /// A cube in one colour, as a mesh of its own.
    ///
    /// <see cref="Cube"/> instances share a single static colour array between them, so
    /// <c>Array.Fill</c> on one cube's colours recolours every cube in the world. A scene that
    /// wants each box a different colour therefore has to bring its own array — the geometry
    /// is still shared, since nothing in the pipeline writes to a vertex.
    /// </summary>
    private static Mesh ColoredBox(Vector3 position, Vector3 scale, ColorRGB color)
    {
        var source = new Cube();

        var colors = new ColorRGB[source.Triangles.Length];
        Array.Fill(colors, color);

        return new Mesh(source.Vertices, source.Triangles, source.NormVertices, colors)
        {
            Position = position,
            Scale = scale,
        };
    }

    /// <summary>
    /// Loads a model file (OBJ, Collada or glTF) into a fresh world, framing the camera and
    /// depth range from the model's own size so any scale of mesh shows up on load.
    /// </summary>
    private static WorldSetup BuildWorldFromFile(string path, IProgress<float>? progress)
    {
        var world = new SimpleWorld();

        // glTF is the one format here that carries a whole scene rather than a pile of
        // meshes, so it is read as one: the node tree becomes the world's root, the skins
        // deform against it, and any clip in the file starts playing.
        if (GltfImporter.Handles(path))
        {
            var scene = GltfImporter.Import(path, progress, ImageTexture.Load);

            world.Root = scene.Root;
            world.Meshes.AddRange(scene.Meshes);

            foreach (var clip in scene.Clips)
            {
                world.Players.Add(new AnimationPlayer(scene.Root, clip));
            }
        }
        else
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            world.Meshes.AddRange(extension switch
            {
                ".obj" => ObjImporter.Import(path, progress, ImageTexture.Load),
                ".dae" => ColladaImporter.HackyImportCollada(path, progress),
                _ => throw new NotSupportedException($"Unsupported model format '{extension}'."),
            });
        }

        world.Lights.Add(new DirectionalLight { Direction = new Vector3(-0.35f, -0.5f, -1f) });

        // Frame the model: pull the camera back proportional to its extent and push the far
        // plane out far enough to contain it, whatever units the file uses. The extent is
        // measured in world space, since a glTF's node tree routinely scales its meshes.
        var radius = 0f;
        foreach (var mesh in world.Meshes)
        {
            var scaled = mesh.WorldBoundingRadius();

            if (float.IsFinite(scaled))
            {
                radius = Math.Max(radius, mesh.WorldMatrix.Translation.Length() + scaled);
            }
        }

        if (radius <= 0f)
        {
            radius = 1f;
        }

        var cameraPosition = new Vector3(0, 0, -radius * 3f);
        var projection = new PerspectiveProjection(FieldOfView, .01f, Math.Max(500f, radius * 20f));

        return new WorldSetup(world, cameraPosition, projection) { SkeletonTickSize = radius * 0.05f };
    }
}
