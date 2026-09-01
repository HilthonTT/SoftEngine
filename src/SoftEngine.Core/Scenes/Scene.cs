using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes.Cameras;
using SoftEngine.Core.Scenes.Projections;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;

namespace SoftEngine.Core.Scenes;

public sealed class Scene
{
    public ICamera Camera { get; set; } = default!;

    public IWorld World { get; set; } = default!;

    public IProjection Projection { get; set; } = default!;

    public FrameBuffer Surface { get; set; } = default!;

    public FogSettings Fog { get; set; } = new();

    public ShadowSettings Shadows { get; set; } = new();

    public ShadowMap? ShadowMap { get; set; }

    public bool GammaCorrect { get; set; }

    public bool HighDynamicRange { get; set; }

    public CubeMap? Environment { get; set; }

    public bool ShowSky { get; set; } = true;

    public float SkyIntensity { get; set; } = 1f;

    public bool AmbientFromEnvironment { get; set; } = true;

    public float AmbientIntensity { get; set; } = 0.35f;

    public Shading.IrradianceVolume? Irradiance { get; set; }
}
