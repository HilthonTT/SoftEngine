using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

public abstract class LitPainter(ILight? light, float ambient) : IPainter
{
    private readonly ILight _fallback = light ?? SceneLights.Default;

    private RasterState _fogState;

    public ILight? FallbackLight => _fallback;

    private ShaderLight[] _lightStorage = [];

    private CubeMap? _ambientSource;
    private float _ambientIntensity = float.NaN;
    private AmbientCube _ambientCube;

    protected LightSet Lights { get; private set; }

    protected ILight Light { get; private set; } = light ?? SceneLights.Default;

    protected float Ambient { get; } = ambient;

    public float AmbientLevel => Ambient;

    protected AmbientField AmbientLight { get; private set; }

    protected bool GammaCorrect { get; private set; }

    protected ShadowMap? Shadows { get; private set; }

    public void Prepare(Scene scene)
    {
        Light = SceneLights.Resolve(scene.World, _fallback);
        Lights = LightSet.Build(scene.World, _fallback, ref _lightStorage);

        _fogState = RasterState.From(scene);
        GammaCorrect = scene.GammaCorrect;
        Shadows = scene.ShadowMap;
        AmbientLight = ResolveAmbient(scene);

        PrepareCore(scene);
    }

    private AmbientField ResolveAmbient(Scene scene)
    {
        if (scene.Irradiance is { } volume)
        {
            return new AmbientField(volume);
        }

        if (scene.Environment is not { } environment || !scene.AmbientFromEnvironment)
        {
            return new AmbientCube(Ambient);
        }

        if (!ReferenceEquals(environment, _ambientSource) || scene.AmbientIntensity != _ambientIntensity)
        {
            _ambientSource = environment;
            _ambientIntensity = scene.AmbientIntensity;
            _ambientCube = AmbientCube.FromEnvironment(environment, scene.AmbientIntensity);
        }

        return _ambientCube;
    }

    protected RasterState StateFor(IMesh mesh) =>
        _fogState.WithOpacity(mesh.Opacity).WithReflectance(ReflectanceFor(mesh));

    protected virtual SurfaceReflectance ReflectanceFor(IMesh mesh) =>
        SurfaceReflectance.FromMaterial(mesh.Material);

    protected virtual void PrepareCore(Scene scene)
    {
    }

    public abstract void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile);

    protected LinearColor LitColor(Vector3 worldPosition, Vector3 normal)
    {
        var n = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
        var total = AmbientLight.Evaluate(worldPosition, n);
        var shadows = Shadows;

        for (var i = 0; i < Lights.Count; i++)
        {
            ref readonly var light = ref Lights[i];

            if (!light.Sample(worldPosition, out var toLight, out var attenuation))
            {
                continue;
            }

            var nDotL = Vector3.Dot(n, toLight);
            if (nDotL <= 0f)
            {
                continue;
            }

            if (light.CastsShadow && shadows is not null)
            {
                attenuation *= shadows.Visibility(worldPosition, nDotL);
                if (attenuation <= 0f)
                {
                    continue;
                }
            }

            total += nDotL * attenuation * light.Color;
        }

        return total;
    }
}
