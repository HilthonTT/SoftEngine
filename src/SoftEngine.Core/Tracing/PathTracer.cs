using SoftEngine.Core.Acceleration;
using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Lights;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tracing;

/// <summary>
/// The reference renderer: the same scene, answered by tracing light through it instead of by
/// filling triangles.
///
/// <para>
/// Everything the rasterizer does about light that does not arrive straight from a lamp is an
/// approximation standing in for this. Ambient light is a constant, or six of them. Occlusion is a
/// screen-space guess made from a depth buffer. A reflection is a prefiltered cube looked up along
/// one direction, and shadows are a depth map with a bias that has to be tuned. Each of those is
/// defensible on its own and none of them can be checked against anything — which is the problem
/// this solves. A path tracer computes the same integral by following actual paths, so the
/// approximations have something to be wrong *against*: render both, subtract, and the difference
/// is the error rather than an opinion.
/// </para>
///
/// <para>
/// It is an <see cref="IRenderer"/>, so it drops into the same slot the rasterizer and the GPU
/// backend occupy — same scene, same <see cref="FrameBuffer"/>, same post-process stack — and it
/// ignores the <see cref="IPainter"/> it is handed, because choosing a shading model per mesh is
/// exactly the thing it is here to not have to do.
/// </para>
///
/// <para>
/// What it is not: a production renderer. There is no bidirectional path tracing, no multiple
/// importance sampling and no light hierarchy, so a scene lit through a keyhole will be noise for a
/// very long time. Lights are the engine's own delta lights, sampled directly and shadowed with one
/// ray, so they cast hard shadows and no caustics. What it does have is unbiased diffuse and
/// specular interreflection, true ambient occlusion, and shadows with no bias to tune — the three
/// things nothing else here can produce.
/// </para>
/// </summary>
public sealed class PathTracer : IRenderer
{
    private Bvh? _accelerator;
    private int _geometryStamp;

    /// <summary>Running sum of radiance per pixel, three floats each, when accumulating.</summary>
    private float[] _accumulator = [];
    private float[] _depth = [];

    private int _width;
    private int _height;

    public RendererSettings Settings { get; set; } = new();

    public PostProcessStack? PostProcess { get; set; }

    public RenderStats Stats { get; } = new();

    public RenderDiagnostics Diagnostics { get; } = new();

    /// <summary>How many paths per pixel to spend, and what they are allowed to do.</summary>
    public TraceSettings Trace { get; } = new();

    /// <summary>
    /// The tree the last render built, or null before the first one. Exposed because building it is
    /// the expensive half of a first frame and a caller may want to know what it got — how many
    /// nodes, how deep — or to reuse it for something else that casts rays.
    /// </summary>
    public Bvh? Accelerator => _accelerator;

    /// <summary>Paths per pixel averaged into the image so far. Always <see cref="TraceSettings.SamplesPerPixel"/> unless accumulating.</summary>
    public int AccumulatedSamples { get; private set; }

    /// <summary>Throws away the accumulated image, so the next render starts from nothing.</summary>
    public void Reset() => AccumulatedSamples = 0;

    public void Render(Scene scene, IPainter? painter)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        var surface = scene.Surface;
        var world = scene.World;

        Stats.Clear();
        Stats.PaintTime();

        Diagnostics.FrameNumber++;

        var events = Diagnostics.Events;
        events.Clear();
        events.Add(GraphicsEventKind.FrameBegin, -1, Diagnostics.FrameNumber);
        events.Add(GraphicsEventKind.RendererSetViewport, SceneObjectIds.RenderTarget, surface.Width, surface.Height);

        surface.SetHighDynamicRange(scene.HighDynamicRange);
        surface.Clear();

        Refresh(world, surface.Width, surface.Height);

        var accelerator = _accelerator!;
        var geometry = accelerator.Geometry;

        Stats.TotalTriangleCount = geometry.TriangleCount;

        if (surface.Width <= 0 || surface.Height <= 0)
        {
            Stats.StopTime();
            return;
        }

        var camera = new CameraFrame(scene);
        var lights = Lights(world);

        var environment = Trace.LightFromEnvironment ? scene.Environment : null;
        var skyIntensity = MathF.Max(0f, scene.SkyIntensity);

        var samples = System.Math.Max(1, Trace.SamplesPerPixel);
        var previous = Trace.Accumulate ? AccumulatedSamples : 0;
        var total = previous + samples;

        if (previous == 0)
        {
            Array.Clear(_accumulator);
        }

        // The view-projection, for turning a hit position into the normalized depth the buffer
        // holds — so the depth view and the depth-reading post effects have the frame's geometry
        // even though no triangle was ever projected.
        var viewProjection = scene.Camera.ViewMatrix *
            scene.Projection.ProjectionMatrix(surface.Width, surface.Height);

        var width = surface.Width;
        var height = surface.Height;

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = x + y * width;

