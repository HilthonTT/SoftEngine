using SoftEngine.Core.Acceleration;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using SoftEngine.Core.Tracing;
using System.Numerics;

namespace SoftEngine.Core.Baking;

/// <summary>
/// Measures the scene's indirect light once, into an <see cref="IrradianceVolume"/> the rasterizer
/// can then read a million times a frame.
///
/// <para>
/// The rasterizer and the path tracer are usually presented as alternatives: one is fast and
/// approximate, the other is slow and correct. A bake is the third thing — the slow renderer run
/// ahead of time over the part of the image that does not change quickly, and the fast one reading
/// the answer. Bounce light is exactly that part. It takes a hundred rays a point to compute and it
/// varies over metres, where a specular highlight varies over a pixel and has to be recomputed for
/// every frame from every angle.
/// </para>
///
/// <para>
/// So this fires rays out of a grid of points and asks <see cref="PathIntegrator"/> — the path
/// tracer's own walk — what comes back along each one. What it stores is the light arriving at a
/// probe from the surfaces around it: their direct lighting, bounced. The lights themselves are
/// never in it, because a delta light has no size for a ray to land on, which is what keeps the
/// rasterizer from counting the sun twice when it adds its own direct term to the ambient one.
/// </para>
///
/// <para>
/// <b>A bake is of one arrangement of a world.</b> Move a wall or a light and the volume describes a
/// room that no longer exists; nothing here notices, because noticing would mean rebaking, and the
/// whole point is that this ran ahead of time. That is the trade every baked-lighting system makes.
/// </para>
/// </summary>
public static class IrradianceBaker
{
    /// <summary>Directions used to decide whether a probe is buried, independent of the ray budget.</summary>
    private const int ValidityRays = 32;

    /// <summary>The golden angle, which is what makes the Fibonacci sphere spread rather than spiral.</summary>
    private static readonly float GoldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

    /// <summary>
    /// Bakes the scene's world, lit by its lights and its environment.
    ///
    /// <see cref="Scenes.Scene.AmbientIntensity"/> and <see cref="Scenes.Scene.AmbientFromEnvironment"/>
    /// are ignored on purpose: they configure the guess this replaces.
    /// </summary>
    public static IrradianceVolume Bake(Scene scene, BakeSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        return Bake(scene.World, scene.Environment, scene.SkyIntensity, settings);
    }

    /// <summary>Bakes a world directly, for callers that have no <see cref="Scene"/> to hand.</summary>
    public static IrradianceVolume Bake(
        IWorld world,
        CubeMap? environment,
        float skyIntensity,
        BakeSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        var accelerator = Bvh.Build(SceneGeometry.Build(world));

        return Bake(accelerator, world, environment, skyIntensity, settings);
    }

    /// <summary>
    /// Bakes against an already-built tree — the expensive half of the job, and one the viewer's path
    /// tracer may already have paid for.
    ///
    /// The tree has to have been built from <paramref name="world"/>. Handing over one built from
    /// something else bakes the light of a world nobody is looking at, which is not an error anything
    /// can detect.
    /// </summary>
    public static IrradianceVolume Bake(
        Bvh accelerator,
        IWorld world,
        CubeMap? environment,
        float skyIntensity,
        BakeSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(accelerator, nameof(accelerator));
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        settings ??= new BakeSettings();

        var trace = new TraceSettings
        {
            MaxBounces = System.Math.Max(0, settings.Bounces),
            LightFromEnvironment = settings.LightFromEnvironment,
            DirectLightScale = settings.DirectLightScale,
            Seed = settings.Seed,
        };

        var integrator = new PathIntegrator(
            accelerator,
            PathIntegrator.Lights(world),
            settings.LightFromEnvironment ? environment : null,
            MathF.Max(0f, skyIntensity),
            // A probe has no primary ray in the sense the frame does — nothing is looking along it —
            // so the sky lights it whether or not the camera would have drawn it.
            showSky: true,
            trace);

        var (min, max) = Extent(accelerator, settings.Padding);
        var (countX, countY, countZ) = Counts(max - min, settings.Resolution);

        var count = countX * countY * countZ;

        var probes = new AmbientCube[count];
        var valid = new bool[count];

        // Built now, filled below, and rebuilt at the end only to carry the average: asking the
        // volume itself where its probes are means a probe is traced from exactly the point the
        // lookup will later interpolate it from, rather than from a second calculation that agrees
        // with the first until one of them is changed.
        var volume = new IrradianceVolume(min, max, countX, countY, countZ, probes, valid, default);

        Parallel.For(0, count, index =>
        {
            var origin = volume.ProbePosition(index);

            // Two independent streams per probe: one for where its rays point, one for the paths
            // they turn into. Both are seeded from the probe's own index, so a probe's light does
            // not depend on how many threads ran or in what order.
            var directions = new Sampler(settings.Seed, index, 0);

            var jitter = directions.Next();
            var phase = directions.Next() * MathF.Tau;

            if (IsBuried(integrator, origin, jitter, phase, settings.InsideThreshold))
            {
                return;
            }

            probes[index] = Probe(integrator, index, origin, jitter, phase, settings);
            valid[index] = true;
        });

        return new IrradianceVolume(min, max, countX, countY, countZ, probes, valid, Average(probes, valid));
    }

