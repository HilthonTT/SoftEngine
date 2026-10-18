using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes.Serialization;
using SoftEngine.WinForms.Cameras;
using System.Text.Json;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private const string SceneFilter = "SoftEngine scene (*.scene.json)|*.scene.json|JSON (*.json)|*.json|All files (*.*)|*.*";

    private bool _applyingScene;

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
        document.Rendering.AnisotropicFiltering = chkAnisotropic.Checked;
        document.Rendering.Animate = chkAnimate.Checked;

        document.Environment ??= new EnvironmentState();
        document.Environment.ShowSky = chkSky.Checked;

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

    private void ApplyScene(SceneDocument document)
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        _applyingScene = true;

        try
        {
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

            ApplySky(scene.World);
        }
        finally
        {
            _applyingScene = false;
        }

        panel3D1.ResetTemporalHistory();

        _history.Clear();
        panel3D1.ClearPick();
        panel3D1.ClearSelectedPixel();

        panel3D1.SyncAnimationTimer();
        panel3D1.Invalidate();

        UpdateStatus();
    }

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
            chkAnisotropic.Checked = rendering.AnisotropicFiltering;
            UpdateFilteringControls();
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

    private void SelectPainter(string? name)
    {
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
}
