using SoftEngine.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>Modulates a base colour by the interpolated intensity. Used by Gouraud.</summary>
public readonly struct LambertShader(ColorRGB color) : IPixelShader<IntensityVarying>
{
    private readonly ColorRGB _color = color;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorRGB Shade(in IntensityVarying v) => v.Intensity * _color;
}
