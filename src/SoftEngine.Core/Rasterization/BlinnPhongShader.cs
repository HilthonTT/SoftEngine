using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Per-pixel Blinn-Phong: ambient plus, for every light in the scene, a Lambert diffuse
/// term and a specular highlight from the half-vector. The lights arrive pre-flattened as
/// a <see cref="LightSet"/>, so nothing here dispatches through an interface per pixel.
/// A shadow map, when the scene has one, is sampled at every fragment for the one light it
/// was rendered from — the world position needed for the lookup is already interpolated
/// for the lighting.
/// </summary>
public readonly struct BlinnPhongShader : IPixelShader<PhongVarying>
{
    private readonly AmbientField _ambient;
    private readonly Vector3 _eye;
    private readonly float _specularStrength;
    private readonly float _shininess;
    private readonly LightSet _lights;
    private readonly ShadowMap? _shadows;

    private readonly bool _gammaCorrect;

    // The base colour in whichever space the output is accumulated in: decoded to linear
    // when shading gamma-correctly, and the raw bytes otherwise, where the light scales
    // the encoded value directly.
    private readonly float _baseR;
    private readonly float _baseG;
    private readonly float _baseB;

    // Shininess is almost always a small whole number (32 by default); exponentiation
    // by squaring is then a handful of multiplies instead of a MathF.Pow per lit pixel.
    private readonly int _shininessInt;

    public BlinnPhongShader(
        ColorRGB color,
        LightSet lights,
        Vector3 eye,
        AmbientField ambient,
        float specularStrength,
        float shininess,
        bool gammaCorrect = false,
        ShadowMap? shadows = null)
    {
        _lights = lights;
        _eye = eye;
        _ambient = ambient;
        _specularStrength = specularStrength;
        _shininess = shininess;
        _gammaCorrect = gammaCorrect;
        _shadows = shadows;

        if (gammaCorrect)
        {
            LinearColor linear = color;
            (_baseR, _baseG, _baseB) = (linear.R, linear.G, linear.B);
        }
        else
        {
            (_baseR, _baseG, _baseB) = (color.R, color.G, color.B);
        }

        _shininessInt =
            shininess > 0 && shininess <= 1024 && shininess == MathF.Floor(shininess) ? (int)shininess : 0;
    }

    public LinearColor Shade(in PhongVarying v)
    {
        var n = Vector3.Normalize(v.Normal);
        var view = Vector3.Normalize(_eye - v.World);

        var diffuse = _ambient.Evaluate(v.World, n);
        var specular = LinearColor.Black;

        for (var i = 0; i < _lights.Count; i++)
        {
            ref readonly var light = ref _lights[i];

            if (!light.Sample(v.World, out var l, out var attenuation))
            {
                continue;
            }

            var nDotL = Vector3.Dot(n, l);
            if (nDotL <= 0f)
            {
                continue;
            }

            // Shadowing scales the light's own contribution; ambient stands in for
            // everything that reaches the surface by other paths, so it survives.
            if (light.CastsShadow && _shadows is { } shadows)
            {
                attenuation *= shadows.Visibility(v.World, nDotL);
                if (attenuation <= 0f)
                {
                    continue;
                }
            }

            diffuse += nDotL * attenuation * light.Color;

            if (_specularStrength > 0f)
            {
                var half = Vector3.Normalize(l + view);
                var nDotH = MathF.Max(0f, Vector3.Dot(n, half));

                var power = _shininessInt > 0 ? PowInt(nDotH, _shininessInt) : MathF.Pow(nDotH, _shininess);

                // The highlight takes the light's colour, not the surface's: it is the
                // light reflecting off the surface rather than being absorbed by it.
                specular += power * _specularStrength * attenuation * light.Color;
            }
        }

        return Combine(diffuse, specular);
    }

    /// <summary>
    /// Folds the accumulated diffuse and specular light onto the base colour. Nothing is
    /// clamped on the linear path — a highlight above white is a real measurement, and on
    /// an HDR target it survives to the tone-map instead of being flattened here.
    /// </summary>
    private LinearColor Combine(LinearColor diffuse, LinearColor specular)
    {
        if (_gammaCorrect)
        {
            return new LinearColor(
                _baseR * diffuse.R + specular.R,
                _baseG * diffuse.G + specular.G,
                _baseB * diffuse.B + specular.B);
        }

        // The naive path accumulates in sRGB bytes: the light scales the encoded base
        // colour, and one unit of specular light is a full 255 rather than a full 1.
        return new ColorRGB(
            Saturate(_baseR * diffuse.R + specular.R * 255f),
            Saturate(_baseG * diffuse.G + specular.G * 255f),
            Saturate(_baseB * diffuse.B + specular.B * 255f));
    }

    private static byte Saturate(float channel) => (byte)System.Math.Clamp(channel, 0f, 255f);

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
