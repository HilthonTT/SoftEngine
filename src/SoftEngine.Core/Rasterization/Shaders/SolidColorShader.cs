using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization.Shaders;

public readonly struct SolidColorShader(LinearColor color) : IPixelShader<EmptyVarying>
{
    private readonly LinearColor _color = color;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinearColor Shade(in EmptyVarying _) => _color;
}
