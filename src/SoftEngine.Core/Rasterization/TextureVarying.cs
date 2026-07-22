using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>UV coordinates plus a Gouraud light intensity, interpolated per pixel.</summary>
public readonly struct TextureVarying(Vector2 uv, float intensity) : IVarying<TextureVarying>
{
    public readonly Vector2 UV = uv;
    public readonly float Intensity = intensity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextureVarying Lerp(in TextureVarying a, in TextureVarying b, float t) =>
        new(Vector2.Lerp(a.UV, b.UV, t), a.Intensity + (b.Intensity - a.Intensity) * t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TextureVarying Scale(in TextureVarying a, float f) =>
        new(a.UV * f, a.Intensity * f);
}
