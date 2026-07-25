using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Emits one constant colour. Used by the classic and flat painters. It takes a
/// <see cref="LinearColor"/> so a flat-shaded triangle can carry light above white, and a
/// <see cref="Diagnostics.ColorRGB"/> converts to one implicitly.
/// </summary>
public readonly struct SolidColorShader(LinearColor color) : IPixelShader<EmptyVarying>
{
    private readonly LinearColor _color = color;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinearColor Shade(in EmptyVarying _) => _color;
}
