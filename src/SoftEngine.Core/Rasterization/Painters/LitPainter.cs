using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// Base for painters that light their pixels. Resolves the scene's lights once per frame
/// into a <see cref="LightSet"/>: every light the world declares, or the one given at
/// construction, or a default point light above and behind the origin. Also snapshots the
/// scene's fog, gamma and shadow settings for the frame.
/// </summary>
public abstract class LitPainter(ILight? light, float ambient) : IPainter
{
    private readonly ILight _fallback = light ?? SceneLights.Default;

    private RasterState _fogState;

    /// <inheritdoc />
    public ILight? FallbackLight => _fallback;

    // Reused across frames; grown only when a world brings more lights than the last one.
    private ShaderLight[] _lightStorage = [];

    // The last environment reduced to an ambient cube, and what it was reduced with.
    private CubeMap? _ambientSource;
    private float _ambientIntensity = float.NaN;
    private AmbientCube _ambientCube;

    /// <summary>
    /// The frame's lights. The first is the one the shadow map was rendered from, when the
    /// scene casts shadows at all.
    /// </summary>
    protected LightSet Lights { get; private set; }

    /// <summary>The first light, for callers that still need a single one.</summary>
    protected ILight Light { get; private set; } = light ?? SceneLights.Default;

    /// <summary>Base intensity every surface receives regardless of the lights.</summary>
    protected float Ambient { get; } = ambient;

    /// <summary>
    /// The light a surface receives from no particular direction, as a function of which
    /// way it faces. A flat grey by default — <see cref="Ambient"/> in every channel — and
    /// the scene's environment reduced to six directional averages when it has one, so a
    /// surface facing the sky and one facing the ground stop receiving the same light.
    /// </summary>
    protected AmbientCube AmbientLight { get; private set; }

    /// <summary>Whether this frame shades in linear light with sRGB output (see <see cref="Scene.GammaCorrect"/>).</summary>
    protected bool GammaCorrect { get; private set; }

    /// <summary>The frame's shadow map, or null when the scene casts no shadows.</summary>
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

    /// <summary>
    /// The frame's ambient light. Reducing an environment to its face averages walks every
    /// texel of all six faces, so the result is kept until the environment or its intensity
    /// changes — which, for a scene with a fixed sky, is never.
    /// </summary>
    private AmbientCube ResolveAmbient(Scene scene)
    {
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

    /// <summary>The frame's fog state combined with a mesh's opacity, for the rasterizer.</summary>
    protected RasterState StateFor(IMesh mesh) => _fogState.WithOpacity(mesh.Opacity);

    protected virtual void PrepareCore(Scene scene)
    {
    }

    public abstract void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile);

    /// <summary>
    /// Ambient plus the Lambert diffuse of every light that reaches the point.
    ///
    /// The sum is not clamped. Two lights on the same surface really do deliver twice the
    /// light, and clamping here would throw that away before the target — which on an
    /// <see cref="FrameBuffer.IsHighDynamicRange">HDR</see> frame can hold it — ever sees
    /// it. What it does still respect is that ambient is never shadowed: it stands in for
    /// light arriving by every other path, so a surface in shadow darkens rather than
    /// going black.
    ///
    /// Painters that interpolate this across a triangle get per-vertex lighting and
    /// per-vertex shadows; per-pixel ones evaluate the same thing in their shader instead.
    /// </summary>
    protected LinearColor LitColor(Vector3 worldPosition, Vector3 normal)
    {
        var n = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
        var total = AmbientLight.Evaluate(n);
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
