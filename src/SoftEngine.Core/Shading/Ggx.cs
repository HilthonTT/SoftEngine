using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// The microfacet model the physically-based path is built on: a surface is a field of
/// mirrors too small to see, and what a material looks like is the statistics of how they
/// are tilted. Three functions describe it, and everything else here — the shader, the
/// environment prefilter, the BRDF table — is one of them evaluated somewhere.
///
/// <list type="bullet">
/// <item><b>D</b>, the distribution: what fraction of the microfacets face a given
/// direction. GGX, whose long tail is why real highlights have a bright core that fades
/// into a wide haze rather than stopping at an edge.</item>
/// <item><b>G</b>, the geometry term: how much of the surface shadows and masks itself at
/// grazing angles. Carried here as the <see cref="Visibility"/> form, already divided by
/// the <c>4·(n·l)(n·v)</c> the specular BRDF would otherwise divide by.</item>
/// <item><b>F</b>, Fresnel: how reflectivity climbs toward 1 as a surface is viewed edge-on
/// — which every surface does, and which is most of what separates a rendered image that
/// looks like a photograph from one that looks like paint.</item>
/// </list>
///
/// Roughness is squared into the model's α before use (<see cref="Alpha"/>), the mapping
/// Disney introduced and everything since has kept: it makes the visible change per unit of
/// roughness roughly even, where α used directly spends most of its range looking identical.
/// </summary>
public static class Ggx
{
    /// <summary>Reflectivity of a dielectric at normal incidence — about 4%, and the same for almost all of them.</summary>
    public const float DielectricF0 = 0.04f;

    /// <summary>
    /// The model's roughness parameter from the authored one. Clamped away from zero: at
    /// exactly 0 the distribution is a Dirac delta, which a point light — itself a delta —
    /// has zero chance of hitting, so a perfect mirror lit by point lights would show no
    /// highlight at all.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Alpha(float roughness)
    {
        var clamped = System.Math.Clamp(roughness, 0.03f, 1f);
        return clamped * clamped;
    }

    /// <summary>Trowbridge-Reitz (GGX) normal distribution.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distribution(float nDotH, float alpha)
    {
        var a2 = alpha * alpha;
        var d = nDotH * nDotH * (a2 - 1f) + 1f;

        return a2 / MathF.Max(MathF.PI * d * d, 1e-9f);
    }

    /// <summary>
    /// Height-correlated Smith visibility: the geometry term with the specular
    /// denominator folded in, so <c>D · V · F</c> is the whole specular BRDF.
    ///
    /// Height-correlated rather than the separable form because masking and shadowing
    /// happen on the same surface — a microfacet hidden from the eye is likelier to be
    /// hidden from the light too — and pretending they are independent darkens grazing
    /// angles for no reason.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Visibility(float nDotV, float nDotL, float alpha)
    {
        var a2 = alpha * alpha;

        var lambdaV = nDotL * MathF.Sqrt(nDotV * nDotV * (1f - a2) + a2);
        var lambdaL = nDotV * MathF.Sqrt(nDotL * nDotL * (1f - a2) + a2);

        return 0.5f / MathF.Max(lambdaV + lambdaL, 1e-9f);
    }

    /// <summary>Schlick's Fresnel weight, <c>(1 - cosine)^5</c> — the interpolant between F0 and white.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FresnelWeight(float cosine)
    {
        var f = System.Math.Clamp(1f - cosine, 0f, 1f);
        var f2 = f * f;

        return f2 * f2 * f;
    }

    /// <summary>Schlick's Fresnel for a scalar reflectance.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Fresnel(float f0, float cosine) =>
        f0 + (1f - f0) * FresnelWeight(cosine);

    /// <summary>Schlick's Fresnel per channel — what a metal, whose F0 is coloured, needs.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LinearColor Fresnel(LinearColor f0, float cosine)
    {
        var w = FresnelWeight(cosine);

        return new LinearColor(
            f0.R + (1f - f0.R) * w,
            f0.G + (1f - f0.G) * w,
            f0.B + (1f - f0.B) * w);
    }

    /// <summary>
    /// The i'th of <paramref name="count"/> points of the Hammersley sequence: the first
    /// coordinate walks the unit interval evenly, the second is the van der Corput radical
    /// inverse — i's bits reflected about the binary point.
    ///
    /// A low-discrepancy sequence rather than a random one because these samples are taken
    /// once and reused for every pixel of every frame. Random samples would leave clumps and
    /// gaps that never average out, and would differ between two runs of the same scene.
    /// </summary>
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

    /// <summary>
    /// A half-vector drawn from the GGX distribution for the given sample point, in a
    /// tangent frame whose Z is the surface normal.
    ///
    /// Importance sampling: rather than spraying directions over the hemisphere and
    /// weighting each by how likely the surface is to reflect that way — where a smooth
    /// surface throws almost all of them away — the sample points are mapped through the
    /// distribution's inverse, so they arrive already concentrated where it is large.
    /// </summary>
    public static Vector3 ImportanceSampleHalfVector(Vector2 xi, float alpha)
    {
        var a2 = alpha * alpha;

        var phi = MathF.Tau * xi.X;

        // Inverting GGX's cumulative distribution. At α → 0 this collapses to cos θ = 1:
        // every sample is the normal itself, which is what a mirror's lobe is.
        var cosTheta = MathF.Sqrt((1f - xi.Y) / MathF.Max(1f + (a2 - 1f) * xi.Y, 1e-9f));
        var sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));

        return new Vector3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
    }

    /// <summary>
    /// An orthonormal basis around <paramref name="normal"/>, for turning a tangent-space
    /// sample into a world-space direction. Any basis will do — the samples are rotationally
    /// symmetric about the normal — so this picks whichever axis the normal points along
    /// least, which is the one guaranteed not to be parallel to it.
    /// </summary>
    public static (Vector3 Tangent, Vector3 Bitangent) BasisAround(Vector3 normal)
    {
        var up = MathF.Abs(normal.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;

        var tangent = Vector3.Normalize(Vector3.Cross(up, normal));
        var bitangent = Vector3.Cross(normal, tangent);

        return (tangent, bitangent);
    }
}
