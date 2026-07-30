using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// One mip level of a texture, bound to a filtering mode and ready to sample. Pixel
/// shaders hold these by value: resolving the level and the filter up front turns what
/// would be a per-pixel indirection through <see cref="Texture"/> into a direct array read.
///
/// UV addressing wraps, and V grows upward — V = 0 is the bottom row of the image.
/// </summary>
public readonly struct TextureSampler
{
    private readonly int[]? _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _bilinear;

    public TextureSampler(Texture? texture, int mipLevel, TextureFiltering filtering)
    {
        if (texture is null)
        {
            return;
        }

        var mip = texture.GetMip(mipLevel);

        _pixels = mip.Pixels;
        _width = mip.Width;
        _height = mip.Height;
        _bilinear = filtering == TextureFiltering.Bilinear;
    }

    /// <summary>False when no texture was bound; sampling then returns black.</summary>
    public bool HasTexture => _pixels is not null;

    public ColorRGB Sample(Vector2 uv) => Sample(uv.X, uv.Y);

    /// <summary>
    /// The alpha channel alone, in [0, 1], at the same texel <see cref="Sample"/> would read.
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

        // The same half-texel shift and wrap the bilinear colour path uses, so the mask and
        // the colour it masks are read from the same four texels with the same weights.
        var fx = u * _width - 0.5f;
        var fy = (1f - v) * _height - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;

        if (x0 < 0)
        {
            x0 += _width;
        }
        if (y0 < 0)
        {
            y0 += _height;
        }
        var x1 = x0 + 1 == _width ? 0 : x0 + 1;
        var y1 = y0 + 1 == _height ? 0 : y0 + 1;

        var pixels = _pixels;

        var a00 = (pixels[x0 + y0 * _width] >>> 24) & 0xFF;
        var a10 = (pixels[x1 + y0 * _width] >>> 24) & 0xFF;
        var a01 = (pixels[x0 + y1 * _width] >>> 24) & 0xFF;
        var a11 = (pixels[x1 + y1 * _width] >>> 24) & 0xFF;

        var alpha =
            a00 * ((1f - tx) * (1f - ty)) +
            a10 * (tx * (1f - ty)) +
            a01 * ((1f - tx) * ty) +
            a11 * (tx * ty);

        return alpha * (1f / 255f);
    }

    public ColorRGB Sample(float u, float v)
    {
        if (_pixels is null)
        {
            return default;
        }

        return _bilinear ? SampleBilinear(u, v) : SampleNearest(u, v);
    }

    private ColorRGB SampleNearest(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        var x = System.Math.Min((int)(u * _width), _width - 1);
        var y = System.Math.Min((int)((1f - v) * _height), _height - 1);

        return ColorRGB.FromPacked(_pixels![x + y * _width]);
    }

    private ColorRGB SampleBilinear(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        // Texel centers sit at (i + 0.5), so shift by half a texel before splitting
        // into base index and blend fraction. V flips the same way nearest does.
        var fx = u * _width - 0.5f;
        var fy = (1f - v) * _height - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;

        // Wrap addressing: u, v were reduced to [0, 1), so only the -1/edge cases remain.
        if (x0 < 0)
        {
            x0 += _width;
        }
        if (y0 < 0)
        {
            y0 += _height;
        }
        var x1 = x0 + 1 == _width ? 0 : x0 + 1;
        var y1 = y0 + 1 == _height ? 0 : y0 + 1;

        var pixels = _pixels!;

        var c00 = pixels[x0 + y0 * _width];
        var c10 = pixels[x1 + y0 * _width];
        var c01 = pixels[x0 + y1 * _width];
        var c11 = pixels[x1 + y1 * _width];

        var w00 = (1f - tx) * (1f - ty);
        var w10 = tx * (1f - ty);
        var w01 = (1f - tx) * ty;
        var w11 = tx * ty;

        var r = ((c00 >> 16) & 0xFF) * w00 + ((c10 >> 16) & 0xFF) * w10 + ((c01 >> 16) & 0xFF) * w01 + ((c11 >> 16) & 0xFF) * w11;
        var g = ((c00 >> 8) & 0xFF) * w00 + ((c10 >> 8) & 0xFF) * w10 + ((c01 >> 8) & 0xFF) * w01 + ((c11 >> 8) & 0xFF) * w11;
        var b = (c00 & 0xFF) * w00 + (c10 & 0xFF) * w10 + (c01 & 0xFF) * w01 + (c11 & 0xFF) * w11;

        return new ColorRGB((byte)(r + 0.5f), (byte)(g + 0.5f), (byte)(b + 0.5f));
    }
}
