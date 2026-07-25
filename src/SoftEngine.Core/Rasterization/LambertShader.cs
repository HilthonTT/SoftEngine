using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Modulates a base colour by the interpolated light. Used by Gouraud. With gamma
/// correction the base colour is decoded once here and multiplied by the light per pixel,
/// leaving the result in linear space; without it the light scales the sRGB bytes
/// directly, which is what the engine did before it shaded in linear light.
/// </summary>
public readonly struct LambertShader : IPixelShader<IntensityVarying>
{
    private readonly ColorRGB _color;
    private readonly LinearColor _linear;
    private readonly bool _gammaCorrect;

    public LambertShader(ColorRGB color)
        : this(color, false)
    {
    }

    public LambertShader(ColorRGB color, bool gammaCorrect)
    {
        _color = color;
        _gammaCorrect = gammaCorrect;
        _linear = gammaCorrect ? color : default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LinearColor Shade(in IntensityVarying v) => _gammaCorrect
        ? v.Light * _linear
        : v.Light.ScaleBytes(_color);
}
