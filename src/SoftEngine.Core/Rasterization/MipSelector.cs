using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

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
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2)
    {
        if (texture.MipCount <= 1)
        {
            return 0;
        }

        var screenArea = MathF.Abs(ScanlineRasterizer.Cross2D(p0, p1, p2)) * 0.5f;
        if (screenArea <= 0f)
        {
            return 0;
        }

        var texelArea = MathF.Abs(
            (uv1.X - uv0.X) * (uv2.Y - uv0.Y) - (uv1.Y - uv0.Y) * (uv2.X - uv0.X))
            * 0.5f * texture.Width * texture.Height;
        if (texelArea <= 0f)
        {
            return 0;
        }

        var level = (int)(0.5f * MathF.Log2(texelArea / screenArea) + 0.5f);
        return System.Math.Clamp(level, 0, texture.MipCount - 1);
    }
}
