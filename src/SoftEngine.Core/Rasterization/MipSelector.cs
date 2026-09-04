using SoftEngine.Core.Geometry;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// A chosen mip level, the blend towards the next coarser one, and — for anisotropic
/// filtering — the line of extra taps to walk across the pixel's texture footprint.
/// </summary>
/// <param name="Level">Base mip level.</param>
/// <param name="Blend">Fraction of the next coarser level to mix in.</param>
/// <param name="Step">Spacing between taps, in UV, along the footprint's major axis.</param>
/// <param name="Taps">Number of taps to average. One (or zero, for <c>default</c>) is isotropic.</param>
public readonly record struct MipSelection(int Level, float Blend, Vector2 Step = default, int Taps = 1);

public static class MipSelector
{
    /// <summary>
    /// Upper bound on anisotropic taps per pixel. Higher keeps oblique surfaces sharper and
    /// costs one bilinear fetch per tap. Clamped to 1..16, matching the usual hardware range.
    /// </summary>
    public static int MaxAnisotropy { get; set; } = 8;

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
        if (filtering == TextureFiltering.Anisotropic)
        {
            return SelectAnisotropic(texture, p0, p1, p2, uv0, uv1, uv2);
        }

        if (filtering != TextureFiltering.Trilinear)
        {
            return new MipSelection(Select(texture, p0, p1, p2, uv0, uv1, uv2), 0f);
        }

        var exact = SelectExact(texture, p0, p1, p2, uv0, uv1, uv2);
        var level = (int)exact;

        return new MipSelection(level, exact - level);
    }

    /// <summary>
    /// Isotropic selection from the ratio of the triangle's texel area to its screen area — one
    /// level for the whole triangle, which is what a single square footprint per pixel implies.
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

    /// <summary>
    /// Anisotropic selection. An area ratio cannot tell a square footprint from a long thin one, so
    /// a floor seen at a glancing angle either aliases or — once the level is raised enough to stop
    /// it — blurs along the axis that was never stretched. This measures the footprint's two axes
    /// separately: the mip level comes from the <em>minor</em> axis, and the major axis is covered
    /// by walking several taps across it instead of by blurring.
    /// </summary>
    private static MipSelection SelectAnisotropic(
        Texture texture,
        in Vector3 p0, in Vector3 p1, in Vector3 p2,
        in Vector2 uv0, in Vector2 uv1, in Vector2 uv2)
    {
        if (texture.MipCount <= 1)
        {
            return new MipSelection(0, 0f);
        }

        var determinant = ScanlineRasterizer.Cross2D(p0, p1, p2);
        if (MathF.Abs(determinant) <= 1e-9f)
        {
            return new MipSelection(0, 0f);
        }

        var inverseDeterminant = 1f / determinant;

        // The triangle's UV Jacobian: invert the screen-space edge basis and apply it to the UV
        // edges, giving how far the texture moves for a one-pixel step in x and in y.
        var e1x = p1.X - p0.X;
        var e1y = p1.Y - p0.Y;
        var e2x = p2.X - p0.X;
        var e2y = p2.Y - p0.Y;

        var d1 = uv1 - uv0;
        var d2 = uv2 - uv0;

        var dudx = (d1.X * e2y - d2.X * e1y) * inverseDeterminant;
        var dvdx = (d1.Y * e2y - d2.Y * e1y) * inverseDeterminant;
        var dudy = (d2.X * e1x - d1.X * e2x) * inverseDeterminant;
        var dvdy = (d2.Y * e1x - d1.Y * e2x) * inverseDeterminant;

        // Measure both axes in texels of the base level, so a level is a doubling of either.
        var width = texture.Width;
        var height = texture.Height;

        var alongX = MathF.Sqrt(Squared(dudx * width) + Squared(dvdx * height));
        var alongY = MathF.Sqrt(Squared(dudy * width) + Squared(dvdy * height));

        var major = MathF.Max(alongX, alongY);
        var minor = MathF.Min(alongX, alongY);

        if (major <= 0f)
        {
            return new MipSelection(0, 0f);
        }

        var maxAnisotropy = System.Math.Clamp(MaxAnisotropy, 1, 16);

        // The minor axis is floored so a footprint collapsing to a line still gets a finite ratio;
        // what remains is in (0, maxAnisotropy], so neither the taps nor the level need clamping.
        var ratio = MathF.Min(major / MathF.Max(minor, 1e-6f), maxAnisotropy);

        // Each tap covers major/ratio texels, so that width — not the whole major axis — is what
        // the mip level has to resolve. Capped anisotropy leaves a residue that still blurs.
        var footprint = major / ratio;

        var exact = System.Math.Clamp(MathF.Log2(footprint), 0f, texture.MipCount - 1);
        var level = (int)exact;

        var taps = (int)MathF.Ceiling(ratio);

        var step = (alongX >= alongY ? new Vector2(dudx, dvdx) : new Vector2(dudy, dvdy)) / taps;

        return new MipSelection(level, exact - level, step, taps);
    }

    private static float Squared(float value) => value * value;
}
