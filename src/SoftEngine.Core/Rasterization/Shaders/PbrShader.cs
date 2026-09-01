using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization.Shaders;

public readonly struct PbrShader : IPixelShader<MaterialVarying>
{
    private readonly TextureSampler _albedo;
    private readonly TextureSampler _normalMap;
    private readonly TextureSampler _metallicMap;
    private readonly TextureSampler _roughnessMap;
    private readonly TextureSampler _emissiveMap;

    private readonly LinearColor _baseColor;
    private readonly LinearColor _emissive;

    private readonly LightSet _lights;
    private readonly Vector3 _eye;
    private readonly AmbientField _ambient;
    private readonly PrefilteredEnvironment? _environment;
    private readonly ShadowMap? _shadows;

    private readonly float _metallic;
    private readonly float _roughness;
    private readonly float _normalStrength;

    public PbrShader(
        ColorRGB baseColor,
        in TextureSampler albedo,
        in TextureSampler normalMap,
        in TextureSampler metallicMap,
        in TextureSampler roughnessMap,
        in TextureSampler emissiveMap,
        LinearColor emissive,
        float metallic,
        float roughness,
        float normalStrength,
        LightSet lights,
        Vector3 eye,
        AmbientField ambient,
        PrefilteredEnvironment? environment,
        ShadowMap? shadows)
    {
        _baseColor = baseColor;
        _albedo = albedo;
        _normalMap = normalMap;
        _metallicMap = metallicMap;
        _roughnessMap = roughnessMap;
        _emissiveMap = emissiveMap;
        _emissive = emissive;
        _metallic = metallic;
        _roughness = roughness;
        _normalStrength = normalStrength;
        _lights = lights;
        _eye = eye;
        _ambient = ambient;
        _environment = environment;
        _shadows = shadows;
    }

    public LinearColor Shade(in MaterialVarying v)
    {
        LinearColor albedo = _albedo.HasTexture ? _albedo.Sample(v.UV) : _baseColor;

        var metallic = _metallic;
        if (_metallicMap.HasTexture)
        {
            metallic *= _metallicMap.Sample(v.UV).B * (1f / 255f);
        }

        var roughness = _roughness;
        if (_roughnessMap.HasTexture)
        {
            roughness *= _roughnessMap.Sample(v.UV).G * (1f / 255f);
        }

        metallic = System.Math.Clamp(metallic, 0f, 1f);
        roughness = System.Math.Clamp(roughness, 0f, 1f);

        var alpha = Ggx.Alpha(roughness);

        var n = ShadingNormal(v);
        var view = Normalize(_eye - v.World, n);

        var nDotV = MathF.Max(Vector3.Dot(n, view), 1e-4f);

        var f0 = LinearColor.Lerp(new LinearColor(Ggx.DielectricF0, Ggx.DielectricF0, Ggx.DielectricF0), albedo, metallic);
        var diffuseColor = albedo * (1f - metallic);

        var result = Direct(v.World, n, view, nDotV, alpha, f0, diffuseColor);

        result += Ambient(v.World, n, view, nDotV, roughness, f0, diffuseColor);

        if (_emissiveMap.HasTexture)
        {
            LinearColor map = _emissiveMap.Sample(v.UV);
            result += _emissive * map;
        }
        else
        {
            result += _emissive;
        }

        return result;
    }

    private LinearColor Direct(
        Vector3 world,
        Vector3 n,
        Vector3 view,
        float nDotV,
        float alpha,
        LinearColor f0,
        LinearColor diffuseColor)
    {
        var total = LinearColor.Black;

        for (var i = 0; i < _lights.Count; i++)
        {
            ref readonly var light = ref _lights[i];

            if (!light.Sample(world, out var l, out var attenuation))
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
                attenuation *= shadows.Visibility(world, nDotL);
                if (attenuation <= 0f)
                {
                    continue;
                }
            }

            var half = Vector3.Normalize(l + view);

            var nDotH = MathF.Max(Vector3.Dot(n, half), 0f);
            var vDotH = MathF.Max(Vector3.Dot(view, half), 0f);

            var fresnel = Ggx.Fresnel(f0, vDotH);

            var specularWeight = MathF.PI * Ggx.Distribution(nDotH, alpha) * Ggx.Visibility(nDotV, nDotL, alpha);

            var diffuse = new LinearColor(
                diffuseColor.R * (1f - fresnel.R),
                diffuseColor.G * (1f - fresnel.G),
                diffuseColor.B * (1f - fresnel.B));

            var brdf = diffuse + specularWeight * fresnel;

            total += (nDotL * attenuation) * (brdf * light.Color);
        }

        return total;
    }

    private LinearColor Ambient(
        Vector3 world,
        Vector3 n,
        Vector3 view,
        float nDotV,
        float roughness,
        LinearColor f0,
        LinearColor diffuseColor)
    {
        var weight = Ggx.FresnelWeight(nDotV);
        var ceiling = MathF.Max(1f - roughness, Ggx.DielectricF0);

        var fresnel = new LinearColor(
            f0.R + (MathF.Max(ceiling, f0.R) - f0.R) * weight,
            f0.G + (MathF.Max(ceiling, f0.G) - f0.G) * weight,
            f0.B + (MathF.Max(ceiling, f0.B) - f0.B) * weight);

        var irradiance = _ambient.Evaluate(world, n);

        var diffuse = new LinearColor(
            diffuseColor.R * irradiance.R * (1f - fresnel.R),
            diffuseColor.G * irradiance.G * (1f - fresnel.G),
            diffuseColor.B * irradiance.B * (1f - fresnel.B));

        var reflection = 2f * Vector3.Dot(n, view) * n - view;

        var incoming = _environment is { } environment
            ? environment.Sample(reflection, roughness)
            : _ambient.Evaluate(world, reflection);

        var response = BrdfLut.Sample(nDotV, roughness);
        var scale = response.X;
        var bias = response.Y;

        var specular = new LinearColor(
            incoming.R * (f0.R * scale + bias),
            incoming.G * (f0.G * scale + bias),
            incoming.B * (f0.B * scale + bias));

        return diffuse + specular;
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

        return Normalize(tangent * x + bitangent * y + n * z, n);
    }

    private static Vector3 Normalize(Vector3 v, Vector3 fallback) =>
        v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : fallback;
}
