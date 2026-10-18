using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization.Varyings;

public readonly struct IntensityVarying : IVarying<IntensityVarying>
{
    public readonly LinearColor Light;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IntensityVarying(LinearColor light) => Light = light;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IntensityVarying(float intensity) => Light = new LinearColor(intensity, intensity, intensity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntensityVarying Lerp(in IntensityVarying a, in IntensityVarying b, float t)
        => new(LinearColor.Lerp(a.Light, b.Light, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntensityVarying Scale(in IntensityVarying a, float f) => new(a.Light * f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntensityVarying Add(in IntensityVarying a, in IntensityVarying b) =>
        new(a.Light + b.Light);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntensityVarying Combine(
        in IntensityVarying a, in IntensityVarying b, in IntensityVarying c, float w0, float w1, float w2) =>
        new(a.Light * w0 + b.Light * w1 + c.Light * w2);
}
