using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// Base for painters that light their pixels. Resolves the active light once per frame:
/// the scene world's first light wins, otherwise the light given at construction,
/// otherwise a default point light above and behind the origin. Also snapshots the
/// scene's fog, gamma and shadow settings for the frame.
/// </summary>
public abstract class LitPainter(ILight? light, float ambient) : IPainter
{
    private readonly ILight _fallback = light ?? SceneLights.Default;

    private RasterState _fogState;

    protected ILight Light { get; private set; } = light ?? SceneLights.Default;

    /// <summary>Base intensity every surface receives regardless of the light.</summary>
    protected float Ambient { get; } = ambient;

    /// <summary>Whether this frame shades in linear light with sRGB output (see <see cref="Scene.GammaCorrect"/>).</summary>
    protected bool GammaCorrect { get; private set; }

    /// <summary>The frame's shadow map, or null when the scene casts no shadows.</summary>
    protected ShadowMap? Shadows { get; private set; }

    public void Prepare(Scene scene)
    {
        Light = SceneLights.Resolve(scene.World, _fallback);
        _fogState = RasterState.From(scene);
        GammaCorrect = scene.GammaCorrect;
        Shadows = scene.ShadowMap;
        PrepareCore(scene);
    }

    /// <summary>The frame's fog state combined with a mesh's opacity, for the rasterizer.</summary>
    protected RasterState StateFor(IMesh mesh) => _fogState.WithOpacity(mesh.Opacity);

    protected virtual void PrepareCore(Scene scene)
    {
    }

    public abstract void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice, in ScreenTile tile);

    /// <summary>
    /// Ambient plus Lambert diffuse, clamped to 1. When the scene casts shadows the diffuse
    /// term is scaled by the light's visibility — ambient is not, so shadowed surfaces
    /// darken rather than go black. Painters that interpolate this across a triangle get
    /// per-vertex shadows; per-pixel ones sample the map in their shader instead.
    /// </summary>
    protected float LitIntensity(Vector3 worldPosition, Vector3 normal)
    {
        var nDotL = Vector3.Dot(Vector3.Normalize(normal), Light.DirectionFrom(worldPosition));
        var diffuse = MathF.Max(0f, nDotL) * Light.Intensity;

        if (Shadows is { } shadows && diffuse > 0f)
        {
            diffuse *= shadows.Visibility(worldPosition, nDotL);
        }

        return MathF.Min(1f, Ambient + diffuse);
    }
}
