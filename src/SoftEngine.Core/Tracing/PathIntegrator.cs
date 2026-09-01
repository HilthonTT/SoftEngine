using SoftEngine.Core.Acceleration;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Tracing;

internal sealed class PathIntegrator
{
    private readonly Bvh _accelerator;
    private readonly SceneGeometry _geometry;
    private readonly ShaderLight[] _lights;
    private readonly CubeMap? _environment;
    private readonly float _skyIntensity;
    private readonly bool _showSky;
    private readonly TraceSettings _settings;

    public PathIntegrator(
        Bvh accelerator,
        ShaderLight[] lights,
        CubeMap? environment,
        float skyIntensity,
        bool showSky,
        TraceSettings settings)
    {
        _accelerator = accelerator;
        _geometry = accelerator.Geometry;
        _lights = lights;
        _environment = environment;
        _skyIntensity = skyIntensity;
        _showSky = showSky;
        _settings = settings;
    }

    public LinearColor Radiance(Ray ray, ref Sampler sampler, out float firstDistance)
    {
        var radiance = LinearColor.Black;
        var throughput = LinearColor.White;

        firstDistance = float.PositiveInfinity;

        var bounces = System.Math.Max(0, _settings.MaxBounces);

        const int passThroughs = 8;

        var bounce = 0;
        var first = true;

        for (var step = 0; step <= bounces + passThroughs; step++)
        {
            if (!_accelerator.Intersect(ray, float.PositiveInfinity, out var hit))
            {
                if (_environment is { } sky && (!first || _showSky))
                {
                    radiance += throughput * (_skyIntensity * sky.SampleRadiance(ray.Direction));
                }

                break;
            }

            if (first)
            {
                firstDistance = hit.Distance;
                first = false;
            }

            var travelled = MathF.Max(hit.Distance, 1e-4f);

            var surface = Evaluate(_geometry, hit, ray);

            if (surface.Opacity < 1f && sampler.Next() >= surface.Opacity)
            {
                ray = new Ray(surface.Point + ray.Direction * (travelled * _settings.RayOffset), ray.Direction);
                continue;
            }

            radiance += throughput * surface.Emissive;

            var view = -Vector3.Normalize(ray.Direction);
            var normal = surface.Normal;

            var nDotV = MathF.Max(Vector3.Dot(normal, view), 1e-4f);
            var alpha = Ggx.Alpha(surface.Roughness);

            var f0 = LinearColor.Lerp(
                new LinearColor(Ggx.DielectricF0, Ggx.DielectricF0, Ggx.DielectricF0),
                surface.Albedo,
                surface.Metallic);

            var diffuseColor = surface.Albedo * (1f - surface.Metallic);

            radiance += throughput * Direct(surface, view, nDotV, alpha, f0, diffuseColor, travelled);

            if (bounce >= bounces)
            {
                break;
            }

            if (bounce >= _settings.RouletteDepth)
            {
                var survival = System.Math.Clamp(throughput.Luminance, 0.05f, 1f);

                if (sampler.Next() >= survival)
                {
                    break;
                }

                throughput = (1f / survival) * throughput;
            }

            if (!Scatter(ref sampler, surface, view, normal, nDotV, alpha, f0, diffuseColor,
                    out var direction, out var weight))
            {
                break;
            }

            throughput *= weight;

            if (throughput.Luminance <= 0f)
            {
                break;
            }

            ray = new Ray(surface.Point + normal * (travelled * _settings.RayOffset), direction);
            bounce++;
        }

        return radiance;
    }

    public bool FirstHit(in Ray ray, out float distance, out bool backface)
    {
        distance = float.PositiveInfinity;
        backface = false;

        if (!_accelerator.Intersect(ray, float.PositiveInfinity, out var hit))
        {
            return false;
        }

        var (a, b, c) = _geometry.Corners(hit.Triangle);

        distance = hit.Distance;

        backface = Vector3.Dot(Vector3.Cross(b - a, c - a), ray.Direction) > 0f;

        return true;
    }

