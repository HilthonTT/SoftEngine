using SoftEngine.Core.Geometry;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;

namespace SoftEngine.Core.Rasterization.Shaders;

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
