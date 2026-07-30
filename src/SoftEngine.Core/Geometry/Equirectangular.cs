using SoftEngine.Core.Imaging;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// The latitude–longitude panorama: one wide image covering the whole sphere, and the layout
/// every HDR environment on the internet ships in.
///
/// <para>
/// It is a poor format to *sample* — the rows near the poles cover a hair of solid angle each and
/// hold as many pixels as the equator does, so a naive lookup shimmers at the top of the sky and
/// costs two transcendentals per texel — and a fine one to *store*. So this converts: it is read
/// once, into the <see cref="CubeMap"/> the renderer already samples by direction, at whatever
/// resolution the environment deserves.
/// </para>
///
/// <para>
/// Longitude is measured from −Z, increasing toward +X, so U = 0.5 is dead ahead of an
/// unrotated camera and the image's own left and right edges join up behind it — the seam lands
/// where a panorama's seam is normally authored. Latitude runs from +Y at V = 0 downward, which
/// is both the order Radiance writes its scanlines and the order a cube face's V already runs.
/// </para>
/// </summary>
public static class Equirectangular
{
    /// <summary>
    /// Where a direction lands in the panorama. The direction need not be normalized.
    /// </summary>
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

    /// <summary>The unit direction a point in the panorama looks along — the inverse of <see cref="Project"/>.</summary>
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

    /// <summary>
    /// Projects a linear-light panorama onto a cube map that keeps its range: the floats become
    /// the cube's radiance, and a clipped sRGB encoding of them its byte faces.
    /// </summary>
    /// <param name="panorama">The source image, twice as wide as it is tall by convention.</param>
    /// <param name="resolution">Edge length of each cube face. A quarter of the panorama's width
    /// loses nothing that matters — a cube face spans 90° where the panorama's width spans 360°.</param>
    /// <param name="samplesPerAxis">Panorama samples per face texel per axis. The default's 2×2 is
    /// enough to keep the poles from crawling; 1 turns supersampling off.</param>
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

    /// <summary>
    /// Projects an 8-bit panorama — a JPEG or PNG sky the host has already decoded — onto a cube
    /// map of bytes.
    ///
    /// No radiance faces, because there is no radiance: the source clipped everything above white
    /// before this renderer ever saw it, and attaching floats that all sit inside [0, 1] would
    /// only claim a range that is not there. The reflections will be flat, and that is the
    /// honest answer for the input.
    /// </summary>
    public static CubeMap ToCubeMap(Texture panorama, int resolution = 0, int samplesPerAxis = 2)
    {
        ArgumentNullException.ThrowIfNull(panorama, nameof(panorama));

        resolution = resolution > 0 ? resolution : DefaultResolution(panorama.Width);

        var faces = new Texture[6];

        for (var f = 0; f < 6; f++)
        {
            // Texture.Sample counts V upward from the bottom row, where a panorama's latitude
            // counts down from the top, so this is the one place the two conventions meet.
            var (pixels, _) = ProjectFace((CubeFace)f, resolution, samplesPerAxis,
                (u, v) => panorama.Sample(u, 1f - v), keepRadiance: false);

            faces[f] = new Texture(resolution, resolution, pixels);
        }

        return new CubeMap(faces);
    }

    /// <summary>
    /// A cube face spans a quarter of the panorama's longitude, so a quarter of its width is the
    /// resolution at which neither is resampling the other. Clamped into a range where the
    /// prefilter's cost stays sane and a face still has texels to interpolate across.
    /// </summary>
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

                        // Normalizing matters: Direction returns a vector on the cube, whose
                        // length grows toward the corners, and latitude is an angle off it.
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
