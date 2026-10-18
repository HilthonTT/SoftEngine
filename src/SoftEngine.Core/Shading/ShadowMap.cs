using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

public sealed class ShadowMap
{
    public const float Empty = 1f;

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

    public int Resolution => _resolution;

    public int CascadeCount => _cascadeCount;

    public float[] Depth => _depth;

    public Span<float> DepthOf(int cascade) =>
        _depth.AsSpan(System.Math.Clamp(cascade, 0, _cascadeCount - 1) * _resolution * _resolution, _resolution * _resolution);

    public int OffsetOf(int cascade) => System.Math.Clamp(cascade, 0, _cascadeCount - 1) * _resolution * _resolution;

    public Matrix4x4 LightViewProjectionOf(int cascade) =>
        _lightViewProjection[System.Math.Clamp(cascade, 0, _cascadeCount - 1)];

    public Matrix4x4 LightViewProjection => _lightViewProjection[0];

    public void Begin(float strength, bool softFilter)
    {
        _strength = System.Math.Clamp(strength, 0f, 1f);
        _softFilter = softFilter;

        Array.Fill(_depth, Empty);
    }

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

    public float Visibility(Vector3 worldPosition, float nDotL)
    {
        for (var cascade = 0; cascade < _cascadeCount; cascade++)
        {
            var light = Vector4.Transform(worldPosition, _lightViewProjection[cascade]);

            var u = light.X * 0.5f + 0.5f;
            var v = 0.5f - light.Y * 0.5f;
            var depth = light.Z;

            if (depth < 0f || depth > 1f)
            {
                continue;
            }

            var margin = cascade + 1 < _cascadeCount ? (_softFilter ? 2f : 1f) / _resolution : 0f;

            if (u < margin || u >= 1f - margin || v < margin || v >= 1f - margin)
            {
                continue;
            }

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
        if ((uint)x >= (uint)_resolution || (uint)y >= (uint)_resolution)
        {
            return false;
        }

        return depth > _depth[offset + x + y * _resolution];
    }
}