                var sum = LinearColor.Black;
                var depth = 1f;

                for (var s = 0; s < samples; s++)
                {
                    var sampler = new Sampler(Trace.Seed, pixel, previous + s);

                    // Jittered inside the pixel: the sample position *is* the antialiasing, since
                    // there is no coverage to compute and nothing to supersample afterwards.
                    var ray = camera.Through(
                        x + sampler.Next(),
                        y + sampler.Next());

                    sum += Radiance(
                        accelerator, geometry, ray, lights, environment, scene, skyIntensity,
                        ref sampler, out var distance);

                    // The depth buffer is one number per pixel and this is a distribution, so it
                    // records the first sample's hit rather than an average of positions that may
                    // be on different surfaces. Accumulating leaves the first frame's, which is the
                    // one the geometry has not moved since.
                    if (s == 0 && previous == 0 && !float.IsPositiveInfinity(distance))
                    {
                        depth = NormalizedDepth(ray.At(distance), viewProjection);
                    }
                }

                var slot = pixel * 3;

                _accumulator[slot] += sum.R;
                _accumulator[slot + 1] += sum.G;
                _accumulator[slot + 2] += sum.B;

                var scale = 1f / total;

                surface.PutBackground(x, y, new LinearColor(
                    _accumulator[slot] * scale,
                    _accumulator[slot + 1] * scale,
                    _accumulator[slot + 2] * scale));

