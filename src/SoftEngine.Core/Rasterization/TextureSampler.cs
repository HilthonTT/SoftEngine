using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Textures;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

public readonly struct TextureSampler
{
    private readonly int[]? _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _bilinear;

    private readonly int[]? _coarsePixels;
    private readonly int _coarseWidth;
    private readonly int _coarseHeight;
    private readonly float _blend;

    // The anisotropic tap line: taps are spaced _step apart in UV and centred on the sample, so
    // the first sits _firstTap steps before it. Fixed per triangle, hence precomputed here.
    private readonly Vector2 _step;
    private readonly int _taps;
    private readonly float _firstTap;
    private readonly float _inverseTaps;

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

        // The selection already encodes the filtering mode it was made under: only an anisotropic
        // selection carries more than one tap, and only a level-blending one a non-zero blend.
        if (mip.Taps > 1)
        {
            _step = mip.Step;
            _taps = mip.Taps;
            _firstTap = -0.5f * (mip.Taps - 1);
            _inverseTaps = 1f / mip.Taps;
        }

        if (mip.Blend <= 0f)
        {
            return;
        }

        var coarse = texture.GetMip(mip.Level + 1);

        if (ReferenceEquals(coarse.Pixels, level.Pixels))
        {
            return;
        }

        _coarsePixels = coarse.Pixels;
        _coarseWidth = coarse.Width;
        _coarseHeight = coarse.Height;
        _blend = mip.Blend;
    }

    public bool HasTexture => _pixels is not null;

    public ColorRGB Sample(Vector2 uv) => Sample(uv.X, uv.Y);

    public float SampleAlpha(Vector2 uv) => SampleAlpha(uv.X, uv.Y);

    public float SampleAlpha(float u, float v)
    {
        if (_pixels is null)
        {
            return 1f;
        }

        if (!_bilinear)
        {
            u -= MathF.Floor(u);
            v -= MathF.Floor(v);

            var nx = System.Math.Min((int)(u * _width), _width - 1);
            var ny = System.Math.Min((int)((1f - v) * _height), _height - 1);

            return ((_pixels[nx + ny * _width] >>> 24) & 0xFF) * (1f / 255f);
        }

        if (_taps > 1)
        {
            return SampleAlphaAnisotropic(u, v) * (1f / 255f);
        }

        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        return AlphaAt(u, v) * (1f / 255f);
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

        if (_taps > 1)
        {
            return SampleAnisotropic(u, v);
        }

        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        ColorAt(u, v, out var r, out var g, out var b);

        return new ColorRGB((byte)(r + 0.5f), (byte)(g + 0.5f), (byte)(b + 0.5f));
    }

    /// <summary>
    /// Averages <see cref="_taps"/> bilinear samples spread along the pixel's texture footprint.
    /// The footprint's long axis is covered by these taps rather than by a coarser mip, which is
    /// what keeps a surface seen edge-on sharp across its width while still filtering along it.
    /// </summary>
    private ColorRGB SampleAnisotropic(float u, float v)
    {
        float r = 0f, g = 0f, b = 0f;

        for (var tap = 0; tap < _taps; tap++)
        {
            var uv = TapUV(tap, u, v);

            ColorAt(uv.X, uv.Y, out var tr, out var tg, out var tb);

            r += tr;
            g += tg;
            b += tb;
        }

        return new ColorRGB(
            (byte)(r * _inverseTaps + 0.5f),
            (byte)(g * _inverseTaps + 0.5f),
            (byte)(b * _inverseTaps + 0.5f));
    }

    private float SampleAlphaAnisotropic(float u, float v)
    {
        var alpha = 0f;

        for (var tap = 0; tap < _taps; tap++)
        {
            var uv = TapUV(tap, u, v);

            alpha += AlphaAt(uv.X, uv.Y);
        }

        return alpha * _inverseTaps;
    }

    /// <summary>Where the given tap of the line centred on (u, v) lands, wrapped into the texture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector2 TapUV(int tap, float u, float v)
    {
        var offset = _firstTap + tap;

        var tu = u + _step.X * offset;
        var tv = v + _step.Y * offset;

        return new Vector2(tu - MathF.Floor(tu), tv - MathF.Floor(tv));
    }

    /// <summary>One bilinear fetch at already-wrapped coordinates, blended towards the coarser mip.</summary>
    private void ColorAt(float u, float v, out float r, out float g, out float b)
    {
        ColorBilinear(_pixels!, _width, _height, u, v, out r, out g, out b);

        if (_coarsePixels is null)
        {
            return;
        }

        ColorBilinear(_coarsePixels, _coarseWidth, _coarseHeight, u, v, out var cr, out var cg, out var cb);

        r = float.Lerp(r, cr, _blend);
        g = float.Lerp(g, cg, _blend);
        b = float.Lerp(b, cb, _blend);
    }

    private float AlphaAt(float u, float v)
    {
        var alpha = AlphaBilinear(_pixels!, _width, _height, u, v);

        if (_coarsePixels is not null)
        {
            alpha = float.Lerp(alpha, AlphaBilinear(_coarsePixels, _coarseWidth, _coarseHeight, u, v), _blend);
        }

        return alpha;
    }

    private ColorRGB SampleNearest(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        var x = System.Math.Min((int)(u * _width), _width - 1);
        var y = System.Math.Min((int)((1f - v) * _height), _height - 1);

        return ColorRGB.FromPacked(_pixels![x + y * _width]);
    }

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

    private static void Corners(
        int width, int height, float u, float v,
        out int c00, out int c10, out int c01, out int c11,
        out float tx, out float ty)
    {
        var fx = u * width - 0.5f;
        var fy = (1f - v) * height - 0.5f;

        var x0 = (int)MathF.Floor(fx);
        var y0 = (int)MathF.Floor(fy);

        tx = fx - x0;
        ty = fy - y0;

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
