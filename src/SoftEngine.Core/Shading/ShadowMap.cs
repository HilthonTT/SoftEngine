using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// Depth rendered from a light's point of view, plus the matrices that put it there. Shading a
/// point means projecting it with the same matrix and comparing its distance to the light
/// against the nearest surface the light could see in that direction: farther means something
/// else got there first, so the point is in shadow.
///
/// <para>
/// The map is split into <em>cascades</em> — several depth buffers, each fitted to a slice of
/// the camera's own view distance. One buffer over a whole scene spends its resolution
/// uniformly, which means the texels land where they are least useful: a shadow ten metres
/// from the eye and one five hundred metres away get the same number of them, and perspective
/// has already made the first fill a hundred times more pixels. Cascades give the near slice a
/// whole buffer of its own, and each slice after it covers more ground with the same texels.
/// </para>
///
/// <para>
/// Which cascade shades a point is decided by <em>containment</em>, not by its view depth: the
/// cascades are nested, so the first one that covers the point is also the sharpest one that
/// does. That keeps <see cref="Visibility"/> a function of world position alone, which is what
/// lets the same call work from a vertex-lit painter and a per-pixel one without either of
/// them knowing cascades exist.
/// </para>
///
/// Depth within a cascade is normalized to [0, 1] by the light's own (orthographic)
/// projection, so it is linear — unlike the main framebuffer's perspective depth, precision is
/// uniform across the range.
/// </summary>
public sealed class ShadowMap
{
    /// <summary>Depth of a texel nothing was drawn into: farther than any real surface.</summary>
    public const float Empty = 1f;

    /// <summary>
    /// The most cascades a scene can ask for. Four covers the distance range of any scene this
    /// renderer can fill at an interactive rate, and every extra one is another depth-only
    /// pass over the world.
    /// </summary>
    public const int MaxCascades = 4;

    private readonly float[] _depth;
    private readonly int _resolution;
    private readonly int _cascadeCount;

    private readonly Matrix4x4[] _lightViewProjection;
    private readonly float[] _depthBias;
    private readonly float[] _slopeBias;

    private float _strength;
    private bool _softFilter;

    public ShadowMap(int resolution, int cascadeCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 1);

        _resolution = resolution;
        _cascadeCount = System.Math.Clamp(cascadeCount, 1, MaxCascades);

        _depth = new float[resolution * resolution * _cascadeCount];