                if (previous == 0)
                {
                    _depth[pixel] = depth;
                }
            }
        });

        AccumulatedSamples = total;

        Stats.AddPixelCounts(width * height, 0);
        Stats.CalculationTime();

        surface.WriteNormalizedDepth(_depth);

        events.Add(GraphicsEventKind.FramePresent, SceneObjectIds.RenderTarget, Stats.DrawnPixelCount, 0);

        Resolve(surface, scene);

        Stats.StopTime();
    }

    /// <summary>
    /// Rebuilds the acceleration structure when the world has moved, and the accumulation buffers
    /// when the frame has changed size. Either invalidates whatever was accumulated: an average of
    /// samples taken against different geometry is not an image of anything.
    /// </summary>
    private void Refresh(IWorld world, int width, int height)
    {
        var stamp = SceneGeometry.Stamp(world);

        if (_accelerator is null || stamp != _geometryStamp)
        {
            _accelerator = Bvh.Build(SceneGeometry.Build(world));
            _geometryStamp = stamp;

            AccumulatedSamples = 0;
        }

        if (_width == width && _height == height && _accumulator.Length == width * height * 3)
        {
            return;
        }

        _width = width;
        _height = height;

        _accumulator = new float[System.Math.Max(0, width * height * 3)];
        _depth = new float[System.Math.Max(0, width * height)];

        AccumulatedSamples = 0;
    }

    /// <summary>
    /// Follows one path and returns the light that came back along it.
    ///
    /// The loop carries a <em>throughput</em> — what fraction of the light found from here would
    /// reach the eye — and adds emission and direct lighting scaled by it at every surface. That is
    /// the same sum as recursing, written so the recursion is a loop and the depth limit is a
    /// counter.
    /// </summary>
    private LinearColor Radiance(
        Bvh accelerator,
        SceneGeometry geometry,
        Ray ray,
        ShaderLight[] lights,
        CubeMap? environment,
        Scene scene,
        float skyIntensity,
        ref Sampler sampler,
        out float firstDistance)
    {
        var radiance = LinearColor.Black;
        var throughput = LinearColor.White;

        firstDistance = float.PositiveInfinity;

        var bounces = System.Math.Max(0, Trace.MaxBounces);

        // Passing through a transparent surface is not a bounce — no light is scattered and the ray
        // keeps going the way it was — but it is a step, and a stack of glass could otherwise loop
        // forever. This is the allowance for those.
        const int passThroughs = 8;

        var bounce = 0;
        var first = true;

        for (var step = 0; step <= bounces + passThroughs; step++)
        {
            if (!accelerator.Intersect(ray, float.PositiveInfinity, out var hit))
            {
                // Nothing out there: the environment, unless this is the pixel's first ray and the
                // scene says the sky is not drawn — an environment can light a frame it is not in.
                if (environment is { } sky && (!first || scene.ShowSky))
                {
                    radiance += throughput * (skyIntensity * sky.SampleRadiance(ray.Direction));
                }

                break;
            }

            if (first)
            {
                firstDistance = hit.Distance;
                first = false;
            }

            var travelled = MathF.Max(hit.Distance, 1e-4f);

            var surface = Evaluate(geometry, hit, ray);

            // A partly transparent surface is a probability, not a blend: pass through it with the
            // chance it is see-through, and shade it otherwise. Averaged over paths that is the
            // blend, and unlike a blend it composes to any depth without sorting anything.
            if (surface.Opacity < 1f && sampler.Next() >= surface.Opacity)
            {
                ray = new Ray(surface.Point + ray.Direction * (travelled * Trace.RayOffset), ray.Direction);
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

            radiance += throughput * Direct(
                accelerator, surface, lights, view, nDotV, alpha, f0, diffuseColor, travelled);

            if (bounce >= bounces)
            {
                break;
            }

            // Russian roulette: past a couple of bounces, kill paths in proportion to how little
            // light they still carry rather than truncating every one of them at a fixed depth.
            if (bounce >= Trace.RouletteDepth)
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

            ray = new Ray(surface.Point + normal * (travelled * Trace.RayOffset), direction);
            bounce++;
        }

        return radiance;
    }

    /// <summary>
    /// Light arriving straight from the scene's lights, each one shadowed by a single ray.
    ///
    /// This is next-event estimation: a delta light has zero chance of being found by a scattered
    /// ray, so it is sampled explicitly at every surface instead. Scaled by
    /// <see cref="TraceSettings.DirectLightScale"/>, whose default matches the rasterizer's exposure.
    /// </summary>
    private LinearColor Direct(
        Bvh accelerator,
        in TracedSurface surface,
        ShaderLight[] lights,
        Vector3 view,
        float nDotV,
        float alpha,
        LinearColor f0,
        LinearColor diffuseColor,
        float travelled)
    {
        var total = LinearColor.Black;
        var offset = travelled * Trace.RayOffset;

        for (var i = 0; i < lights.Length; i++)
        {
            ref readonly var light = ref lights[i];

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

            if (reach > 0f && accelerator.IsOccluded(shadow, reach))
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
            total += (Trace.DirectLightScale * nDotL * attenuation) * (brdf * light.Color);
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
    private static ShaderLight[] Lights(IWorld world)
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

    /// <summary>
    /// Where a world position lands in the depth buffer's [0, 1]. The same divide the rasterizer's
    /// clip-space z goes through, so a traced depth buffer and a rasterized one hold the same
    /// numbers for the same surface.
    /// </summary>
    private static float NormalizedDepth(Vector3 point, in Matrix4x4 viewProjection)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);

        if (clip.W <= 0f)
        {
            return 1f;
        }

        return System.Math.Clamp(clip.Z / clip.W, 0f, 1f);
    }

    /// <summary>
    /// The same resolve the rasterizer ends a frame with: the post-process stack when there is one,
    /// and otherwise the encode an HDR target still needs.
    /// </summary>
    private void Resolve(FrameBuffer surface, Scene scene)
    {
        var stack = PostProcess is { HasEffects: true } candidate ? candidate : null;

        if (stack is not null)
        {
            stack.Apply(surface, scene.Projection);
        }
        else if (surface.IsHighDynamicRange)
        {
            surface.ResolveToScreen();
        }
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

    /// <summary>
    /// The camera, reduced to what firing a ray through a pixel needs.
    ///
    /// <see cref="ScenePicker.RayThrough"/> answers the same question and inverts the projection
    /// every time it is asked, which is right for one click a second and wrong for a million rays a
    /// frame. The arithmetic is deliberately identical, so a traced pixel and a picked one look
    /// along the same line.
    /// </summary>
    private readonly struct CameraFrame
    {
        private readonly Matrix4x4 _inverseView;
        private readonly float _invScaleX;
        private readonly float _invScaleY;
        private readonly float _toNdcX;
        private readonly float _toNdcY;
        private readonly bool _orthographic;

        public CameraFrame(Scene scene)
        {
            var surface = scene.Surface;
            var projection = scene.Projection;

            var matrix = projection.ProjectionMatrix(surface.Width, surface.Height);

            _invScaleX = 1f / (matrix.M11 == 0f ? 1f : matrix.M11);
            _invScaleY = 1f / (matrix.M22 == 0f ? 1f : matrix.M22);

            _toNdcX = 2f / MathF.Max(surface.Width - 1, 1);
            _toNdcY = 2f / MathF.Max(surface.Height - 1, 1);

            _orthographic = projection.IsOrthographic;

            _inverseView = Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverse)
                ? inverse
                : Matrix4x4.Identity;
        }

        public Ray Through(float x, float y)
        {
            var ndcX = x * _toNdcX - 1f;
            var ndcY = 1f - y * _toNdcY;

            // A parallel projection fires every ray the same way and moves the origin instead; a
            // perspective one fires them all from the eye.
            var (origin, direction) = _orthographic
                ? (new Vector3(ndcX * _invScaleX, ndcY * _invScaleY, 0f), -Vector3.UnitZ)
                : (Vector3.Zero, new Vector3(ndcX * _invScaleX, ndcY * _invScaleY, -1f));

            return new Ray(
                Vector3.Transform(origin, _inverseView),
                Vector3.Normalize(Vector3.TransformNormal(direction, _inverseView)));
        }

    }
}
