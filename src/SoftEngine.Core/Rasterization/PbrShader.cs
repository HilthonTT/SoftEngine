using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Per-pixel Cook-Torrance shading over a metallic-roughness material: GGX microfacet
/// specular from every light in the scene, plus the environment as both a diffuse and a
/// specular source.
///
/// What makes it different from <see cref="MaterialShader"/> is not the number of terms but
/// what they are answerable to. Blinn-Phong's specular strength and shininess are two dials
/// with no units, tuned until a surface looks right under the light it was tuned under, and
/// wrong again when the light moves. Here the parameters describe the surface — how rough it
/// is, whether it is metal — the same numbers hold under any lighting, and the model
/// conserves energy: a rougher surface spreads the same reflected light over a wider lobe
/// instead of losing it, and a metal's missing diffuse goes into its (tinted) reflection.
///
/// <b>One deliberate deviation.</b> The physical BRDF divides the diffuse term by π, and every
/// other painter here multiplies albedo by n·l with no such divisor — so an identical scene
/// would render about three times darker the moment it switched to this shader, which is not
/// a thing a viewer should do when you click a different radio button. The whole BRDF is
/// therefore scaled by π, which is the same as saying the engine's lights carry irradiance
/// with the 1/π already folded in. It changes the exposure, never the relative weight of
/// diffuse against specular — which is the part that has to be right.
///
/// Output is always linear light, unclamped. There is no encoded-byte path of the kind
/// <see cref="Scenes.Scene.GammaCorrect"/> selects for the older shaders: this model is
/// defined in linear light, and the framebuffer encodes it on the way out either way.
/// </summary>
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
    private readonly AmbientCube _ambient;
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
        AmbientCube ambient,
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

        // Metallic from blue, roughness from green: the channels glTF packs them into, and
        // the same value in every channel of the greyscale maps an OBJ brings.
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

        // Clamped away from zero: at exactly grazing incidence every term below divides by
        // it, and a surface seen edge-on is one pixel wide, not a stripe of infinities.
        var nDotV = MathF.Max(Vector3.Dot(n, view), 1e-4f);

        // A dielectric reflects the same few percent whatever colour it is, and keeps its
        // albedo for the diffuse it scatters. A metal has no diffuse at all, and tints its
        // reflection with the albedo instead. This one interpolation is the whole difference.
        var f0 = LinearColor.Lerp(new LinearColor(Ggx.DielectricF0, Ggx.DielectricF0, Ggx.DielectricF0), albedo, metallic);
        var diffuseColor = albedo * (1f - metallic);

        var result = Direct(v.World, n, view, nDotV, alpha, f0, diffuseColor);

        result += Ambient(n, view, nDotV, roughness, f0, diffuseColor);

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

    /// <summary>The scene's lights, each one a single direction and so a single evaluation of the BRDF.</summary>
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

            // D · V · F is the specular BRDF; the π is the exposure correction the summary
            // describes, applied to both terms so their ratio is untouched.
            var specularWeight = MathF.PI * Ggx.Distribution(nDotH, alpha) * Ggx.Visibility(nDotV, nDotL, alpha);

            // Whatever Fresnel reflects cannot also be transmitted and scattered back out,
            // so the diffuse term gets what the specular left.
            var diffuse = new LinearColor(
                diffuseColor.R * (1f - fresnel.R),
                diffuseColor.G * (1f - fresnel.G),
                diffuseColor.B * (1f - fresnel.B));

            var brdf = diffuse + specularWeight * fresnel;

            total += (nDotL * attenuation) * (brdf * light.Color);
        }

        return total;
    }

    /// <summary>
    /// The environment, as both halves of what it contributes: the light arriving from
    /// everywhere at once, and the image of itself the surface reflects.
    ///
    /// The specular half is the split-sum approximation —
    /// <see cref="PrefilteredEnvironment"/> for the light, <see cref="BrdfLut"/> for the
    /// surface's response to it. With no environment to prefilter, both halves fall back to
    /// the <see cref="AmbientCube"/>, evaluated along the reflection direction rather than
    /// the normal: six directional averages are a poor mirror, but a surface reflecting the
    /// bright side of the room stays brighter than one reflecting the dark side, which is
    /// the part that reads.
    /// </summary>
    private LinearColor Ambient(
        Vector3 n,
        Vector3 view,
        float nDotV,
        float roughness,
        LinearColor f0,
        LinearColor diffuseColor)
    {
        // Fresnel with roughness folded in. The plain form assumes a perfect mirror and
        // sends every grazing pixel of a rough surface to white; this keeps the edge
        // brightening a smooth surface deserves and a rough one does not.
        var weight = Ggx.FresnelWeight(nDotV);
        var ceiling = MathF.Max(1f - roughness, Ggx.DielectricF0);

        var fresnel = new LinearColor(
            f0.R + (MathF.Max(ceiling, f0.R) - f0.R) * weight,
            f0.G + (MathF.Max(ceiling, f0.G) - f0.G) * weight,
            f0.B + (MathF.Max(ceiling, f0.B) - f0.B) * weight);

        var irradiance = _ambient.Evaluate(n);

        var diffuse = new LinearColor(
            diffuseColor.R * irradiance.R * (1f - fresnel.R),
            diffuseColor.G * irradiance.G * (1f - fresnel.G),
            diffuseColor.B * irradiance.B * (1f - fresnel.B));

        // The direction the surface would reflect the eye along.
        var reflection = 2f * Vector3.Dot(n, view) * n - view;

        var incoming = _environment is { } environment
            ? environment.Sample(reflection, roughness)
            : _ambient.Evaluate(reflection);

        var response = BrdfLut.Sample(nDotV, roughness);
        var scale = response.X;
        var bias = response.Y;

        var specular = new LinearColor(
            incoming.R * (f0.R * scale + bias),
            incoming.G * (f0.G * scale + bias),
            incoming.B * (f0.B * scale + bias));

        return diffuse + specular;
    }

    /// <summary>
    /// The interpolated vertex normal, tilted by the normal map when there is one and the
    /// mesh brought a usable tangent — identical to <see cref="MaterialShader"/>'s, because
    /// a normal map describes geometry and knows nothing about the lighting model reading it.
    /// </summary>
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
