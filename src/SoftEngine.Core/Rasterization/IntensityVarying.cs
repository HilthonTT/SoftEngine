using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// The light arriving at a vertex, interpolated across the triangle (Gouraud).
///
/// A colour rather than a scalar, because lights have colours: a scene lit by a warm key
/// and a cool fill cannot be described by one number per vertex, however many lights it
/// was summed from. The float constructor still exists for white light of a given
/// intensity, which is what this carried when it was one.
/// </summary>
public readonly struct IntensityVarying : IVarying<IntensityVarying>
{
    public readonly LinearColor Light;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IntensityVarying(LinearColor light) => Light = light;

    /// <summary>White light of the given intensity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IntensityVarying(float intensity) => Light = new LinearColor(intensity, intensity, intensity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntensityVarying Lerp(in IntensityVarying a, in IntensityVarying b, float t)
        => new(LinearColor.Lerp(a.Light, b.Light, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntensityVarying Scale(in IntensityVarying a, float f) => new(a.Light * f);
}
