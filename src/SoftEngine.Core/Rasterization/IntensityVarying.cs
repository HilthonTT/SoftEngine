using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>A single interpolated scalar light intensity (Gouraud).</summary>
public readonly struct IntensityVarying(float intensity) : IVarying<IntensityVarying>
{
    public readonly float Intensity = intensity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntensityVarying Lerp(in IntensityVarying a, in IntensityVarying b, float t)
        => new(a.Intensity + (b.Intensity - a.Intensity) * t);
}
