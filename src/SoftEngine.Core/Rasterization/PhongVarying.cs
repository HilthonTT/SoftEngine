using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// World-space position and normal, interpolated per pixel so lighting can be
/// evaluated at every fragment (Phong shading).
/// </summary>
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
}
