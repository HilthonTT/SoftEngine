using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Shaders;

public readonly struct BlinnPhongShader : IPixelShader<PhongVarying>
{
    private readonly AmbientField _ambient;
    private readonly Vector3 _eye;
    private readonly float _specularStrength;
    private readonly float _shininess;
    private readonly LightSet _lights;
    private readonly ShadowMap? _shadows;

    private readonly bool _gammaCorrect;

    private readonly float _baseR;
    private readonly float _baseG;
    private readonly float _baseB;

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

                specular += power * _specularStrength * attenuation * light.Color;
            }
        }

        return Combine(diffuse, specular);
    }

    private LinearColor Combine(LinearColor diffuse, LinearColor specular)
    {
        if (_gammaCorrect)
        {
            return new LinearColor(
                _baseR * diffuse.R + specular.R,
                _baseG * diffuse.G + specular.G,
                _baseB * diffuse.B + specular.B);
        }

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
