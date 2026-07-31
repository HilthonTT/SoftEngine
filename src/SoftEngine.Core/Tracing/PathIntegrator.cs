using SoftEngine.Core.Acceleration;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tracing;

/// <summary>
/// One ray in, the light that came back along it out — the whole of what a path tracer does,
/// separated from the frame it was being done for.
///
/// <para>
/// <see cref="PathTracer"/> asks this question once per sample per pixel.
/// <see cref="Baking.IrradianceBaker"/> asks it a few hundred times per probe, from points that are
/// not on the camera's film and have no pixel to write to. Those are the same question, and the
/// answer has to be produced by the same code: a bake is only worth anything if the light it stores
/// is the light the reference renderer would have found there. Two implementations of this walk —
/// however carefully written — would be free to disagree, and the disagreement would show up as the
/// rasterizer's ambient term being subtly wrong against the very renderer it exists to be checked
/// against.
/// </para>
///
/// <para>
/// It carries no buffers and no frame state, so one instance answers rays from any number of threads
/// at once; everything that varies per path lives in the caller's <see cref="Sampler"/>.
/// </para>
/// </summary>
internal sealed class PathIntegrator
{
    private readonly Bvh _accelerator;
    private readonly SceneGeometry _geometry;
    private readonly ShaderLight[] _lights;
    private readonly CubeMap? _environment;
    private readonly float _skyIntensity;
    private readonly bool _showSky;
    private readonly TraceSettings _settings;

    /// <param name="showSky">
    /// Whether a <em>primary</em> ray that escapes picks up the environment. A frame that does not
    /// draw its sky is still lit by it, so this suppresses only the first segment; every bounce
    /// after that collects the environment either way. A bake has no primary segment in that sense —
    /// nothing is looking at its rays — so it leaves this on.
    /// </param>
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

    /// <summary>
    /// Follows one path and returns the light that came back along it.
    ///
    /// The loop carries a <em>throughput</em> — what fraction of the light found from here would
    /// reach the eye — and adds emission and direct lighting scaled by it at every surface. That is
    /// the same sum as recursing, written so the recursion is a loop and the depth limit is a
    /// counter.
    /// </summary>
    public LinearColor Radiance(Ray ray, ref Sampler sampler, out float firstDistance)
    {
        var radiance = LinearColor.Black;
        var throughput = LinearColor.White;

        firstDistance = float.PositiveInfinity;

        var bounces = System.Math.Max(0, _settings.MaxBounces);

        // Passing through a transparent surface is not a bounce — no light is scattered and the ray
        // keeps going the way it was — but it is a step, and a stack of glass could otherwise loop
        // forever. This is the allowance for those.
        const int passThroughs = 8;

        var bounce = 0;
        var first = true;

        for (var step = 0; step <= bounces + passThroughs; step++)
        {
            if (!_accelerator.Intersect(ray, float.PositiveInfinity, out var hit))
            {
                // Nothing out there: the environment, unless this is the pixel's first ray and the
                // scene says the sky is not drawn — an environment can light a frame it is not in.
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

            // A partly transparent surface is a probability, not a blend: pass through it with the
            // chance it is see-through, and shade it otherwise. Averaged over paths that is the
            // blend, and unlike a blend it composes to any depth without sorting anything.
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

            // Russian roulette: past a couple of bounces, kill paths in proportion to how little
            // light they still carry rather than truncating every one of them at a fixed depth.
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

    /// <summary>
    /// The nearest surface along a ray, reported as the side facing the ray and whether the ray
    /// arrived at its back.
    ///
    /// Only the baker asks this, and only to find out that it is inside a wall: a probe buried in
    /// geometry sees backfaces in every direction, and the light it would otherwise store is the
    /// inside of the wall rather than the room.
    /// </summary>
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

        // The geometric normal, not the interpolated one: this is a question about which side of a
        // surface the ray came from, and a shading normal is allowed to lie about that.
        backface = Vector3.Dot(Vector3.Cross(b - a, c - a), ray.Direction) > 0f;

        return true;
    }

    /// <summary>
    /// Light arriving straight from the scene's lights, each one shadowed by a single ray.
    ///
    /// This is next-event estimation: a delta light has zero chance of being found by a scattered
    /// ray, so it is sampled explicitly at every surface instead. Scaled by
    /// <see cref="TraceSettings.DirectLightScale"/>, whose default matches the rasterizer's exposure.
    /// </summary>
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

            // The same resolve the rasterizer's shaders do, from the same flattened light — so the
            // range falloff and the spot cone are not merely similar between the two renderers,
            // they are the same code.
            if (!light.Sample(surface.Point, out var toLight, out var attenuation))
            {
                continue;
            }

            var nDotL = Vector3.Dot(surface.Normal, toLight);

            if (nDotL <= 0f)
            {
                continue;
            }

            // How far the shadow ray may travel before it is past the light and anything it finds
            // is behind it. A directional light is infinitely far away, so its shadow ray runs
            // until it leaves the scene.
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

            // The physical BRDF: Lambert is albedo over π, and what Fresnel reflects cannot also be
            // transmitted and scattered back out.
            var diffuse = (1f / MathF.PI) * new LinearColor(
                diffuseColor.R * (1f - fresnel.R),
                diffuseColor.G * (1f - fresnel.G),
                diffuseColor.B * (1f - fresnel.B));

            var brdf = diffuse + specular * fresnel;

            // ShaderLight.Color already carries the light's intensity.
            total += (_settings.DirectLightScale * nDotL * attenuation) * (brdf * light.Color);
        }

        return total;
    }