    private LinearColor Direct(
        in TracedSurface surface,
        Vector3 view,
        float nDotV,
        float alpha,
        LinearColor f0,
        LinearColor diffuseColor,
        float travelled)
    {
        var total = LinearColor.Black;
        var offset = travelled * _settings.RayOffset;

        for (var i = 0; i < _lights.Length; i++)
        {
            ref readonly var light = ref _lights[i];

            if (!light.Sample(surface.Point, out var toLight, out var attenuation))
            {
                continue;
            }

            var nDotL = Vector3.Dot(surface.Normal, toLight);

            if (nDotL <= 0f)
            {
                continue;
            }

            var reach = light.IsDirectional
                ? float.PositiveInfinity
                : (light.Vector - surface.Point).Length() - offset;

            var shadow = new Ray(surface.Point + surface.Normal * offset, toLight);

            if (reach > 0f && _accelerator.IsOccluded(shadow, reach))
            {
                continue;
            }

            var half = Vector3.Normalize(toLight + view);

            var nDotH = MathF.Max(Vector3.Dot(surface.Normal, half), 0f);
            var vDotH = MathF.Max(Vector3.Dot(view, half), 0f);

            var fresnel = Ggx.Fresnel(f0, vDotH);

            var specular = Ggx.Distribution(nDotH, alpha) * Ggx.Visibility(nDotV, nDotL, alpha);

            var diffuse = (1f / MathF.PI) * new LinearColor(
                diffuseColor.R * (1f - fresnel.R),
                diffuseColor.G * (1f - fresnel.G),
                diffuseColor.B * (1f - fresnel.B));

            var brdf = diffuse + specular * fresnel;

            total += (_settings.DirectLightScale * nDotL * attenuation) * (brdf * light.Color);
        }

        return total;
    }

    private static bool Scatter(
        ref Sampler sampler,
        in TracedSurface surface,
        Vector3 view,
        Vector3 normal,
        float nDotV,
        float alpha,
        LinearColor f0,
        LinearColor diffuseColor,
        out Vector3 direction,
        out LinearColor weight)
    {
        direction = default;
        weight = LinearColor.Black;

        var fresnel = Ggx.Fresnel(f0, nDotV);

        var specularWeight = fresnel.Luminance;
        var diffuseWeight = diffuseColor.Luminance * (1f - specularWeight);

        var sum = specularWeight + diffuseWeight;

        var specularProbability = sum > 1e-6f
            ? System.Math.Clamp(specularWeight / sum, 0.05f, 0.95f)
            : 0.5f;

        if (sampler.Next() < specularProbability)
        {
            var (tangent, bitangent) = Ggx.BasisAround(normal);

            var tangentHalf = Ggx.ImportanceSampleHalfVector(sampler.NextPair(), alpha);
            var half = Vector3.Normalize(tangent * tangentHalf.X + bitangent * tangentHalf.Y + normal * tangentHalf.Z);

            var light = Vector3.Reflect(-view, half);

            var nDotL = Vector3.Dot(normal, light);

            if (nDotL <= 0f)
            {
                return false;
            }

            var vDotH = MathF.Max(Vector3.Dot(view, half), 1e-4f);
            var nDotH = MathF.Max(Vector3.Dot(normal, half), 1e-4f);

            var scale = 4f * Ggx.Visibility(nDotV, nDotL, alpha) * nDotL * vDotH / nDotH;

            direction = light;
            weight = (scale / specularProbability) * Ggx.Fresnel(f0, vDotH);

            return true;
        }

        direction = sampler.NextCosineDirection(normal);

        var diffuseFresnel = Ggx.Fresnel(f0, MathF.Max(Vector3.Dot(normal, direction), 0f));

        weight = (1f / (1f - specularProbability)) * new LinearColor(
            diffuseColor.R * (1f - diffuseFresnel.R),
            diffuseColor.G * (1f - diffuseFresnel.G),
            diffuseColor.B * (1f - diffuseFresnel.B));

        return true;
    }

