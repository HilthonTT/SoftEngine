using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>No interpolants. Erased entirely by the JIT.</summary>
public readonly struct EmptyVarying : IVarying<EmptyVarying>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmptyVarying Lerp(in EmptyVarying a, in EmptyVarying b, float t) => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EmptyVarying Scale(in EmptyVarying a, float f) => default;
}
