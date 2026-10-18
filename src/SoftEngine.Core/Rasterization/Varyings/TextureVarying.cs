using SoftEngine.Core.Shading;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization.Varyings;

public readonly struct TextureVarying : IVarying<TextureVarying>, ITexturedVarying
{
    public readonly Vector2 UV;
    public readonly LinearColor Light;

    public Vector2 TexCoord => UV;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TextureVarying(Vector2 uv, LinearColor light)
    {
        UV = uv;
        Light = light;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TextureVarying(Vector2 uv, float intensity)
        : this(uv, new LinearColor(intensity, intensity, intensity))
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextureVarying Lerp(in TextureVarying a, in TextureVarying b, float t) =>
        new(Vector2.Lerp(a.UV, b.UV, t), LinearColor.Lerp(a.Light, b.Light, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextureVarying Scale(in TextureVarying a, float f) =>
        new(a.UV * f, a.Light * f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextureVarying Add(in TextureVarying a, in TextureVarying b) =>
        new(a.UV + b.UV, a.Light + b.Light);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextureVarying Combine(
        in TextureVarying a, in TextureVarying b, in TextureVarying c, float w0, float w1, float w2) =>
        new(a.UV * w0 + b.UV * w1 + c.UV * w2,
            a.Light * w0 + b.Light * w1 + c.Light * w2);
}
