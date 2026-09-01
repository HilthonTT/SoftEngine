using SoftEngine.Core.Imaging;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Textures;

public static class Equirectangular
{
    public static (float U, float V) Project(Vector3 direction)
    {
        var length = direction.Length();

        if (length < 1e-20f)
        {
            return (0.5f, 0.5f);
        }

        var longitude = MathF.Atan2(direction.X, -direction.Z);
        var latitude = MathF.Acos(System.Math.Clamp(direction.Y / length, -1f, 1f));

        var u = 0.5f + longitude / MathF.Tau;

        return (u, latitude / MathF.PI);
    }

    public static Vector3 Direction(float u, float v)
    {
        var latitude = v * MathF.PI;
        var longitude = (u - 0.5f) * MathF.Tau;

        var ring = MathF.Sin(latitude);

        return new Vector3(
            ring * MathF.Sin(longitude),
            MathF.Cos(latitude),
            -ring * MathF.Cos(longitude));
    }

    public static CubeMap ToCubeMap(HdrImage panorama, int resolution = 0, int samplesPerAxis = 2)
    {
        ArgumentNullException.ThrowIfNull(panorama, nameof(panorama));

        resolution = resolution > 0 ? resolution : DefaultResolution(panorama.Width);

        var faces = new Texture[6];
        var radiance = new float[6][];

        for (var f = 0; f < 6; f++)
        {
            var (pixels, floats) = ProjectFace((CubeFace)f, resolution, samplesPerAxis, panorama.Sample);

            faces[f] = new Texture(resolution, resolution, pixels);
            radiance[f] = floats!;
        }

        return new CubeMap(faces, radiance);
    }

    public static CubeMap ToCubeMap(Texture panorama, int resolution = 0, int samplesPerAxis = 2)
    {
        ArgumentNullException.ThrowIfNull(panorama, nameof(panorama));

        resolution = resolution > 0 ? resolution : DefaultResolution(panorama.Width);

        var faces = new Texture[6];

        for (var f = 0; f < 6; f++)
        {
            var (pixels, _) = ProjectFace((CubeFace)f, resolution, samplesPerAxis,
                (u, v) => panorama.Sample(u, 1f - v), keepRadiance: false);

            faces[f] = new Texture(resolution, resolution, pixels);
        }

        return new CubeMap(faces);
    }

    private static int DefaultResolution(int panoramaWidth) =>
        System.Math.Clamp(1 << (int)MathF.Round(MathF.Log2(MathF.Max(4f, panoramaWidth / 4f))), 16, 512);

    private static (int[] Pixels, float[]? Radiance) ProjectFace(
        CubeFace face,
        int resolution,
        int samplesPerAxis,
        Func<float, float, LinearColor> sample,
        bool keepRadiance = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution, nameof(resolution));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplesPerAxis, nameof(samplesPerAxis));

        var pixels = new int[resolution * resolution];
        var radiance = keepRadiance ? new float[resolution * resolution * 3] : null;

        var perTexel = samplesPerAxis * samplesPerAxis;
        var step = 1f / (resolution * samplesPerAxis);

        Parallel.For(0, resolution, y =>
        {
            for (var x = 0; x < resolution; x++)
            {
                float r = 0f, g = 0f, b = 0f;

                for (var j = 0; j < samplesPerAxis; j++)
                {
                    var v = (y * samplesPerAxis + j + 0.5f) * step;

                    for (var i = 0; i < samplesPerAxis; i++)
                    {
                        var u = (x * samplesPerAxis + i + 0.5f) * step;

                        var direction = Vector3.Normalize(CubeMap.Direction(face, u, v));
                        var (su, sv) = Project(direction);

                        var light = sample(su, sv);

                        r += light.R;
                        g += light.G;
                        b += light.B;
                    }
                }

                var scale = 1f / perTexel;
                var texel = x + y * resolution;

                var averaged = new LinearColor(r * scale, g * scale, b * scale);

                if (radiance is not null)
                {
                    radiance[texel * 3] = averaged.R;
                    radiance[texel * 3 + 1] = averaged.G;
                    radiance[texel * 3 + 2] = averaged.B;
                }

                pixels[texel] = averaged.ToColorRGB().Color;
            }
        });

        return (pixels, radiance);
    }
}
