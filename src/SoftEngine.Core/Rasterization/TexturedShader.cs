using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Samples a texture at the interpolated UV and applies the interpolated light
/// intensity. Samples one mip level, chosen per triangle by the painter; filtering is
/// nearest or bilinear, and the intensity can be applied in linear light (gamma-correct).
/// </summary>
public readonly struct TexturedShader : IPixelShader<TextureVarying>
{
    private readonly int[] _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _bilinear;
    private readonly bool _gammaCorrect;

    public TexturedShader(Texture texture)
        : this(texture, 0, TextureFiltering.Nearest, false)
    {
    }

    public TexturedShader(Texture texture, int mipLevel, TextureFiltering filtering, bool gammaCorrect)
    {
        var mip = texture.GetMip(mipLevel);
        _pixels = mip.Pixels;
        _width = mip.Width;
        _height = mip.Height;
        _bilinear = filtering == TextureFiltering.Bilinear;
        _gammaCorrect = gammaCorrect;
    }

    public ColorRGB Shade(in TextureVarying v)
    {
        var texel = _bilinear ? SampleBilinear(v.UV.X, v.UV.Y) : SampleNearest(v.UV.X, v.UV.Y);

        if (!_gammaCorrect)
        {
            return v.Intensity * texel;
        }

        return new ColorRGB(
            ColorSpace.ToSrgb(v.Intensity * ColorSpace.ToLinear(texel.R)),
            ColorSpace.ToSrgb(v.Intensity * ColorSpace.ToLinear(texel.G)),
            ColorSpace.ToSrgb(v.Intensity * ColorSpace.ToLinear(texel.B)));
    }

    private ColorRGB SampleNearest(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        var x = System.Math.Min((int)(u * _width), _width - 1);
        var y = System.Math.Min((int)((1f - v) * _height), _height - 1);

        return ColorRGB.FromPacked(_pixels[x + y * _width]);
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

        var c00 = _pixels[x0 + y0 * _width];
        var c10 = _pixels[x1 + y0 * _width];
        var c01 = _pixels[x0 + y1 * _width];
        var c11 = _pixels[x1 + y1 * _width];

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
