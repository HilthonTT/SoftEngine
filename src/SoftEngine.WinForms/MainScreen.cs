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
using SoftEngine.WinForms.Demos;
using SoftEngine.WinForms.Dialogs;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen : Form
{
    private const float FieldOfView = DemoDefaults.FieldOfView;

    private readonly Label lblLoading;
    private readonly FlatProgressBar prgLoading;

    private string _currentDemoId = "skull";

    private string? _modelPath;

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

        InitializeWorkspace();

        _ = PrepareWorldAsync("skull");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        pnlSidebar.AutoScrollPosition = Point.Empty;
        panel3D1.Focus();
    }

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

    private TexturedPainter CreateTexturedPainter()
    {
        var painter = new TexturedPainter();
        ApplyTextureFiltering(painter);
        return painter;
    }

    private MaterialPainter CreateMaterialPainter()
    {
        var painter = new MaterialPainter();
        ApplyTextureFiltering(painter);
        return painter;
    }

    private PbrPainter CreatePbrPainter()
    {
        var painter = new PbrPainter();
        ApplyTextureFiltering(painter);
        return painter;
    }

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

    private sealed record BufferViewChoice(string Label, DebugView View)
    {
        public override string ToString() => Label;
    }

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

    private void ApplyShadows()
    {
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

    private void ApplyFog()
    {
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