        _lightViewProjection = new Matrix4x4[_cascadeCount];
        _depthBias = new float[_cascadeCount];
        _slopeBias = new float[_cascadeCount];
    }

    /// <summary>Side length of one cascade's square buffer.</summary>
    public int Resolution => _resolution;

    public int CascadeCount => _cascadeCount;

    /// <summary>
    /// Every cascade's texels end to end, row-major from the top-left of each. Cascade
    /// <c>c</c> starts at <c>c * Resolution * Resolution</c>.
    /// </summary>
    public float[] Depth => _depth;

    /// <summary>The texels of one cascade, for the pass that fills it and the view that shows it.</summary>
    public Span<float> DepthOf(int cascade) =>
        _depth.AsSpan(System.Math.Clamp(cascade, 0, _cascadeCount - 1) * _resolution * _resolution, _resolution * _resolution);

    /// <summary>Where cascade <c>c</c>'s texels begin in <see cref="Depth"/>.</summary>
    public int OffsetOf(int cascade) => System.Math.Clamp(cascade, 0, _cascadeCount - 1) * _resolution * _resolution;

    /// <summary>World space to one cascade's clip space; w is 1 throughout (parallel projection).</summary>
    public Matrix4x4 LightViewProjectionOf(int cascade) =>
        _lightViewProjection[System.Math.Clamp(cascade, 0, _cascadeCount - 1)];

    /// <summary>The nearest cascade's matrix — the whole map when there is only one.</summary>
    public Matrix4x4 LightViewProjection => _lightViewProjection[0];

    /// <summary>Clears every cascade to <see cref="Empty"/> and adopts the pass's shading settings.</summary>
    public void Begin(float strength, bool softFilter)
    {
        _strength = System.Math.Clamp(strength, 0f, 1f);
        _softFilter = softFilter;

        Array.Fill(_depth, Empty);
    }

    /// <summary>Adopts one cascade's projection and the biases derived from its own texel size.</summary>
    public void SetCascade(int cascade, in Matrix4x4 lightViewProjection, float depthBias, float slopeBias)
    {
        if ((uint)cascade >= (uint)_cascadeCount)
        {
            return;
        }

        _lightViewProjection[cascade] = lightViewProjection;
        _depthBias[cascade] = depthBias;
        _slopeBias[cascade] = slopeBias;
    }

    /// <summary>
    /// How much of the light reaches <paramref name="worldPosition"/>: 1 is fully lit, 0 is
    /// fully shadowed. <paramref name="nDotL"/> is the (unclamped) cosine between the surface
    /// normal and the light, used to scale the slope bias.
    ///
    /// Points outside every cascade are treated as lit — the map only covers the range the
    /// cascades were fitted to.
    /// </summary>
    public float Visibility(Vector3 worldPosition, float nDotL)
    {
        for (var cascade = 0; cascade < _cascadeCount; cascade++)
        {
            // Parallel projection: w is 1, so the transform needs no divide.
            var light = Vector4.Transform(worldPosition, _lightViewProjection[cascade]);

            var u = light.X * 0.5f + 0.5f;
            var v = 0.5f - light.Y * 0.5f;
            var depth = light.Z;

            if (depth < 0f || depth > 1f)
            {
                continue;
            }

            // A point right at a cascade's edge has its filter taps hanging off the buffer,
            // which read as lit and leave a bright seam along the boundary. Every cascade but
            // the last therefore hands the point to the next one out a margin early — the
            // margin being exactly the reach of the filter. The last cascade keeps the full
            // range, because there is nowhere left to hand it to.
            var margin = cascade + 1 < _cascadeCount ? (_softFilter ? 2f : 1f) / _resolution : 0f;

            if (u < margin || u >= 1f - margin || v < margin || v >= 1f - margin)
            {
                continue;
            }

            // sqrt(1 - cos²)/cos is the tangent of the incidence angle — how much depth one
            // texel of surface spans. Clamped, or a surface edge-on to the light asks for
            // unbounded bias and its shadow detaches completely.
            var cos = System.Math.Clamp(MathF.Abs(nDotL), 0.05f, 1f);
            var bias = _depthBias[cascade] + _slopeBias[cascade] * MathF.Min(MathF.Sqrt(1f - cos * cos) / cos, 4f);

            var x = (int)(u * _resolution);
            var y = (int)(v * _resolution);
            var offset = cascade * _resolution * _resolution;

            var occlusion = _softFilter
                ? SampleSoft(offset, x, y, depth - bias)
                : (IsOccluded(offset, x, y, depth - bias) ? 1f : 0f);

            return 1f - occlusion * _strength;
        }

        return 1f;
    }

    /// <summary>
    /// Which cascade shades a point, or -1 when none does. Only the debug views ask; the
    /// shading path finds and uses the cascade in one pass.
    /// </summary>
    public int CascadeAt(Vector3 worldPosition)
    {
        for (var cascade = 0; cascade < _cascadeCount; cascade++)
        {
            var light = Vector4.Transform(worldPosition, _lightViewProjection[cascade]);

            var u = light.X * 0.5f + 0.5f;
            var v = 0.5f - light.Y * 0.5f;

            if (light.Z is >= 0f and <= 1f && u is >= 0f and < 1f && v is >= 0f and < 1f)
            {
                return cascade;
            }
        }

        return -1;
    }

    /// <summary>Fraction of a 3×3 neighbourhood that occludes <paramref name="depth"/>.</summary>
    private float SampleSoft(int offset, int x, int y, float depth)
    {
        var occluded = 0;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (IsOccluded(offset, x + dx, y + dy, depth))
                {
                    occluded++;
                }
            }
        }

        return occluded * (1f / 9f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsOccluded(int offset, int x, int y, float depth)
    {
        // Off-map texels have never been drawn into, so nothing there can cast a shadow.
        if ((uint)x >= (uint)_resolution || (uint)y >= (uint)_resolution)
        {
            return false;
        }

        return depth > _depth[offset + x + y * _resolution];
    }
}
