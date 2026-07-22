using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Geometry;

/// <summary>
/// A CPU-side texture: packed 32-bit ARGB texels sampled by UV with wrap addressing.
/// Platform-neutral — the front-end (or a factory here) supplies the pixels.
/// </summary>
public sealed class Texture
{
    public Texture(int width, int height, int[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != width * height)
        {
            throw new ArgumentException($"Expected {width * height} pixels, got {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int[] Pixels { get; }

    /// <summary>
    /// Nearest-neighbour sample with wrap addressing. V grows upward (V = 0 is the
    /// bottom row), matching the usual UV convention.
    /// </summary>
    public ColorRGB Sample(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        var x = System.Math.Min((int)(u * Width), Width - 1);
        var y = System.Math.Min((int)((1f - v) * Height), Height - 1);

        return ColorRGB.FromPacked(Pixels[x + y * Width]);
    }

    /// <summary>A procedural checkerboard, handy as a default and for eyeballing UV mapping.</summary>
    public static Texture Checkerboard(int size, int cells, ColorRGB even, ColorRGB odd)
    {
        var pixels = new int[size * size];
        var cellSize = System.Math.Max(1, size / cells);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var isEven = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                pixels[x + y * size] = (isEven ? even : odd).Color;
            }
        }

        return new Texture(size, size, pixels);
    }
}
