using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Per-pixel Blinn-Phong: ambient + Lambert diffuse + specular highlight from the
/// half-vector. The light is baked in as either a position (point light) or a unit
/// direction toward the light (directional), so no interface dispatch happens per pixel.
/// </summary>
public readonly struct BlinnPhongShader(
    ColorRGB color,
    Vector3 lightVector,
    bool isDirectional,
    float lightIntensity,
    Vector3 eye,
    float ambient,
    float specularStrength,
    float shininess,
    bool gammaCorrect = false) : IPixelShader<PhongVarying>
{
    private readonly ColorRGB _color = color;
    private readonly Vector3 _lightVector = lightVector;
    private readonly bool _isDirectional = isDirectional;
    private readonly float _lightIntensity = lightIntensity;
    private readonly Vector3 _eye = eye;
    private readonly float _ambient = ambient;
    private readonly float _specularStrength = specularStrength;
    private readonly float _shininess = shininess;

    private readonly bool _gammaCorrect = gammaCorrect;
    private readonly float _linearR = gammaCorrect ? ColorSpace.ToLinear(color.R) : 0f;
    private readonly float _linearG = gammaCorrect ? ColorSpace.ToLinear(color.G) : 0f;
    private readonly float _linearB = gammaCorrect ? ColorSpace.ToLinear(color.B) : 0f;

    // Shininess is almost always a small whole number (32 by default); exponentiation
    // by squaring is then a handful of multiplies instead of a MathF.Pow per lit pixel.
    private readonly int _shininessInt =
        shininess > 0 && shininess <= 1024 && shininess == MathF.Floor(shininess) ? (int)shininess : 0;

    public ColorRGB Shade(in PhongVarying v)
    {
        var n = Vector3.Normalize(v.Normal);
        var l = _isDirectional ? _lightVector : Vector3.Normalize(_lightVector - v.World);

        var nDotL = Vector3.Dot(n, l);
        var diffuse = MathF.Max(0, nDotL) * _lightIntensity;
        var lit = MathF.Min(1f, _ambient + diffuse);

        var spec = 0f;
        if (nDotL > 0 && _specularStrength > 0)
        {
            var view = Vector3.Normalize(_eye - v.World);
            var half = Vector3.Normalize(l + view);
            var nDotH = MathF.Max(0, Vector3.Dot(n, half));
            spec = (_shininessInt > 0 ? PowInt(nDotH, _shininessInt) : MathF.Pow(nDotH, _shininess))
                * _specularStrength * _lightIntensity;
        }

        if (_gammaCorrect)
        {
            // Linear light: scale the decoded base colour and add the white specular
            // term directly (white decodes to 1 per channel), then encode to sRGB.
            return new ColorRGB(
                ColorSpace.ToSrgb(lit * _linearR + spec),
                ColorSpace.ToSrgb(lit * _linearG + spec),
                ColorSpace.ToSrgb(lit * _linearB + spec));
        }

        var shaded = lit * _color;

        if (spec > 0)
        {
            shaded += spec * ColorRGB.White;
        }

        return shaded;
    }

    private static float PowInt(float x, int n)
    {
        var result = 1f;
        while (n > 0)
        {
            if ((n & 1) != 0)
            {
                result *= x;
            }
            x *= x;
            n >>= 1;
        }
        return result;
    }
}
