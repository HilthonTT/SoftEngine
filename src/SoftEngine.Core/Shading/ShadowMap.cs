using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// A depth buffer rendered from a light's point of view, plus the matrix that put it
/// there. Shading a point means projecting it with the same matrix and comparing its
/// distance to the light against the nearest surface the light could see in that
/// direction: farther means something else got there first, so the point is in shadow.
///
/// Depth is normalized to [0, 1] by the light's own (orthographic) projection, so it is
/// linear — unlike the main framebuffer's perspective depth, precision is uniform across
/// the whole range.
/// </summary>
public sealed class ShadowMap
{
    /// <summary>Depth of a texel nothing was drawn into: farther than any real surface.</summary>
    public const float Empty = 1f;

    private readonly float[] _depth;
    private readonly int _resolution;

    private Matrix4x4 _lightViewProjection;
    private float _depthBias;
    private float _slopeBias;
    private float _strength;
    private bool _softFilter;

    public ShadowMap(int resolution)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 1);

        _resolution = resolution;
        _depth = new float[resolution * resolution];
    }

    public int Resolution => _resolution;

    /// <summary>The raw depth texels, row-major from the top-left of the light's view.</summary>
    public float[] Depth => _depth;

    /// <summary>World space to the light's clip space; w is 1 throughout (parallel projection).</summary>
    public Matrix4x4 LightViewProjection => _lightViewProjection;

    /// <summary>Clears every texel to <see cref="Empty"/> and adopts the pass's matrix and biases.</summary>
    public void Begin(in Matrix4x4 lightViewProjection, float depthBias, float slopeBias, float strength, bool softFilter)
    {
        _lightViewProjection = lightViewProjection;
        _depthBias = depthBias;
        _slopeBias = slopeBias;
        _strength = System.Math.Clamp(strength, 0f, 1f);
        _softFilter = softFilter;

        Array.Fill(_depth, Empty);
    }

    /// <summary>
    /// How much of the light reaches <paramref name="worldPosition"/>: 1 is fully lit,
    /// 0 is fully shadowed. <paramref name="nDotL"/> is the (unclamped) cosine between
    /// the surface normal and the light, used to scale the slope bias.
    /// Points outside the map are treated as lit — the map only covers the scene's extent.
    /// </summary>
    public float Visibility(Vector3 worldPosition, float nDotL)
    {
        // Parallel projection: w is 1, so the transform needs no divide.
        var light = Vector4.Transform(worldPosition, _lightViewProjection);

        var u = light.X * 0.5f + 0.5f;
        var v = 0.5f - light.Y * 0.5f;
        var depth = light.Z;

        if (u < 0f || u >= 1f || v < 0f || v >= 1f || depth < 0f || depth > 1f)
        {
            return 1f;
        }

        // sqrt(1 - cos²)/cos is the tangent of the incidence angle — how much depth one
        // texel of surface spans. Clamped, or a surface edge-on to the light asks for
        // unbounded bias and its shadow detaches completely.
        var cos = System.Math.Clamp(MathF.Abs(nDotL), 0.05f, 1f);
        var bias = _depthBias + _slopeBias * MathF.Min(MathF.Sqrt(1f - cos * cos) / cos, 4f);

        var x = (int)(u * _resolution);
        var y = (int)(v * _resolution);

        var occlusion = _softFilter
            ? SampleSoft(x, y, depth - bias)
            : (IsOccluded(x, y, depth - bias) ? 1f : 0f);

        return 1f - occlusion * _strength;
    }

    /// <summary>Fraction of a 3×3 neighbourhood that occludes <paramref name="depth"/>.</summary>
    private float SampleSoft(int x, int y, float depth)
    {
        var occluded = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (IsOccluded(x + dx, y + dy, depth))
                {
                    occluded++;
                }
            }
        }

        return occluded * (1f / 9f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsOccluded(int x, int y, float depth)
    {
        // Off-map texels have never been drawn into, so nothing there can cast a shadow.
        if ((uint)x >= (uint)_resolution || (uint)y >= (uint)_resolution)
        {
            return false;
        }

        return depth > _depth[x + y * _resolution];
    }
}
