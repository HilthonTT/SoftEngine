using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization.Varyings;

public readonly struct EmptyVarying : IVarying<EmptyVarying>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmptyVarying Lerp(in EmptyVarying a, in EmptyVarying b, float t) => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmptyVarying Scale(in EmptyVarying a, float f) => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmptyVarying Add(in EmptyVarying a, in EmptyVarying b) => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmptyVarying Combine(
        in EmptyVarying a, in EmptyVarying b, in EmptyVarying c, float w0, float w1, float w2) => default;
}
