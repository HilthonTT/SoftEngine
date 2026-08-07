using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// One mip level of a texture — or two, for a trilinear fill — bound to a filtering mode and
/// ready to sample. Pixel shaders hold these by value: resolving the level and the filter up
/// front turns what would be a per-pixel indirection through <see cref="Texture"/> into a
/// direct array read.
///
/// UV addressing wraps, and V grows upward — V = 0 is the bottom row of the image.
/// </summary>
public readonly struct TextureSampler
{
    private readonly int[]? _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _bilinear;

    // The coarser of the two levels a trilinear fill blends, and how much of it to take. Null
    // for every other mode, and for a trilinear triangle that landed on a level exactly — so
    // the one-level paths below are reached by a null test rather than by a blend of zero,
    // and their arithmetic is untouched to the bit.
    private readonly int[]? _coarsePixels;
    private readonly int _coarseWidth;
    private readonly int _coarseHeight;
    private readonly float _blend;

    public TextureSampler(Texture? texture, int mipLevel, TextureFiltering filtering)
        : this(texture, new MipSelection(mipLevel, 0f), filtering)
    {
    }

    public TextureSampler(Texture? texture, in MipSelection mip, TextureFiltering filtering)
    {
        if (texture is null)
        {
            return;
        }

        var level = texture.GetMip(mip.Level);

        _pixels = level.Pixels;
        _width = level.Width;
        _height = level.Height;
        _bilinear = filtering != TextureFiltering.Nearest;

        if (filtering != TextureFiltering.Trilinear || mip.Blend <= 0f)
        {
            return;
        }

        var coarse = texture.GetMip(mip.Level + 1);

        // GetMip clamps past the end of the chain, so the last level asked to blend upward
        // would otherwise blend with itself — a second tap for no change in the result.
        if (ReferenceEquals(coarse.Pixels, level.Pixels))
        {
            return;
        }

        _coarsePixels = coarse.Pixels;
        _coarseWidth = coarse.Width;
        _coarseHeight = coarse.Height;
        _blend = mip.Blend;
    }

    /// <summary>False when no texture was bound; sampling then returns black.</summary>
    public bool HasTexture => _pixels is not null;

    public ColorRGB Sample(Vector2 uv) => Sample(uv.X, uv.Y);

    /// <summary>
    /// The alpha channel alone, in [0, 1], at the same texel <see cref="Sample(Vector2)"/> would read.
    /// One when no texture is bound, so an absent mask covers everything.
    ///
    /// It is its own method rather than a channel of the sampled colour because
    /// <see cref="ColorRGB"/> does not carry one out of the bilinear path — the three colour
    /// channels are filtered and the result is constructed opaque. Alpha is filtered here on
    /// the same footing, which is what keeps a cutout edge as smooth as the colour beside it,
    /// and the colour path stays exactly the arithmetic it was.
    /// </summary>
    public float SampleAlpha(Vector2 uv) => SampleAlpha(uv.X, uv.Y);

    /// <inheritdoc cref="SampleAlpha(Vector2)"/>
    public float SampleAlpha(float u, float v)
    {
        if (_pixels is null)
        {
            return 1f;
        }

        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        if (!_bilinear)
        {
            var nx = System.Math.Min((int)(u * _width), _width - 1);
            var ny = System.Math.Min((int)((1f - v) * _height), _height - 1);

            return ((_pixels[nx + ny * _width] >>> 24) & 0xFF) * (1f / 255f);
        }

        var alpha = AlphaBilinear(_pixels, _width, _height, u, v);

        // The mask crosses levels with the colour it masks, or a cutout edge would be cut from
        // one level and shaded from two.
        if (_coarsePixels is not null)
        {
            alpha = float.Lerp(alpha, AlphaBilinear(_coarsePixels, _coarseWidth, _coarseHeight, u, v), _blend);
        }

        return alpha * (1f / 255f);
    }

    public ColorRGB Sample(float u, float v)
    {
        if (_pixels is null)
        {
            return default;
        }

        if (!_bilinear)
        {
            return SampleNearest(u, v);
        }

        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        ColorBilinear(_pixels, _width, _height, u, v, out var r, out var g, out var b);

        if (_coarsePixels is not null)
        {
            ColorBilinear(_coarsePixels, _coarseWidth, _coarseHeight, u, v, out var cr, out var cg, out var cb);

            // Blended before rounding: two colours each rounded to a byte and then mixed
            // would quantize twice, and the band the blend exists to remove would come back
            // as a fainter one.
            r = float.Lerp(r, cr, _blend);
            g = float.Lerp(g, cg, _blend);
            b = float.Lerp(b, cb, _blend);
        }

        return new ColorRGB((byte)(r + 0.5f), (byte)(g + 0.5f), (byte)(b + 0.5f));
    }

    private ColorRGB SampleNearest(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        var x = System.Math.Min((int)(u * _width), _width - 1);
        var y = System.Math.Min((int)((1f - v) * _height), _height - 1);

        return ColorRGB.FromPacked(_pixels![x + y * _width]);
    }

    /// <summary>
    /// The four-texel weighted average of one level, in unrounded channel values. U and V
    /// have already been reduced to [0, 1) by the caller, which is what lets both levels of a
    /// trilinear tap share one reduction.
    /// </summary>
    private static void ColorBilinear(
        int[] pixels, int width, int height, float u, float v,
        out float r, out float g, out float b)
    {
        Corners(width, height, u, v, out var c00, out var c10, out var c01, out var c11, out var tx, out var ty);

        var w00 = (1f - tx) * (1f - ty);
        var w10 = tx * (1f - ty);
        var w01 = (1f - tx) * ty;
        var w11 = tx * ty;

        var p00 = pixels[c00];
        var p10 = pixels[c10];
        var p01 = pixels[c01];
        var p11 = pixels[c11];

        r = ((p00 >> 16) & 0xFF) * w00 + ((p10 >> 16) & 0xFF) * w10 + ((p01 >> 16) & 0xFF) * w01 + ((p11 >> 16) & 0xFF) * w11;
        g = ((p00 >> 8) & 0xFF) * w00 + ((p10 >> 8) & 0xFF) * w10 + ((p01 >> 8) & 0xFF) * w01 + ((p11 >> 8) & 0xFF) * w11;
        b = (p00 & 0xFF) * w00 + (p10 & 0xFF) * w10 + (p01 & 0xFF) * w01 + (p11 & 0xFF) * w11;
    }

    /// <summary>The same average of the alpha channel, in [0, 255].</summary>
    private static float AlphaBilinear(int[] pixels, int width, int height, float u, float v)
    {
        Corners(width, height, u, v, out var c00, out var c10, out var c01, out var c11, out var tx, out var ty);

        var a00 = (pixels[c00] >>> 24) & 0xFF;
        var a10 = (pixels[c10] >>> 24) & 0xFF;
        var a01 = (pixels[c01] >>> 24) & 0xFF;
        var a11 = (pixels[c11] >>> 24) & 0xFF;

        return
            a00 * ((1f - tx) * (1f - ty)) +
            a10 * (tx * (1f - ty)) +
            a01 * ((1f - tx) * ty) +
            a11 * (tx * ty);
    }

    /// <summary>
    /// The four texel indices around (u, v) and the fractions between them, shared by the
    /// colour and alpha taps so a mask is read from the same texels with the same weights.
    /// </summary>
    private static void Corners(
        int width, int height, float u, float v,
        out int c00, out int c10, out int c01, out int c11,
        out float tx, out float ty)
    {
        // Texel centers sit at (i + 0.5), so shift by half a texel before splitting
        // into base index and blend fraction. V flips the same way nearest does.
        var fx = u * width - 0.5f;
        var fy = (1f - v) * height - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);

        tx = fx - x0;
        ty = fy - y0;

        // Wrap addressing: u, v were reduced to [0, 1), so only the -1/edge cases remain.
        if (x0 < 0)
        {
            x0 += width;
        }
        if (y0 < 0)
        {
            y0 += height;
        }
        var x1 = x0 + 1 == width ? 0 : x0 + 1;
        var y1 = y0 + 1 == height ? 0 : y0 + 1;

        c00 = x0 + y0 * width;
        c10 = x1 + y0 * width;
        c01 = x0 + y1 * width;
        c11 = x1 + y1 * width;
    }
}
