using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Per-pixel Blinn-Phong over a <see cref="Geometry.Material"/>, summed over every light in
/// the scene: albedo from a diffuse map, the shading normal perturbed by a tangent-space
/// normal map, and the highlight's strength masked by a specular map. Shadows, when the
/// scene casts them, are sampled here too, for the one light the map was rendered from.
///
/// The interesting part is the normal map. It replaces the one thing interpolation cannot
/// give you — surface detail finer than a vertex — without adding a single triangle: the
/// map stores a normal per texel relative to the surface's own UV frame, and the frame
/// carried by <see cref="MaterialVarying"/> rotates it into world space at every pixel.
/// Every map is optional; with none of them this shades exactly like
/// <see cref="BlinnPhongShader"/>.
/// </summary>
public readonly struct MaterialShader : IPixelShader<MaterialVarying>
{
    private readonly TextureSampler _albedo;
    private readonly TextureSampler _normalMap;
    private readonly TextureSampler _specularMap;

    private readonly ColorRGB _color;
    private readonly LightSet _lights;
    private readonly Vector3 _eye;
    private readonly AmbientCube _ambient;
    private readonly float _specularStrength;
    private readonly float _shininess;
    private readonly float _normalStrength;
    private readonly ShadowMap? _shadows;
    private readonly bool _gammaCorrect;

    // Same trick as BlinnPhongShader: a whole-number exponent is a few multiplies rather
    // than a MathF.Pow at every lit pixel.
    private readonly int _shininessInt;

    public MaterialShader(
        ColorRGB color,
        in TextureSampler albedo,
        in TextureSampler normalMap,
        in TextureSampler specularMap,
        LightSet lights,
        Vector3 eye,
        AmbientCube ambient,
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
            // A specular map is a mask, not a colour: the red channel is the convention.
            specularStrength *= _specularMap.Sample(v.UV).R * (1f / 255f);
        }

        // Evaluated with the shading normal, so a normal map shapes the ambient the same
        // way it shapes the lights — which is most of what makes it read as detail rather
        // than as a pattern printed on a flat surface.
        var diffuse = _ambient.Evaluate(n);
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
            // Unclamped, as in BlinnPhongShader: the highlight is allowed above white so an
            // HDR target can keep it and the tone-map can shape it.
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

    /// <summary>
    /// The interpolated vertex normal, tilted by the normal map when there is one and the
    /// mesh brought a usable tangent. The map's X and Y are scaled rather than the whole
    /// vector, so strength flattens or exaggerates the detail without changing its sign.
    /// </summary>
    private Vector3 ShadingNormal(in MaterialVarying v)
    {
        var n = Normalize(v.Normal, Vector3.UnitY);

        // A zero tangent means the mesh has no UV frame here — nothing to rotate the map into.
        if (!_normalMap.HasTexture || v.Tangent.LengthSquared() < 1e-12f)
        {
            return n;
        }

        var tangent = new Vector3(v.Tangent.X, v.Tangent.Y, v.Tangent.Z);

        // Re-orthogonalize: interpolating a frame across a triangle does not preserve the
        // right angle between the tangent and the normal.
        tangent -= n * Vector3.Dot(n, tangent);

        if (tangent.LengthSquared() < 1e-12f)
        {
            return n;
        }

        tangent = Vector3.Normalize(tangent);

        var bitangent = Vector3.Cross(n, tangent) * (v.Tangent.W < 0f ? -1f : 1f);

        var texel = _normalMap.Sample(v.UV);

        // Decode (v + 1) / 2 back to a direction. No gamma decode: this is geometry.
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
