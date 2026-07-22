using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Painters;

/// <summary>
/// Base for painters that light their pixels. Resolves the active light once per frame:
/// the scene world's first light wins, otherwise the light given at construction,
/// otherwise a default point light above and behind the origin.
/// </summary>
public abstract class LitPainter(ILight? light, float ambient) : IPainter
{
    private readonly ILight _fallback = light ?? new PointLight { Position = new Vector3(0, 10, 10) };

    protected ILight Light { get; private set; } = light ?? new PointLight { Position = new Vector3(0, 10, 10) };

    /// <summary>Base intensity every surface receives regardless of the light.</summary>
    protected float Ambient { get; } = ambient;

    public void Prepare(Scene scene)
    {
        var lights = scene.World.Lights;
        Light = lights.Count > 0 ? lights[0] : _fallback;
        PrepareCore(scene);
    }

    protected virtual void PrepareCore(Scene scene)
    {
    }

    public abstract void DrawTriangle(FrameBuffer surface, ColorRGB color, VertexBuffer vertexBuffer, int triangleIndice);

    /// <summary>Ambient plus Lambert diffuse, clamped to 1.</summary>
    protected float LitIntensity(Vector3 worldPosition, Vector3 normal) =>
        MathF.Min(1f, Ambient + LambertLighting.ComputeNDotL(worldPosition, normal, Light));
}
