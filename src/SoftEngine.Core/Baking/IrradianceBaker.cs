using SoftEngine.Core.Acceleration;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Shading;
using SoftEngine.Core.Textures;
using SoftEngine.Core.Tracing;
using System.Numerics;

namespace SoftEngine.Core.Baking;

public static class IrradianceBaker
{
    private const int ValidityRays = 32;

    private static readonly float GoldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

    public static IrradianceVolume Bake(Scene scene, BakeSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(scene, nameof(scene));

        return Bake(scene.World, scene.Environment, scene.SkyIntensity, settings);
    }

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

            showSky: true,
            trace);

        var (min, max) = Extent(accelerator, settings.Padding);
        var (countX, countY, countZ) = Counts(max - min, settings.Resolution);

        var count = countX * countY * countZ;

        var probes = new AmbientCube[count];
        var valid = new bool[count];

        var volume = new IrradianceVolume(min, max, countX, countY, countZ, probes, valid, default);

        Parallel.For(0, count, index =>
        {
            var origin = volume.ProbePosition(index);

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

            var sampler = new Sampler(settings.Seed, index, i + 1);

            var light = integrator.Radiance(new Ray(origin, direction), ref sampler, out _);

            var r = MathF.Min(light.R, ceiling);
            var g = MathF.Min(light.G, ceiling);
            var b = MathF.Min(light.B, ceiling);

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

    private static Vector3 Direction(int i, int count, float jitter, float phase)
    {
        var z = 1f - 2f * (i + jitter) / count;
        var radius = MathF.Sqrt(MathF.Max(0f, 1f - z * z));

        var angle = phase + i * GoldenAngle;

        return new Vector3(radius * MathF.Cos(angle), radius * MathF.Sin(angle), z);
    }

    private static (Vector3 Min, Vector3 Max) Extent(Bvh accelerator, float padding)
    {
        var (min, max) = accelerator.Bounds;

        if (!(min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z))
        {
            return (new Vector3(-0.5f), new Vector3(0.5f));
        }

        var size = max - min;
        var longest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));

        var margin = MathF.Max(longest, 1e-3f) * MathF.Max(padding, 0f);

        if (margin <= 0f)
        {
            margin = 1e-3f;
        }

        return (min - new Vector3(margin), max + new Vector3(margin));
    }

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