    private static TracedSurface Evaluate(SceneGeometry geometry, in Bvh.Hit hit, in Ray ray)
    {
        var triangle = hit.Triangle;

        var w = hit.W;
        var u = hit.U;
        var v = hit.V;

        var (a, b, c) = geometry.Corners(triangle);
        var point = a * w + b * u + c * v;

        var shading = geometry.Normal(triangle, 0) * w +
                      geometry.Normal(triangle, 1) * u +
                      geometry.Normal(triangle, 2) * v;

        var normal = shading.LengthSquared() > 1e-20f
            ? Vector3.Normalize(shading)
            : Vector3.Normalize(Vector3.Cross(b - a, c - a));

        if (Vector3.Dot(normal, ray.Direction) > 0f)
        {
            normal = -normal;
        }

        var mesh = geometry.Mesh(triangle);
        var material = mesh.Material;

        var uv = geometry.HasTexCoords
            ? geometry.TexCoord(triangle, 0) * w + geometry.TexCoord(triangle, 1) * u + geometry.TexCoord(triangle, 2) * v
            : Vector2.Zero;

        var albedo = Albedo(geometry, triangle, mesh, material, uv);

        var metallic = material?.Metallic ?? 0f;
        var roughness = material?.Roughness ?? 0.5f;

        if (material?.MetallicMap is { } metallicMap)
        {
            metallic *= metallicMap.Sample(uv.X, uv.Y).B * (1f / 255f);
        }

        if (material?.RoughnessMap is { } roughnessMap)
        {
            roughness *= roughnessMap.Sample(uv.X, uv.Y).G * (1f / 255f);
        }

        if (material?.NormalMap is { } normalMap && geometry.HasTangents)
        {
            normal = TiltedNormal(geometry, triangle, normal, normalMap, uv, material.NormalStrength, w, u, v);
        }

        var emissive = LinearColor.Black;

        if (material is not null && material.EmissiveStrength > 0f)
        {
            LinearColor authored = material.Emissive;

            emissive = material.EmissiveMap is { } emissiveMap
                ? material.EmissiveStrength * (authored * (LinearColor)emissiveMap.Sample(uv.X, uv.Y))
                : material.EmissiveStrength * authored;
        }

        return new TracedSurface
        {
            Point = point,
            Normal = normal,
            Albedo = albedo,
            Metallic = System.Math.Clamp(metallic, 0f, 1f),
            Roughness = System.Math.Clamp(roughness, 0f, 1f),
            Emissive = emissive,
            Opacity = System.Math.Clamp(mesh.Opacity, 0f, 1f),
        };
    }

    private static LinearColor Albedo(SceneGeometry geometry, int triangle, IMesh mesh, Material? material, Vector2 uv)
    {
        if (material is not null)
        {
            if (material.DiffuseMap is { } map && geometry.HasTexCoords)
            {
                return map.Sample(uv.X, uv.Y);
            }

            return material.Diffuse;
        }

        if (mesh.Texture is { } texture && geometry.HasTexCoords)
        {
            return texture.Sample(uv.X, uv.Y);
        }

        var colors = mesh.TriangleColors;
        var source = geometry.SourceTriangle(triangle);

        return colors.Length > source ? colors[source] : LinearColor.White;
    }

    private static Vector3 TiltedNormal(
        SceneGeometry geometry,
        int triangle,
        Vector3 normal,
        Texture normalMap,
        Vector2 uv,
        float strength,
        float w, float u, float v)
    {
        var interpolated = geometry.Tangent(triangle, 0) * w +
                           geometry.Tangent(triangle, 1) * u +
                           geometry.Tangent(triangle, 2) * v;

        var tangent = new Vector3(interpolated.X, interpolated.Y, interpolated.Z);

        tangent -= normal * Vector3.Dot(normal, tangent);

        if (tangent.LengthSquared() < 1e-12f)
        {
            return normal;
        }

        tangent = Vector3.Normalize(tangent);

        var handedness = interpolated.W < 0f ? -1f : 1f;
        var bitangent = Vector3.Cross(normal, tangent) * handedness;

        var sample = normalMap.Sample(uv.X, uv.Y);

        var tilt = new Vector3(
            sample.R * (2f / 255f) - 1f,
            sample.G * (2f / 255f) - 1f,
            sample.B * (2f / 255f) - 1f);

        tilt = new Vector3(tilt.X * strength, tilt.Y * strength, MathF.Max(tilt.Z, 1e-4f));

        var tilted = tangent * tilt.X + bitangent * tilt.Y + normal * tilt.Z;

        return tilted.LengthSquared() > 1e-12f ? Vector3.Normalize(tilted) : normal;
    }

    public static ShaderLight[] Lights(IWorld world)
    {
        if (world.Lights.Count == 0)
        {
            return [ShaderLight.From(SceneLights.Default)];
        }

        var lights = new ShaderLight[world.Lights.Count];

        for (var i = 0; i < lights.Length; i++)
        {
            lights[i] = ShaderLight.From(world.Lights[i]);
        }

        return lights;
    }

    private readonly struct TracedSurface
    {
        public required Vector3 Point { get; init; }

        public required Vector3 Normal { get; init; }

        public required LinearColor Albedo { get; init; }

        public required float Metallic { get; init; }

        public required float Roughness { get; init; }

        public required LinearColor Emissive { get; init; }

        public required float Opacity { get; init; }
    }
}
