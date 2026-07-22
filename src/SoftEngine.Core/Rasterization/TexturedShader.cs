using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;

namespace SoftEngine.Core.Rasterization;

/// <summary>Samples a texture at the interpolated UV and applies the interpolated light intensity.</summary>
public readonly struct TexturedShader(Texture texture) : IPixelShader<TextureVarying>
{
    private readonly Texture _texture = texture;

    public ColorRGB Shade(in TextureVarying v) => v.Intensity * _texture.Sample(v.UV.X, v.UV.Y);
}
