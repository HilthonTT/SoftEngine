using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Shading;

/// <summary>
/// The light half of the split-sum approximation: an environment convolved with the GGX
/// lobe, once per roughness, so a surface can look up what it reflects with a single sample
/// instead of integrating the hemisphere per pixel.
///
/// A mirror reflects exactly what lies along the reflection direction. A rough surface
/// reflects a cone around it, widening as the roughness climbs, and by the time it is fully
/// rough it reflects nearly everything above it equally. Those are all the same environment
/// blurred by different amounts — so blurring it ahead of time, at a handful of roughnesses,
/// turns the integral into an interpolation between two of them.
///
/// Level 0 is the source environment itself, sampled directly: a mirror wants the sharpest
/// image available, not a downsampled copy of it. The rest are prefiltered at successively
/// lower resolutions, which costs almost nothing and loses nothing — a lobe wide enough to
/// need level 4 has already thrown away every detail a large face could have held.
///
/// Levels are kept as linear floats rather than packed sRGB bytes. Convolution is an average
/// of light, and re-encoding it at every level would quantize each blur into the next.
/// </summary>
public sealed class PrefilteredEnvironment
{
    /// <summary>Samples drawn per texel. Low-discrepancy, so this buys more than random sampling would.</summary>
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

    /// <summary>The environment this was built from; also what <see cref="Sample"/> reads at roughness 0.</summary>
    public CubeMap Source => _source;

    /// <summary>Number of roughness steps, counting the mirror level. <c>LevelCount - 1</c> is fully rough.</summary>
    public int LevelCount => _levels.Length + 1;

    /// <summary>Edge length of the first prefiltered level, in texels.</summary>
    public int BaseResolution { get; }

    /// <summary>The scale the levels were built with — see <see cref="Build"/>.</summary>
    public float Intensity { get; }

    /// <summary>
    /// Convolves <paramref name="environment"/> at <paramref name="levelCount"/> - 1
    /// roughnesses, evenly spaced over (0, 1].
    ///
    /// <paramref name="intensity"/> is baked in rather than applied per sample, because it
    /// is fixed for a scene and a per-pixel multiply is not. It is the same knob
    /// <see cref="Scenes.Scene.AmbientIntensity"/> turns for the diffuse half: what a sky
    /// looks like and how much light a surface facing it receives are different numbers.
    ///
    /// Building walks every texel of every level and takes <see cref="SampleCount"/> samples
    /// of the environment at each, so it is done once per environment and cached — see
    /// <see cref="Rasterization.Painters.PbrPainter"/>.
    /// </summary>
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
            // Level i + 1 of levelCount, so the last one lands exactly on roughness 1.
            var roughness = (i + 1) / (float)(levelCount - 1);

            // Halving per level, but never below 4×4: a cube face smaller than that has too
            // few texels to interpolate across without the seams showing.
            var resolution = System.Math.Max(4, baseResolution >> i);

            levels[i] = Convolve(environment, resolution, roughness, intensity);
        }

        return new PrefilteredEnvironment(environment, levels, baseResolution, intensity);
    }

    /// <summary>
    /// The light reflected from <paramref name="direction"/> by a surface of the given
    /// roughness, interpolated between the two levels it falls between.
    ///
    /// Interpolating rather than snapping matters more here than it does for texture mips: a
    /// model whose roughness varies across it would otherwise show a visible band wherever it
    /// crosses a level boundary, in the middle of a smooth surface.
    /// </summary>
    public LinearColor Sample(Vector3 direction, float roughness)
    {
        var position = System.Math.Clamp(roughness, 0f, 1f) * _levels.Length;

        var index = (int)position;
        var blend = position - index;

        if (index >= _levels.Length)
        {
            return _levels[^1].Sample(direction);
        }

        // Level 0 is the source environment, which is packed sRGB and has to be decoded.
        var lower = index == 0
            ? Intensity * (LinearColor)_source.Sample(direction)
            : _levels[index - 1].Sample(direction);

        if (blend <= 0f)
        {
            return lower;
        }

        return LinearColor.Lerp(lower, _levels[index].Sample(direction), blend);
    }

    /// <summary>
    /// Convolves the environment with the GGX lobe of one roughness.
    ///
    /// The lobe is defined around a view direction, and there is no view direction at
    /// prefilter time — so this makes the usual assumption that the surface is being looked
    /// at head-on, <c>n = v = r</c>. It is wrong at grazing angles, where a real lobe
    /// stretches into a streak rather than staying round, and it is the reason prefiltered
    /// reflections lose the elongated highlight you see along the edge of a rough surface.
    /// Everything real-time makes the same trade.
    /// </summary>
    private static Level Convolve(CubeMap environment, int resolution, float roughness, float intensity)
    {
        var alpha = Ggx.Alpha(roughness);
        var faces = new float[6][];

        // Sample directions are the same for every texel up to the frame they are rotated
        // into, so the tangent-space half-vectors are drawn once for the whole level.
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

                        // Reflect the view (= the normal, by the assumption above) about the
                        // sampled microfacet to get the direction it would bring light from.
                        var light = 2f * Vector3.Dot(normal, half) * half - normal;

                        var nDotL = Vector3.Dot(normal, light);
                        if (nDotL <= 0f)
                        {
                            continue;
                        }

                        // Weighting by n·l rather than taking a flat average is a well-worn
                        // fudge: it pulls the result toward the lobe's centre and visibly
                        // reduces the noise a finite sample count leaves behind.
                        LinearColor texel = environment.Sample(light);

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

    /// <summary>
    /// One convolved cube: six faces of linear RGB floats, addressed the way
    /// <see cref="CubeMap"/> addresses its own — by direction, clamping at the face edges
    /// rather than wrapping, since a face's neighbour along u is the next face round.
    /// </summary>
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
