using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// Turns a height map into the tangent-space normal map a material can actually shade with.
///
/// Wavefront's <c>map_Bump</c> was specified as a height map and is used as a normal map by
/// almost everyone, so both turn up in the wild — and a height map fed to a normal-map
/// sampler reads as a nearly-flat surface tinted by its own greyscale. Converting is a
/// gradient: how fast the height changes across the surface is the slope the normal tilts by.
/// </summary>
public static class NormalMapBuilder
{
    /// <summary>
    /// Sobel-differentiates <paramref name="height"/> (its red channel) into a normal map.
    /// <paramref name="strength"/> scales the slope: 1 treats a full black-to-white step
    /// across one texel as a 45° face.
    /// </summary>
    public static Texture FromHeight(Texture height, float strength = 1f)
    {
        ArgumentNullException.ThrowIfNull(height, nameof(height));

        var width = height.Width;
        var rows = height.Height;
        var source = height.Pixels;
        var pixels = new int[width * rows];

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Wrap addressing, matching how the sampler will read the result: a tiling
                // height map has to produce a tiling normal map.
                var left = (x + width - 1) % width;
                var right = (x + 1) % width;
                var up = (y + rows - 1) % rows;
                var down = (y + 1) % rows;

                var dx =
                    Height(source, left, up, width) + 2f * Height(source, left, y, width) + Height(source, left, down, width) -
                    Height(source, right, up, width) - 2f * Height(source, right, y, width) - Height(source, right, down, width);

                // V grows upward in this engine's UV convention, and the image's rows run
                // downward — so the vertical gradient is negated relative to the horizontal.
                var dy =
                    Height(source, left, down, width) + 2f * Height(source, x, down, width) + Height(source, right, down, width) -
                    Height(source, left, up, width) - 2f * Height(source, x, up, width) - Height(source, right, up, width);

                var nx = dx * strength * 0.25f;
                var ny = dy * strength * 0.25f;
                const float nz = 1f;

                var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);

                pixels[x + y * width] = new ColorRGB(
                    Encode(nx / length),
                    Encode(ny / length),
                    Encode(nz / length)).Color;
            }
        }

        return new Texture(width, rows, pixels);
    }

    private static float Height(int[] pixels, int x, int y, int width) =>
        ((pixels[x + y * width] >> 16) & 0xFF) * (1f / 255f);

    /// <summary>Packs a component of a unit vector into a byte as (v + 1) / 2.</summary>
    private static byte Encode(float component) =>
        (byte)System.Math.Clamp((component * 0.5f + 0.5f) * 255f + 0.5f, 0f, 255f);
}
