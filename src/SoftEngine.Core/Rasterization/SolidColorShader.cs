using SoftEngine.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>Emits one constant colour. Used by the classic and flat painters.</summary>
public readonly struct SolidColorShader(ColorRGB color) : IPixelShader<EmptyVarying>
{
    private readonly ColorRGB _color = color;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorRGB Shade(in EmptyVarying _) => _color;
}
