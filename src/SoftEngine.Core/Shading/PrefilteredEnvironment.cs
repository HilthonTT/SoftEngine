using SoftEngine.Core.Geometry;
using SoftEngine.Core.Textures;
using System.Numerics;

namespace SoftEngine.Core.Shading;

public sealed class PrefilteredEnvironment
{
    private const int SampleCount = 128;

    private readonly CubeMap _source;
    private readonly Level[] _levels;

    private PrefilteredEnvironment(CubeMap source, Level[] levels, int baseResolution, float intensity)
    {
        _source = source;
        _levels = levels;

        BaseResolution = baseResolution;
        Intensity = intensity;
    }

    public CubeMap Source => _source;

    public int LevelCount => _levels.Length + 1;

    public int BaseResolution { get; }

    public float Intensity { get; }

    public static PrefilteredEnvironment Build(
        CubeMap environment,
        int baseResolution = 64,
        int levelCount = 5,
        float intensity = 1f)
    {
        ArgumentNullException.ThrowIfNull(environment, nameof(environment));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseResolution);
        ArgumentOutOfRangeException.ThrowIfLessThan(levelCount, 2);

        var levels = new Level[levelCount - 1];

        for (var i = 0; i < levels.Length; i++)
        {
            var roughness = (i + 1) / (float)(levelCount - 1);

            var resolution = System.Math.Max(4, baseResolution >> i);

            levels[i] = Convolve(environment, resolution, roughness, intensity);
        }

        return new PrefilteredEnvironment(environment, levels, baseResolution, intensity);
    }

    public LinearColor Sample(Vector3 direction, float roughness)
    {
        var position = System.Math.Clamp(roughness, 0f, 1f) * _levels.Length;

        var index = (int)position;
        var blend = position - index;

        if (index >= _levels.Length)
        {
            return _levels[^1].Sample(direction);
        }

        var lower = index == 0
            ? Intensity * _source.SampleRadiance(direction)
            : _levels[index - 1].Sample(direction);

        if (blend <= 0f)
        {
            return lower;
        }

        return LinearColor.Lerp(lower, _levels[index].Sample(direction), blend);
    }

    private static Level Convolve(CubeMap environment, int resolution, float roughness, float intensity)
    {
        var alpha = Ggx.Alpha(roughness);
        var faces = new float[6][];

        var halfVectors = new Vector3[SampleCount];
        for (var i = 0; i < SampleCount; i++)
        {
            halfVectors[i] = Ggx.ImportanceSampleHalfVector(Ggx.Hammersley(i, SampleCount), alpha);
        }

        for (var f = 0; f < 6; f++)
        {
            var face = new float[resolution * resolution * 3];
            var cubeFace = (CubeFace)f;

            Parallel.For(0, resolution, y =>
            {
                var v = (y + 0.5f) / resolution;

                for (var x = 0; x < resolution; x++)
                {
                    var u = (x + 0.5f) / resolution;
                    var normal = Vector3.Normalize(CubeMap.Direction(cubeFace, u, v));

                    var (tangent, bitangent) = Ggx.BasisAround(normal);

                    float r = 0f, g = 0f, b = 0f, weight = 0f;

                    foreach (var tangentHalf in halfVectors)
                    {
                        var half = tangent * tangentHalf.X + bitangent * tangentHalf.Y + normal * tangentHalf.Z;

                        var light = 2f * Vector3.Dot(normal, half) * half - normal;

                        var nDotL = Vector3.Dot(normal, light);
                        if (nDotL <= 0f)
                        {
                            continue;
                        }

                        var texel = environment.SampleRadiance(light);

                        r += texel.R * nDotL;
                        g += texel.G * nDotL;
                        b += texel.B * nDotL;
                        weight += nDotL;
                    }

                    var scale = weight > 0f ? intensity / weight : 0f;

                    var index = (x + y * resolution) * 3;

                    face[index] = r * scale;
                    face[index + 1] = g * scale;
                    face[index + 2] = b * scale;
                }
            });

            faces[f] = face;
        }

        return new Level(faces, resolution);
    }

    private readonly struct Level(float[][] faces, int resolution)
    {
        private readonly float[][] _faces = faces;
        private readonly int _resolution = resolution;

        public LinearColor Sample(Vector3 direction)
        {
            var (face, u, v) = CubeMap.Project(direction);

            var pixels = _faces[(int)face];
            var size = _resolution;

            var fx = u * size - 0.5f;
            var fy = v * size - 0.5f;

            var x0 = (int)MathF.Floor(fx);
            var y0 = (int)MathF.Floor(fy);

            var tx = fx - x0;
            var ty = fy - y0;

            var xa = System.Math.Clamp(x0, 0, size - 1);
            var xb = System.Math.Clamp(x0 + 1, 0, size - 1);
            var ya = System.Math.Clamp(y0, 0, size - 1) * size;
            var yb = System.Math.Clamp(y0 + 1, 0, size - 1) * size;

            var top = LinearColor.Lerp(At(pixels, xa + ya), At(pixels, xb + ya), tx);
            var bottom = LinearColor.Lerp(At(pixels, xa + yb), At(pixels, xb + yb), tx);

            return LinearColor.Lerp(top, bottom, ty);
        }

        private static LinearColor At(float[] pixels, int texel)
        {
            var i = texel * 3;
            return new LinearColor(pixels[i], pixels[i + 1], pixels[i + 2]);
        }
    }
}
