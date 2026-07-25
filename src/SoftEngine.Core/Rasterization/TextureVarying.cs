using SoftEngine.Core.Shading;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>UV coordinates plus the Gouraud light at the vertex, interpolated per pixel.</summary>
public readonly struct TextureVarying : IVarying<TextureVarying>
{
    public readonly Vector2 UV;
    public readonly LinearColor Light;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TextureVarying(Vector2 uv, LinearColor light)
    {
        UV = uv;
        Light = light;
    }

    /// <summary>White light of the given intensity.</summary>
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
}
