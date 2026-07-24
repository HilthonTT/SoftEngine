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
    private readonly TextureSampler _albedo;
    private readonly bool _gammaCorrect;

    public TexturedShader(Texture texture)
        : this(texture, 0, TextureFiltering.Nearest, false)
    {
    }

    public TexturedShader(Texture texture, int mipLevel, TextureFiltering filtering, bool gammaCorrect)
    {
        _albedo = new TextureSampler(texture, mipLevel, filtering);
        _gammaCorrect = gammaCorrect;
    }

    public ColorRGB Shade(in TextureVarying v)
    {
        var texel = _albedo.Sample(v.UV);

        if (!_gammaCorrect)
        {
            return v.Intensity * texel;
        }

        return new ColorRGB(
            ColorSpace.ToSrgb(v.Intensity * ColorSpace.ToLinear(texel.R)),
            ColorSpace.ToSrgb(v.Intensity * ColorSpace.ToLinear(texel.G)),
            ColorSpace.ToSrgb(v.Intensity * ColorSpace.ToLinear(texel.B)));
    }
}
