using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Shaders;

public readonly struct MaterialShader : IPixelShader<MaterialVarying>
{
    private readonly TextureSampler _albedo;
    private readonly TextureSampler _normalMap;
    private readonly TextureSampler _specularMap;

    private readonly ColorRGB _color;
    private readonly LightSet _lights;
    private readonly Vector3 _eye;
    private readonly AmbientField _ambient;
    private readonly float _specularStrength;
    private readonly float _shininess;
    private readonly float _normalStrength;
    private readonly ShadowMap? _shadows;
    private readonly bool _gammaCorrect;

    private readonly int _shininessInt;

    public MaterialShader(
        ColorRGB color,
        in TextureSampler albedo,
        in TextureSampler normalMap,
        in TextureSampler specularMap,
        LightSet lights,
        Vector3 eye,
        AmbientField ambient,
        float specularStrength,
        float shininess,
        float normalStrength,
        bool gammaCorrect,
        ShadowMap? shadows)
    {
        _color = color;
        _albedo = albedo;
        _normalMap = normalMap;
        _specularMap = specularMap;
        _lights = lights;
        _eye = eye;
        _ambient = ambient;
        _specularStrength = specularStrength;
        _shininess = shininess;
        _normalStrength = normalStrength;
        _gammaCorrect = gammaCorrect;
        _shadows = shadows;

        _shininessInt = shininess > 0 && shininess <= 1024 && shininess == MathF.Floor(shininess)
            ? (int)shininess
            : 0;
    }

    public LinearColor Shade(in MaterialVarying v)
    {
        var albedo = _albedo.HasTexture ? _albedo.Sample(v.UV) : _color;
        var n = ShadingNormal(v);
        var view = Vector3.Normalize(_eye - v.World);

        var specularStrength = _specularStrength;
        if (_specularMap.HasTexture)
        {
            specularStrength *= _specularMap.Sample(v.UV).R * (1f / 255f);
        }

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

            if (specularStrength > 0f)
            {
                var half = Vector3.Normalize(l + view);
                var nDotH = MathF.Max(0f, Vector3.Dot(n, half));

                var power = _shininessInt > 0 ? PowInt(nDotH, _shininessInt) : MathF.Pow(nDotH, _shininess);

                specular += power * specularStrength * attenuation * light.Color;
            }
        }

        if (_gammaCorrect)
        {
            LinearColor linear = albedo;

            return new LinearColor(
                linear.R * diffuse.R + specular.R,
                linear.G * diffuse.G + specular.G,
                linear.B * diffuse.B + specular.B);
        }

        return new ColorRGB(
            Saturate(albedo.R * diffuse.R + specular.R * 255f),
            Saturate(albedo.G * diffuse.G + specular.G * 255f),
            Saturate(albedo.B * diffuse.B + specular.B * 255f));
    }

    private Vector3 ShadingNormal(in MaterialVarying v)
    {
        var n = Normalize(v.Normal, Vector3.UnitY);

        if (!_normalMap.HasTexture || v.Tangent.LengthSquared() < 1e-12f)
        {
            return n;
        }

        var tangent = new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z);

        tangent -= n * Vector3.Dot(n, tangent);

        if (tangent.LengthSquared() < 1e-12f)
        {
            return n;
        }

        tangent = Vector3.Normalize(tangent);

        var bitangent = Vector3.Cross(n, tangent) * (v.Tangent.W < 0f ? -1f : 1f);

        var texel = _normalMap.Sample(v.UV);

        var x = (texel.R * (2f / 255f) - 1f) * _normalStrength;
        var y = (texel.G * (2f / 255f) - 1f) * _normalStrength;
        var z = texel.B * (2f / 255f) - 1f;

        var perturbed = tangent * x + bitangent * y + n * z;

        return Normalize(perturbed, n);
    }

    private static byte Saturate(float channel) => (byte)System.Math.Clamp(channel, 0f, 255f);

    private static Vector3 Normalize(Vector3 v, Vector3 fallback) =>
        v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : fallback;

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
