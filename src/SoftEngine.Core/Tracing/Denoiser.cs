using System.Numerics;

namespace SoftEngine.Core.Tracing;

public sealed class Denoiser
{
    private static readonly float[] _kernel = BuildKernel();

    private float[] _irradiance = [];
    private float[] _scratch = [];

    public int Iterations { get; set; } = 4;

    public float ColorSigma { get; set; } = 4f;

    public float NormalSigma { get; set; } = 64f;

    public float DepthSigma { get; set; } = 0.4f;

    public void Apply(
        float[] color,
        float[] albedo,
        float[] normal,
        float[] distance,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(color, nameof(color));
        ArgumentNullException.ThrowIfNull(albedo, nameof(albedo));
        ArgumentNullException.ThrowIfNull(normal, nameof(normal));
        ArgumentNullException.ThrowIfNull(distance, nameof(distance));

        var passes = System.Math.Clamp(Iterations, 0, 8);

        if (passes == 0 || width < 5 || height < 5)
        {
            return;
        }

        var channels = width * height * 3;

        if (_irradiance.Length < channels)
        {
            _irradiance = new float[channels];
            _scratch = new float[channels];
        }

        Demodulate(color, albedo, _irradiance, width * height);

        var source = _irradiance;
        var destination = _scratch;

        for (var pass = 0; pass < passes; pass++)
        {
            Filter(source, destination, normal, distance, width, height, 1 << pass);

            (source, destination) = (destination, source);
        }

        Modulate(source, albedo, color, distance, width * height);

        _irradiance = source;
        _scratch = destination;
    }

    private static void Demodulate(float[] color, float[] albedo, float[] irradiance, int pixels)
    {
        Parallel.For(0, pixels, pixel =>
        {
            var slot = pixel * 3;

            irradiance[slot] = color[slot] / MathF.Max(albedo[slot], 1e-3f);
            irradiance[slot + 1] = color[slot + 1] / MathF.Max(albedo[slot + 1], 1e-3f);
            irradiance[slot + 2] = color[slot + 2] / MathF.Max(albedo[slot + 2], 1e-3f);
        });
    }

    private static void Modulate(float[] irradiance, float[] albedo, float[] color, float[] distance, int pixels)
    {
        Parallel.For(0, pixels, pixel =>
        {
            if (float.IsPositiveInfinity(distance[pixel]))
            {
                return;
            }

            var slot = pixel * 3;

            color[slot] = irradiance[slot] * MathF.Max(albedo[slot], 1e-3f);
            color[slot + 1] = irradiance[slot + 1] * MathF.Max(albedo[slot + 1], 1e-3f);
            color[slot + 2] = irradiance[slot + 2] * MathF.Max(albedo[slot + 2], 1e-3f);
        });
    }

    private void Filter(
        float[] source,
        float[] destination,
        float[] normal,
        float[] distance,
        int width,
        int height,
        int step)
    {
        var colorSigma = MathF.Max(ColorSigma, 1e-3f);
        var normalSigma = MathF.Max(NormalSigma, 1e-3f);
        var depthSigma = MathF.Max(DepthSigma, 1e-3f) * step;

        var kernel = _kernel;

        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var center = x + y * width;
                var slot = center * 3;

                if (float.IsPositiveInfinity(distance[center]))
                {
                    destination[slot] = source[slot];
                    destination[slot + 1] = source[slot + 1];
                    destination[slot + 2] = source[slot + 2];
                    continue;
                }

                var centerNormal = NormalAt(normal, center);
                var centerDistance = distance[center];
                var centerLuminance = Luminance(source, slot);

                var sumR = 0f;
                var sumG = 0f;
                var sumB = 0f;
                var sumWeight = 0f;

                for (var ky = -2; ky <= 2; ky++)
                {
                    var sy = y + ky * step;

                    if ((uint)sy >= (uint)height)
                    {
                        continue;
                    }

                    for (var kx = -2; kx <= 2; kx++)
                    {
                        var sx = x + kx * step;

                        if ((uint)sx >= (uint)width)
                        {
                            continue;
                        }

                        var sample = sx + sy * width;

                        if (float.IsPositiveInfinity(distance[sample]))
                        {
                            continue;
                        }

                        var sampleSlot = sample * 3;

                        var alignment = MathF.Max(Vector3.Dot(centerNormal, NormalAt(normal, sample)), 0f);
                        var normalWeight = MathF.Pow(alignment, normalSigma);

                        if (normalWeight <= 1e-6f)
                        {
                            continue;
                        }

                        var depthGap = MathF.Abs(centerDistance - distance[sample]);
                        var depthWeight = MathF.Exp(-depthGap / (depthSigma * MathF.Max(centerDistance, 1e-3f)));

                        var luminanceGap = MathF.Abs(centerLuminance - Luminance(source, sampleSlot));
                        var colorWeight = MathF.Exp(-luminanceGap / colorSigma);

                        var weight = kernel[(ky + 2) + (kx + 2) * 5] * normalWeight * depthWeight * colorWeight;

                        sumR += source[sampleSlot] * weight;
                        sumG += source[sampleSlot + 1] * weight;
                        sumB += source[sampleSlot + 2] * weight;
                        sumWeight += weight;
                    }
                }

                if (sumWeight <= 1e-8f)
                {
                    destination[slot] = source[slot];
                    destination[slot + 1] = source[slot + 1];
                    destination[slot + 2] = source[slot + 2];
                    continue;
                }

                var inverse = 1f / sumWeight;

                destination[slot] = sumR * inverse;
                destination[slot + 1] = sumG * inverse;
                destination[slot + 2] = sumB * inverse;
            }
        });
    }

    private static Vector3 NormalAt(float[] normal, int pixel)
    {
        var slot = pixel * 3;

        return new Vector3(normal[slot], normal[slot + 1], normal[slot + 2]);
    }

    private static float Luminance(float[] color, int slot) =>
        0.2126f * color[slot] + 0.7152f * color[slot + 1] + 0.0722f * color[slot + 2];

    private static float[] BuildKernel()
    {
        float[] taps = [1f / 16f, 1f / 4f, 3f / 8f, 1f / 4f, 1f / 16f];

        var kernel = new float[25];

        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                kernel[y + x * 5] = taps[y] * taps[x];
            }
        }

        return kernel;
    }
}
