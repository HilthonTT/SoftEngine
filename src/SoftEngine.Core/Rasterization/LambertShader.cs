using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Modulates a base colour by the interpolated intensity. Used by Gouraud. With gamma
/// correction the base colour is decoded once here, scaled in linear light per pixel,
/// and re-encoded to sRGB.
/// </summary>
public readonly struct LambertShader : IPixelShader<IntensityVarying>
{
    private readonly ColorRGB _color;
    private readonly bool _gammaCorrect;
    private readonly float _linearR;
    private readonly float _linearG;
    private readonly float _linearB;

    public LambertShader(ColorRGB color)
        : this(color, false)
    {
    }

    public LambertShader(ColorRGB color, bool gammaCorrect)
    {
        _color = color;
        _gammaCorrect = gammaCorrect;

        if (gammaCorrect)
        {
            _linearR = ColorSpace.ToLinear(color.R);
            _linearG = ColorSpace.ToLinear(color.G);
            _linearB = ColorSpace.ToLinear(color.B);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColorRGB Shade(in IntensityVarying v) => _gammaCorrect
        ? new ColorRGB(
            ColorSpace.ToSrgb(v.Intensity * _linearR),
            ColorSpace.ToSrgb(v.Intensity * _linearG),
            ColorSpace.ToSrgb(v.Intensity * _linearB))
        : v.Intensity * _color;
}
