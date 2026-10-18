using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization.Shaders;

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
