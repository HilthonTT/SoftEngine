using SoftEngine.Core.Baking;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;
using SoftEngine.WinForms.Interop;
using System.Numerics;

namespace SoftEngine.WinForms;

/// <summary>
/// What lights the world other than its own lamps: the sky the scene sits under, a panorama
/// loaded in place of it, the indirect light baked out of either, and the two screen-space
/// effects whose reach is a world distance and so has to be measured against the world.
///
/// <para>
/// Kept apart from <c>MainScreen.cs</c> because all of it is derived rather than set. Every
/// method here answers "given the world that was just loaded, what should this be?" — which is
/// also why each one sits out a scene load: a document carries the numbers it was saved with,
/// and deriving them again from the framing would throw those away.
/// </para>
///
/// <para>
/// Named <c>MainScreenEnvironment.cs</c> rather than <c>MainScreen.Environment.cs</c> for the
/// reason spelled out in <c>MainScreenWorkspace.cs</c>: a dotted partial of a <see cref="Form"/>
/// invites Visual Studio to generate a <c>.resx</c> whose resource name collides with the form's
/// own.
/// </para>
/// </summary>
public sealed partial class MainScreen
{
    /// <summary>The generated sky, and the sun direction and range it was generated around.</summary>
    private CubeMap? _sky;
    private Vector3 _skySunDirection;
    private bool _skyIsHighDynamicRange;

    /// <summary>A loaded panorama and where it came from, or null until one is opened.</summary>
    private CubeMap? _panorama;
    private string? _panoramaPath;

    /// <summary>The last bake of the current world, or null when nothing has been measured yet.</summary>
    private Core.Shading.IrradianceVolume? _bakedLight;

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
}
