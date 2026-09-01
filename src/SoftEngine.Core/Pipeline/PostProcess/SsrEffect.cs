using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.PostProcess;

public sealed class SsrEffect : IPostEffect
{
    private float[] _reflection = [];
    private float[] _weight = [];
    private float[] _blurredReflection = [];
    private float[] _blurredWeight = [];

    private float[] _grazing = [];

    public string Name => "Reflections";

    public bool Enabled { get; set; }

    public bool NeedsDepth => true;

    public bool NeedsReflectance => true;

    public float Strength { get; set; } = 1f;

    public int MaxSteps { get; set; } = 64;

    public float MaxDistance { get; set; } = 40f;

    public float Thickness { get; set; } = 1.5f;

    public float MaxRoughness { get; set; } = 0.6f;

    public int BlurRadius { get; set; } = 3;

    public float EdgeFade { get; set; } = 0.15f;

    public void Apply(PostProcessTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.HasDepth || !target.HasReflectance || target.Width < 3 || target.Height < 3 || Strength <= 0f)
        {
            return;
        }

        var count = target.Width * target.Height;

        if (_weight.Length < count)
        {
            _reflection = new float[count * 3];
            _blurredReflection = new float[count * 3];
            _weight = new float[count];
            _blurredWeight = new float[count];
            _grazing = new float[count];
        }

