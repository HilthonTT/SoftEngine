using SoftEngine.Core.Baking;
using SoftEngine.Core.Imaging;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Textures;
using SoftEngine.WinForms.Interop;
using System.Numerics;

namespace SoftEngine.WinForms;

public sealed partial class MainScreen
{
    private CubeMap? _sky;
    private Vector3 _skySunDirection;
    private bool _skyIsHighDynamicRange;

    private CubeMap? _panorama;
    private string? _panoramaPath;

    private Core.Shading.IrradianceVolume? _bakedLight;

    private void ApplySky(IWorld? world)
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        if (chkPanorama.Checked && _panorama is { } panorama)
        {
            scene.Environment = panorama;
            scene.ShowSky = chkSky.Checked;
            return;
        }

        scene.ShowSky = true;

        var sunDirection = world?.Lights.OfType<DirectionalLight>().FirstOrDefault()?.Direction
            ?? new Vector3(-0.35f, -0.6f, -1f);

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

    private void ApplyBakedLight()
    {
        if (panel3D1.Scene is not { } scene)
        {
            return;
        }

        scene.Irradiance = chkBakedLight.Checked ? _bakedLight : null;
    }

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

    private void ApplyAmbientOcclusion()
    {
        if (_applyingScene || panel3D1.PostProcess.Find<SsaoEffect>() is not { } ssao)
        {
            return;
        }

        var reference = panel3D1.ReferenceDistance;

        ssao.Radius = reference > 0f ? reference * 0.02f : 0.5f;
        ssao.Bias = ssao.Radius * 0.04f;
    }

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