    /// <summary>
    /// Chooses which way the path continues and what fraction of the light survives the bounce.
    ///
    /// One of the two lobes is picked at random, in proportion to how much light each is expected to
    /// carry, and the chosen one's weight is divided by that probability — so the average over many
    /// paths is the sum of both lobes, at the cost of one ray instead of two.
    /// </summary>
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
                // The sampled microfacet reflects the view below the surface. The path ends here,
                // which is not a bias: those samples carry no light in the first place.
                return false;
            }

            var vDotH = MathF.Max(Vector3.Dot(view, half), 1e-4f);
            var nDotH = MathF.Max(Vector3.Dot(normal, half), 1e-4f);

            // The GGX estimator with the distribution cancelled out: sampling the half vector from
            // D means the D in the BRDF and the D in the probability are the same number.
            var scale = 4f * Ggx.Visibility(nDotV, nDotL, alpha) * nDotL * vDotH / nDotH;

            direction = light;
            weight = (scale / specularProbability) * Ggx.Fresnel(f0, vDotH);

            return true;
        }

        // Cosine-weighted, which is the density a Lambertian surface reflects in — so the albedo is
        // the whole weight and the cosine never appears.
        direction = sampler.NextCosineDirection(normal);

        var diffuseFresnel = Ggx.Fresnel(f0, MathF.Max(Vector3.Dot(normal, direction), 0f));

        weight = (1f / (1f - specularProbability)) * new LinearColor(
            diffuseColor.R * (1f - diffuseFresnel.R),
            diffuseColor.G * (1f - diffuseFresnel.G),
            diffuseColor.B * (1f - diffuseFresnel.B));

        return true;
    }

    /// <summary>
    /// Everything about the surface a ray landed on: the interpolated frame, and the material
    /// resolved at that point.
    /// </summary>
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

        // A ray arriving at the back of a surface shades against the side it can see. Without this
        // every inward-facing wall of a room is black, and so is the room.
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

    /// <summary>
    /// The surface colour, taking the same fallback chain the painters do: the material's map, its
    /// base colour, the mesh's own texture, and finally the per-triangle colour a mesh that predates
    /// materials still carries.
    /// </summary>
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

    /// <summary>The shading normal after a tangent-space normal map has tilted it.</summary>
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

        // Gram-Schmidt against the normal: interpolating a frame does not keep it orthogonal.
        tangent -= normal * Vector3.Dot(normal, tangent);

        if (tangent.LengthSquared() < 1e-12f)
        {
            return normal;
        }

        tangent = Vector3.Normalize(tangent);

        var handedness = interpolated.W < 0f ? -1f : 1f;
        var bitangent = Vector3.Cross(normal, tangent) * handedness;

        // A normal map holds directions, not colour, so its bytes are never gamma-decoded.
        var sample = normalMap.Sample(uv.X, uv.Y);

        var tilt = new Vector3(
            sample.R * (2f / 255f) - 1f,
            sample.G * (2f / 255f) - 1f,
            sample.B * (2f / 255f) - 1f);

        tilt = new Vector3(tilt.X * strength, tilt.Y * strength, MathF.Max(tilt.Z, 1e-4f));

        var tilted = tangent * tilt.X + bitangent * tilt.Y + normal * tilt.Z;

        return tilted.LengthSquared() > 1e-12f ? Vector3.Normalize(tilted) : normal;
    }

    /// <summary>
    /// The scene's lights, flattened exactly as the painters flatten them — including the fallback
    /// they share when a world has none, so an unlit world traces to the same picture it rasterizes
    /// to rather than to black.
    /// </summary>
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

    /// <summary>A shaded point, resolved once and read by both the direct and the scattered term.</summary>
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
