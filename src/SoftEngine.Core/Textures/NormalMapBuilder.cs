using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Textures;

public static class NormalMapBuilder
{
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
                var left = (x + width - 1) % width;
                var right = (x + 1) % width;
                var up = (y + rows - 1) % rows;
                var down = (y + 1) % rows;

                var dx =
                    Height(source, left, up, width) + 2f * Height(source, left, y, width) + Height(source, left, down, width) -
                    Height(source, right, up, width) - 2f * Height(source, right, y, width) - Height(source, right, down, width);

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

    private static byte Encode(float component) =>
        (byte)System.Math.Clamp((component * 0.5f + 0.5f) * 255f + 0.5f, 0f, 255f);
}
