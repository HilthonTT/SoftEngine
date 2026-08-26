using SoftEngine.Core.Pipeline.Debugging;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Painters;
using SoftEngine.Core.Scenes.Serialization;
using SoftEngine.WinForms.Cameras;
using System.Text.Json;

namespace SoftEngine.WinForms;

/// <summary>
/// Scene documents: writing what is on screen out as JSON, and putting one back.
///
/// <para>
/// Kept apart from <c>MainScreen.cs</c> because loading a scene runs the viewer backwards.
/// That file wires a control to the pipeline; this one drives the same controls from a file, and
/// the two directions need different care — see <see cref="_applyingScene"/> for the handlers
/// that have to sit out a load, and <see cref="SyncControlsToScene"/> for why the sidebar is
/// brought into agreement rather than left to catch up.
/// </para>
///
/// <para>
/// Named <c>MainScreenScenes.cs</c> rather than <c>MainScreen.Scenes.cs</c> for the reason
/// spelled out in <c>MainScreenWorkspace.cs</c>: a dotted partial of a <see cref="Form"/> invites
/// Visual Studio to generate a <c>.resx</c> whose resource name collides with the form's own.
/// </para>
/// </summary>
public sealed partial class MainScreen
{
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
}
