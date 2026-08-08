using SoftEngine.Core.Geometry;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Which level a triangle samples from, and how far it lies toward the next one.
///
/// <see cref="Blend"/> is zero for every filtering mode but
/// <see cref="TextureFiltering.Trilinear"/>: the others resolve to a single level, and a level
/// plus a zero blend is exactly what they always asked for.
/// </summary>
/// <param name="Level">The level to sample, in [0, MipCount - 1].</param>
/// <param name="Blend">How far toward <c>Level + 1</c> the surface falls, in [0, 1).</param>
public readonly record struct MipSelection(int Level, float Blend);

/// <summary>Chooses which mip level a triangle should sample from.</summary>
public static class MipSelector
{
    /// <summary>
    /// One mip level per triangle, from the ratio of its texel footprint to its screen
    /// area: level 0 when a texel maps to a pixel or more, one level up for every 4×
    /// more texels than pixels. Cruder than per-pixel derivatives, but a triangle is a
    /// small enough unit in practice — and it keeps the per-pixel path branch-free.
    /// </summary>
    public static int Select(
        Texture texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2) =>
        System.Math.Clamp(
            (int)(SelectExact(texture, p0, p1, p2, uv0, uv1, uv2) + 0.5f),
            0,
            texture.MipCount - 1);

    /// <summary>
    /// The level to sample and the blend toward the next one, for a given filtering mode.
    ///
    /// The level a nearest or bilinear fill uses is the <em>nearest</em> one, since a single
    /// tap should come from whichever level fits best. A trilinear fill takes the level
    /// <em>below</em> and blends upward, because there is no such thing as blending toward
    /// the level you already rounded away from — the two are the same choice expressed for a
    /// path that keeps the fraction and one that has to throw it away.
    /// </summary>
    public static MipSelection SelectBlended(
        Texture texture,
        TextureFiltering filtering,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2)
    {
        if (filtering != TextureFiltering.Trilinear)
        {
            return new MipSelection(Select(texture, p0, p1, p2, uv0, uv1, uv2), 0f);
        }

        var exact = SelectExact(texture, p0, p1, p2, uv0, uv1, uv2);
        var level = (int)exact;

        return new MipSelection(level, exact - level);
    }

    /// <summary>
    /// The level this triangle's footprint calls for, unrounded and clamped to the chain —
    /// the quantity both selections above are made from.
    /// </summary>
    private static float SelectExact(
        Texture texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2)
    {
        if (texture.MipCount <= 1)
        {
            return 0f;
        }

        var screenArea = MathF.Abs(ScanlineRasterizer.Cross2D(p0, p1, p2)) * 0.5f;
        if (screenArea <= 0f)
        {
            return 0f;
        }

        var texelArea = MathF.Abs(
            (uv1.X - uv0.X) * (uv2.Y - uv0.Y) - (uv1.Y - uv0.Y) * (uv2.X - uv0.X))
            * 0.5f * texture.Width * texture.Height;
        if (texelArea <= 0f)
        {
            return 0f;
        }

        var level = 0.5f * MathF.Log2(texelArea / screenArea);
        return System.Math.Clamp(level, 0f, texture.MipCount - 1);
    }
}
