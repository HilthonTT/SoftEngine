using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Controls;
using SoftEngine.WinForms.Dialogs;
using SoftEngine.WinForms.Rendering;
using SoftEngine.WinForms.Worlds;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen : Form
{
    private readonly Label lblLoading;
    private readonly FlatProgressBar prgLoading;

    /// <summary>Id of the bundled world on screen, so the picker reopens on it.</summary>
    private string _currentDemoId = "skull";

    /// <summary>Path of the model file on screen, when the world came from one rather than from a demo.</summary>
    private string? _modelPath;

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

        var currentPainter = PainterNames.KindOf(panel3D1.Painter);

        foreach (var (radio, kind) in ShadingRadios())
        {
            radio.Checked = kind == currentPainter;

            radio.CheckedChanged += (s, e) =>
            {
                if (s is not RadioButton { Checked: true })
                {
                    return;
                }
                panel3D1.Painter = CreatePainter(kind);
                panel3D1.Invalidate();
            };
        }

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
            Projection = new PerspectiveProjection(DemoWorlds.FieldOfView, .01f, 500f),
            Camera = new ArcBallCamera(panel3D1) { Position = new Vector3(0, 0, -60), FieldOfView = DemoWorlds.FieldOfView },
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

    /// <summary>Opens the model picker: the bundled worlds, or a file from the machine.</summary>
    private async Task ShowModelPickerAsync()
    {
        using var dialog = new ModelPickerDialog(DemoWorlds.Demos, _currentDemoId);

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

    private (RadioButton Radio, PainterKind Kind)[] ShadingRadios() =>
    [
        (rdbNoneShading, PainterKind.None),
        (rdbClassicShading, PainterKind.Classic),
        (rdbFlatShading, PainterKind.Flat),
        (rdbGouraudShading, PainterKind.Gouraud),
        (rdbPhongShading, PainterKind.Phong),
        (rdbTexturedShading, PainterKind.Textured),
        (rdbMaterialShading, PainterKind.Material),
        (rdbPbrShading, PainterKind.Pbr),
    ];

    private IPainter? CreatePainter(PainterKind kind)
    {
        var painter = PainterNames.Create(kind);
        ApplyTextureFiltering(painter);
        return painter;
    }

    /// <summary>Applies the filtering checkboxes to whichever painter samples textures, if any.</summary>
    private void ApplyTextureFiltering(IPainter? painter)
    {
        var filtering = PainterNames.Filtering(chkTextureFiltering.Checked, chkTrilinear.Checked);

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
        scene.Shadows.Resolution = WorldScale.ShadowResolution(panel3D1.BufferSize.Width);

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
            fog.Mode = FogMode.Linear;
            (fog.Start, fog.End) = WorldScale.FogRange(panel3D1.ReferenceDistance);
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

        string label = DemoWorlds.Demos.FirstOrDefault(demo => demo.Id == id)?.Display ?? id;

        return PrepareWorldCoreAsync(progress => DemoWorlds.BuildWorld(id, progress), label);
    }

    private Task PrepareWorldFromFileAsync(string path)
    {
        _currentDemoId = string.Empty;
        _modelPath = path;

        // Recorded on the attempt rather than on the result. A file that fails to import is still
        // one somebody went and found, and the recent list is the shortest way back to it after
        // whatever went wrong has been dealt with.
        RememberRecentFile(path);

        return PrepareWorldCoreAsync(progress => DemoWorlds.BuildWorldFromFile(path, progress), Path.GetFileName(path));
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

        (ssao.Radius, ssao.Bias) = WorldScale.AmbientOcclusion(panel3D1.ReferenceDistance);
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

        (ssr.MaxDistance, ssr.Thickness) = WorldScale.Reflections(panel3D1.ReferenceDistance);
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
            panel3D1.Scene?.Projection = setup.Projection ?? DemoWorlds.ProjectionFor(setup);

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
}
