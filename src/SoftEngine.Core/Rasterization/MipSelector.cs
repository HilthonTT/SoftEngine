using SoftEngine.Core.Geometry;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

public readonly record struct MipSelection(int Level, float Blend);

public static class MipSelector
{
    public static int Select(
        Texture texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2) =>
        System.Math.Clamp(
            (int)(SelectExact(texture, p0, p1, p2, uv0, uv1, uv2) + 0.5f),
            0,
            texture.MipCount - 1);

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
