using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Textures;
using SoftEngine.WinForms.Cameras;
using SoftEngine.WinForms.Controls;
using SoftEngine.WinForms.Dialogs;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen : Form
{
    /// <summary>
    /// The vertical field of view every world is rendered with. The camera solves its pan
    /// against this too, so the two have to stay the same number.
    /// </summary>
    private const float FieldOfView = 40f * MathF.PI / 180f;

    private readonly Label lblLoading;
    private readonly FlatProgressBar prgLoading;

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
}