    /// <summary>
    /// One probe: rays over the whole sphere, each one's returned light weighted onto the three cube
    /// faces it points toward.
    ///
    /// A face is the <em>cosine-weighted mean</em> of the radiance arriving around its axis, not the
    /// sum — which is the same quantity <see cref="AmbientCube.FromEnvironment"/> produces from a sky,
    /// so the two sources are interchangeable and a shader multiplying albedo by it is computing the
    /// reflected light correctly either way.
    /// </summary>
    private static AmbientCube Probe(
        PathIntegrator integrator,
        int index,
        Vector3 origin,
        float jitter,
        float phase,
        BakeSettings settings)
    {
        var rays = settings.Rays;

        Span<float> sum = stackalloc float[18];
        Span<float> weights = stackalloc float[6];

        sum.Clear();
        weights.Clear();

        var ceiling = settings.MaxRadiance > 0f ? settings.MaxRadiance : float.PositiveInfinity;

        for (var i = 0; i < rays; i++)
        {
            var direction = Direction(i, rays, jitter, phase);

            // Seeded from the probe and the ray, never from a shared generator: the same two
            // identifiers the frame renderer uses for a pixel and a sample, for the same reason.
            // Sample 0 belongs to the direction stream above, so paths start at 1.
            var sampler = new Sampler(settings.Seed, index, i + 1);

            var light = integrator.Radiance(new Ray(origin, direction), ref sampler, out _);

            var r = MathF.Min(light.R, ceiling);
            var g = MathF.Min(light.G, ceiling);
            var b = MathF.Min(light.B, ceiling);

            // Each direction lands on exactly three of the six faces — the ones whose axes it has a
            // positive component along — and contributes to each in proportion to that component,
            // which is the cosine the irradiance integral asks for.
            Accumulate(sum, weights, 0, MathF.Max(direction.X, 0f), r, g, b);
            Accumulate(sum, weights, 1, MathF.Max(-direction.X, 0f), r, g, b);
            Accumulate(sum, weights, 2, MathF.Max(direction.Y, 0f), r, g, b);
            Accumulate(sum, weights, 3, MathF.Max(-direction.Y, 0f), r, g, b);
            Accumulate(sum, weights, 4, MathF.Max(direction.Z, 0f), r, g, b);
            Accumulate(sum, weights, 5, MathF.Max(-direction.Z, 0f), r, g, b);
        }

        return new AmbientCube(
            Face(sum, weights, 0, settings.Intensity),
            Face(sum, weights, 1, settings.Intensity),
            Face(sum, weights, 2, settings.Intensity),
            Face(sum, weights, 3, settings.Intensity),
            Face(sum, weights, 4, settings.Intensity),
            Face(sum, weights, 5, settings.Intensity));
    }

    private static void Accumulate(Span<float> sum, Span<float> weights, int face, float weight, float r, float g, float b)
    {
        if (weight <= 0f)
        {
            return;
        }

        var slot = face * 3;

        sum[slot] += r * weight;
        sum[slot + 1] += g * weight;
        sum[slot + 2] += b * weight;

        weights[face] += weight;
    }

    private static LinearColor Face(Span<float> sum, Span<float> weights, int face, float intensity)
    {
        var weight = weights[face];

        if (weight <= 0f)
        {
            return LinearColor.Black;
        }

        var scale = intensity / weight;
        var slot = face * 3;

        return new LinearColor(sum[slot] * scale, sum[slot + 1] * scale, sum[slot + 2] * scale);
    }

