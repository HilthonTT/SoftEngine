using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

public static class Ggx
{
    public const float DielectricF0 = 0.04f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Alpha(float roughness)
    {
        var clamped = System.Math.Clamp(roughness, 0.03f, 1f);
        return clamped * clamped;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distribution(float nDotH, float alpha)
    {
        var a2 = alpha * alpha;
        var d = nDotH * nDotH * (a2 - 1f) + 1f;

        return a2 / MathF.Max(MathF.PI * d * d, 1e-9f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Visibility(float nDotV, float nDotL, float alpha)
    {
        var a2 = alpha * alpha;

        var lambdaV = nDotL * MathF.Sqrt(nDotV * nDotV * (1f - a2) + a2);
        var lambdaL = nDotV * MathF.Sqrt(nDotL * nDotL * (1f - a2) + a2);

        return 0.5f / MathF.Max(lambdaV + lambdaL, 1e-9f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FresnelWeight(float cosine)
    {
        var f = System.Math.Clamp(1f - cosine, 0f, 1f);
        var f2 = f * f;

        return f2 * f2 * f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Fresnel(float f0, float cosine) =>
        f0 + (1f - f0) * FresnelWeight(cosine);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor Fresnel(LinearColor f0, float cosine)
    {
        var w = FresnelWeight(cosine);

        return new LinearColor(
            f0.R + (1f - f0.R) * w,
            f0.G + (1f - f0.G) * w,
            f0.B + (1f - f0.B) * w);
    }

    public static Vector2 Hammersley(int i, int count)
    {
        var bits = (uint)i;

        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);

        return new Vector2(i / (float)count, bits * 2.3283064365386963e-10f);
    }

    public static Vector3 ImportanceSampleHalfVector(Vector2 xi, float alpha)
    {
        var a2 = alpha * alpha;

        var phi = MathF.Tau * xi.X;

        var cosTheta = MathF.Sqrt((1f - xi.Y) / MathF.Max(1f + (a2 - 1f) * xi.Y, 1e-9f));
        var sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));

        return new Vector3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
    }

    public static (Vector3 Tangent, Vector3 Bitangent) BasisAround(Vector3 normal)
    {
        var up = MathF.Abs(normal.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;

        var tangent = Vector3.Normalize(Vector3.Cross(up, normal));
        var bitangent = Vector3.Cross(normal, tangent);

        return (tangent, bitangent);
    }
}
