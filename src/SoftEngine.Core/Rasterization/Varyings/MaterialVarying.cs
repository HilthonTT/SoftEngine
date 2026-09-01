using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization.Varyings;

public readonly struct MaterialVarying(Vector3 world, Vector3 normal, Vector4 tangent, Vector2 uv)
    : IVarying<MaterialVarying>, ITexturedVarying
{
    public readonly Vector3 World = world;
    public readonly Vector3 Normal = normal;
    public readonly Vector4 Tangent = tangent;
    public readonly Vector2 UV = uv;

    public Vector2 TexCoord => UV;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MaterialVarying Lerp(in MaterialVarying a, in MaterialVarying b, float t) =>
        new(Vector3.Lerp(a.World, b.World, t),
            Vector3.Lerp(a.Normal, b.Normal, t),
            Vector4.Lerp(a.Tangent, b.Tangent, t),
            Vector2.Lerp(a.UV, b.UV, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MaterialVarying Scale(in MaterialVarying a, float f) =>
        new(a.World * f, a.Normal * f, a.Tangent * f, a.UV * f);
}