    /// <summary>
    /// Whether the probe is inside geometry, decided by how many directions run into the back of a
    /// surface before they run into anything else.
    ///
    /// This is one intersection per direction with no shading and no bounces, so it costs a fraction
    /// of the probe it can save entirely.
    /// </summary>
    private static bool IsBuried(PathIntegrator integrator, Vector3 origin, float jitter, float phase, float threshold)
    {
        var backfaces = 0;

        for (var i = 0; i < ValidityRays; i++)
        {
            if (integrator.FirstHit(new Ray(origin, Direction(i, ValidityRays, jitter, phase)), out _, out var backface) &&
                backface)
            {
                backfaces++;
            }
        }

        return backfaces > ValidityRays * threshold;
    }

    /// <summary>
    /// Direction <paramref name="i"/> of <paramref name="count"/> over the sphere: a Fibonacci
    /// spiral, jittered as a whole.
    ///
    /// Stratifying beats sampling the sphere at random — a few hundred random directions leave gaps
    /// wide enough to miss a window — and jittering the whole set per probe keeps every probe in the
    /// grid from sampling the *same* few hundred directions, which would turn the estimator's error
    /// into a pattern that repeats across the volume instead of averaging out between neighbours.
    /// </summary>
    private static Vector3 Direction(int i, int count, float jitter, float phase)
    {
        var z = 1f - 2f * (i + jitter) / count;
        var radius = MathF.Sqrt(MathF.Max(0f, 1f - z * z));

        var angle = phase + i * GoldenAngle;

        return new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), z);
    }

    /// <summary>
    /// The box the probes are laid out over: the world's own bounds with a margin, and a unit box
    /// around the origin when there is no world to measure.
    /// </summary>
    private static (Vector3 Min, Vector3 Max) Extent(Bvh accelerator, float padding)
    {
        var (min, max) = accelerator.Bounds;

        if (!(min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z))
        {
            // An empty tree reports an inverted box. A volume over nothing is still worth baking —
            // its probes see the sky and nothing else, which is exactly what an empty world's
            // ambient light is.
            return (new Vector3(-0.5f), new Vector3(0.5f));
        }

        var size = max - min;
        var longest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));

        // A world that is one point, or one flat plane, still needs a box with an inside.
        var margin = MathF.Max(longest, 1e-3f) * MathF.Max(padding, 0f);

        if (margin <= 0f)
        {
            margin = 1e-3f;
        }

        return (min - new Vector3(margin), max + new Vector3(margin));
    }

    /// <summary>
    /// Probes per axis: <paramref name="resolution"/> along the longest, and proportionally fewer
    /// along the others, so a corridor does not get as many probes across as it does along.
    /// </summary>
    private static (int X, int Y, int Z) Counts(Vector3 size, int resolution)
    {
        var longest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));

        if (longest <= 0f)
        {
            return (2, 2, 2);
        }

        return (Axis(size.X), Axis(size.Y), Axis(size.Z));

        int Axis(float extent) =>
            System.Math.Clamp((int)MathF.Round(resolution * extent / longest), 2, resolution);
    }

    /// <summary>The mean of the probes worth averaging, which a lookup with no usable neighbour falls back to.</summary>
    private static AmbientCube Average(AmbientCube[] probes, bool[] valid)
    {
        Span<float> sum = stackalloc float[18];
        sum.Clear();

        var count = 0;

        for (var i = 0; i < probes.Length; i++)
        {
            if (!valid[i])
            {
                continue;
            }

            for (var face = 0; face < 6; face++)
            {
                var light = probes[i][(CubeFace)face];
                var slot = face * 3;

                sum[slot] += light.R;
                sum[slot + 1] += light.G;
                sum[slot + 2] += light.B;
            }

            count++;
        }

        if (count == 0)
        {
            return default;
        }

        var scale = 1f / count;

        return new AmbientCube(
            new LinearColor(sum[0] * scale, sum[1] * scale, sum[2] * scale),
            new LinearColor(sum[3] * scale, sum[4] * scale, sum[5] * scale),
            new LinearColor(sum[6] * scale, sum[7] * scale, sum[8] * scale),
            new LinearColor(sum[9] * scale, sum[10] * scale, sum[11] * scale),
            new LinearColor(sum[12] * scale, sum[13] * scale, sum[14] * scale),
            new LinearColor(sum[15] * scale, sum[16] * scale, sum[17] * scale));
    }
}
