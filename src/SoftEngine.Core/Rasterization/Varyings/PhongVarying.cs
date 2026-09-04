using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization.Varyings;

public readonly struct PhongVarying(Vector3 world, Vector3 normal) : IVarying<PhongVarying>
{
    public readonly Vector3 World = world;
    public readonly Vector3 Normal = normal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PhongVarying Lerp(in PhongVarying a, in PhongVarying b, float t) =>
        new(Vector3.Lerp(a.World, b.World, t), Vector3.Lerp(a.Normal, b.Normal, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PhongVarying Scale(in PhongVarying a, float f) =>
        new(a.World * f, a.Normal * f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PhongVarying Add(in PhongVarying a, in PhongVarying b) =>
        new(a.World + b.World, a.Normal + b.Normal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PhongVarying Combine(
        in PhongVarying a, in PhongVarying b, in PhongVarying c, float w0, float w1, float w2) =>
        new(a.World * w0 + b.World * w1 + c.World * w2,
            a.Normal * w0 + b.Normal * w1 + c.Normal * w2);
}
