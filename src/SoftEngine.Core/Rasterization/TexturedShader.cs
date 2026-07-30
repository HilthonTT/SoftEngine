using SoftEngine.Core.Geometry;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Samples a texture at the interpolated UV and applies the interpolated light. Samples
/// one mip level, chosen per triangle by the painter; filtering is nearest or bilinear,
/// and the light can be applied in linear space (gamma-correct).
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
        : this(new TextureSampler(texture, mipLevel, filtering), gammaCorrect)
    {
    }

    /// <summary>
    /// Shades from a sampler the caller has already bound. For a painter that needs the same
    /// binding twice — the colour here and the alpha as a cutout mask — so the two cannot
    /// come from different mip levels of the same map.
    /// </summary>
    public TexturedShader(in TextureSampler albedo, bool gammaCorrect)
    {
        _albedo = albedo;
        _gammaCorrect = gammaCorrect;
    }

    public LinearColor Shade(in TextureVarying v)
    {
        var texel = _albedo.Sample(v.UV);

        return _gammaCorrect
            ? v.Light * (LinearColor)texel
            : v.Light.ScaleBytes(texel);
    }
}