        March(target);
        Blur(target);
        Composite(target);
    }

    private void March(PostProcessTarget target)
    {
        var width = target.Width;
        var height = target.Height;
        var depth = target.ViewDepth;
        var reflectance = target.Reflectance;
        var field = target.Field;
        var color = target.Color;

        var reflection = _reflection;
        var weight = _weight;
        var grazing = _grazing;

        var steps = System.Math.Clamp(MaxSteps, 1, 512);
        var maxDistance = MathF.Max(MaxDistance, 1e-3f);
        var stepLength = maxDistance / steps;
        var thickness = MathF.Max(Thickness, 1e-4f);
        var maxRoughness = System.Math.Clamp(MaxRoughness, 0f, 1f);

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = x + y * width;

                weight[index] = 0f;

                var surface = SurfaceReflectance.FromPacked(reflectance[index]);

                if (!surface.IsReflective || float.IsPositiveInfinity(depth[index]))
                {
                    continue;
                }

                var roughness = surface.Roughness;

                if (roughness > maxRoughness)
                {
                    continue;
                }

                var origin = field.PositionAt(x, y);
                var normal = field.NormalAt(x, y);

                if (normal == Vector3.Zero)
                {
                    continue;
                }

                var incident = Vector3.Normalize(origin);
                var direction = Vector3.Reflect(incident, normal);

                if (direction.Z >= 0f)
                {
                    continue;
                }

                if (!Trace(field, depth, origin, normal, direction, steps, stepLength, thickness,
                        out var hitX, out var hitY, out var travelled))
                {
                    continue;
                }

                var confidence = EdgeWeight(hitX, hitY, width, height)
                    * DistanceWeight(travelled, maxDistance);

                if (confidence <= 0f)
                {
                    continue;
                }

                var hit = (hitX + hitY * width) * 3;

                reflection[index * 3] = color[hit];
                reflection[index * 3 + 1] = color[hit + 1];
                reflection[index * 3 + 2] = color[hit + 2];
                weight[index] = confidence;
                grazing[index] = Grazing(incident, normal);
            }
        });
    }

    private static bool Trace(
        in DepthField field,
        float[] depth,
        Vector3 origin,
        Vector3 normal,
        Vector3 direction,
        int steps,
        float stepLength,
        float thickness,
        out int hitX,
        out int hitY,
        out float travelled)
    {
        hitX = 0;
        hitY = 0;
        travelled = 0f;

        var width = field.Width;

        var start = origin + normal * (stepLength * 0.5f);

        var previousDistance = 0f;

        for (var step = 1; step <= steps; step++)
        {
            var distance = step * stepLength;
            var point = start + direction * distance;

            if (!field.ProjectToScreen(point, out var sx, out var sy, out var rayDepth))
            {
                return false;
            }

            var sceneDepth = depth[sx + sy * width];

            if (float.IsPositiveInfinity(sceneDepth))
            {
                previousDistance = distance;
                continue;
            }

            var difference = rayDepth - sceneDepth;

            if (difference <= 0f)
            {
                previousDistance = distance;
                continue;
            }

            if (difference > thickness)
            {
                previousDistance = distance;
                continue;
            }

            Refine(field, depth, start, direction, previousDistance, distance, thickness,
                ref sx, ref sy, ref distance);

            hitX = sx;
            hitY = sy;
            travelled = distance;

            return true;
        }

        return false;
    }

    private static void Refine(
        in DepthField field,
        float[] depth,
        Vector3 start,
        Vector3 direction,
        float near,
        float far,
        float thickness,
        ref int hitX,
        ref int hitY,
        ref float distance)
    {
        const int Bisections = 6;

        var width = field.Width;

        for (var i = 0; i < Bisections; i++)
        {
            var middle = (near + far) * 0.5f;

            if (!field.ProjectToScreen(start + direction * middle, out var sx, out var sy, out var rayDepth))
            {
                break;
            }

            var sceneDepth = depth[sx + sy * width];
            var difference = rayDepth - sceneDepth;

            if (float.IsPositiveInfinity(sceneDepth) || difference <= 0f || difference > thickness)
            {
                near = middle;
                continue;
            }

            far = middle;
            hitX = sx;
            hitY = sy;
            distance = middle;
        }
    }

    private static float Grazing(Vector3 incident, Vector3 normal)
    {
        var cosine = System.Math.Clamp(Vector3.Dot(-incident, normal), 0f, 1f);
        var f = 1f - cosine;
        var f2 = f * f;

        return f2 * f2 * f;
    }

    private float EdgeWeight(int x, int y, int width, int height)
    {
        var fade = System.Math.Clamp(EdgeFade, 0f, 0.5f);

        if (fade <= 0f)
        {
            return 1f;
        }

        var u = (x + 0.5f) / width;
        var v = (y + 0.5f) / height;

        var horizontal = System.Math.Clamp(MathF.Min(u, 1f - u) / fade, 0f, 1f);
        var vertical = System.Math.Clamp(MathF.Min(v, 1f - v) / fade, 0f, 1f);

        return horizontal * vertical;
    }

    private static float DistanceWeight(float travelled, float maxDistance)
    {
        const float FadeFrom = 0.75f;

        var t = travelled / maxDistance;

        return t <= FadeFrom ? 1f : System.Math.Clamp((1f - t) / (1f - FadeFrom), 0f, 1f);
    }

    private void Blur(PostProcessTarget target)
    {
        var maxRadius = System.Math.Clamp(BlurRadius, 0, 8);

        if (maxRadius == 0)
        {
            return;
        }

        var width = target.Width;
        var height = target.Height;
        var reflectance = target.Reflectance;
        var maxRoughness = MathF.Max(MaxRoughness, 1e-4f);

        var source = _reflection;
        var sourceWeight = _weight;
        var destination = _blurredReflection;
        var destinationWeight = _blurredWeight;

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = x + y * width;

                destinationWeight[index] = sourceWeight[index];
                destination[index * 3] = source[index * 3];
                destination[index * 3 + 1] = source[index * 3 + 1];
                destination[index * 3 + 2] = source[index * 3 + 2];

                if (sourceWeight[index] <= 0f)
                {
                    continue;
                }

                var roughness = SurfaceReflectance.FromPacked(reflectance[index]).Roughness;
                var radius = (int)(roughness / maxRoughness * maxRadius + 0.5f);

                if (radius <= 0)
                {
                    continue;
                }

                float r = 0f, g = 0f, b = 0f, total = 0f;

                for (var dy = -radius; dy <= radius; dy++)
                {
                    var sy = y + dy;

                    if ((uint)sy >= (uint)height)
                    {
                        continue;
                    }

                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sx = x + dx;

                        if ((uint)sx >= (uint)width)
                        {
                            continue;
                        }

                        var neighbour = sx + sy * width;
                        var w = sourceWeight[neighbour];

                        if (w <= 0f)
                        {
                            continue;
                        }

                        r += source[neighbour * 3] * w;
                        g += source[neighbour * 3 + 1] * w;
                        b += source[neighbour * 3 + 2] * w;
                        total += w;
                    }
                }

                if (total <= 0f)
                {
                    continue;
                }

                destination[index * 3] = r / total;
                destination[index * 3 + 1] = g / total;
                destination[index * 3 + 2] = b / total;
            }
        });

        (_reflection, _blurredReflection) = (_blurredReflection, _reflection);
        (_weight, _blurredWeight) = (_blurredWeight, _weight);
    }

    private void Composite(PostProcessTarget target)
    {
        var width = target.Width;
        var color = target.Color;
        var reflectance = target.Reflectance;
        var reflection = _reflection;
        var weight = _weight;
        var grazing = _grazing;
        var strength = MathF.Max(Strength, 0f);

        Parallel.For(0, target.Height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var index = x + y * width;
                var confidence = weight[index];

                if (confidence <= 0f)
                {
                    continue;
                }

                var f0 = SurfaceReflectance.FromPacked(reflectance[index]).Reflectivity;
                var g = grazing[index];
                var scale = confidence * strength;
                var i = index * 3;

                Blend(color, i, reflection[i], Schlick(f0.R, g) * scale);
                Blend(color, i + 1, reflection[i + 1], Schlick(f0.G, g) * scale);
                Blend(color, i + 2, reflection[i + 2], Schlick(f0.B, g) * scale);
            }
        });
    }

    private static float Schlick(float f0, float grazing) => f0 + (1f - f0) * grazing;

    private static void Blend(float[] color, int i, float reflected, float amount) =>
        color[i] = float.Lerp(color[i], reflected, System.Math.Clamp(amount, 0f, 1f));
}
