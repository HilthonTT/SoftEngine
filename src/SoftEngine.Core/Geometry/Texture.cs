using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Geometry;

public enum TextureFiltering
{
    /// <summary>One texel per pixel — fast, blocky up close and shimmery at a distance.</summary>
    Nearest,

    /// <summary>Weighted average of the four surrounding texels.</summary>
    Bilinear,
}

/// <summary>One level of a texture's mip chain: packed ARGB texels and their dimensions.</summary>
public readonly struct TextureMip(int[] pixels, int width, int height)
{
    public readonly int[] Pixels = pixels;
    public readonly int Width = width;
    public readonly int Height = height;
}

/// <summary>
/// A CPU-side texture: packed 32-bit ARGB texels sampled by UV with wrap addressing.
/// Platform-neutral — the front-end (or a factory here) supplies the pixels.
/// </summary>
public sealed class Texture
{
    // Level 0 is the full-resolution image; built on demand by EnsureMipMaps.
    private TextureMip[]? _mips;

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

    /// <summary>Number of available mip levels; 1 until <see cref="EnsureMipMaps"/> has run.</summary>
    public int MipCount => _mips?.Length ?? 1;

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

    /// <summary>
    /// Builds the mip chain by successive 2×2 box halving, down to 1×1. Idempotent, and
    /// meant to be called from a painter's Prepare — before the parallel paint phase.
    /// </summary>
    public void EnsureMipMaps()
    {
        if (_mips is not null)
        {
            return;
        }

        var levels = new List<TextureMip> { new(Pixels, Width, Height) };

        var source = new TextureMip(Pixels, Width, Height);
        while (source.Width > 1 || source.Height > 1)
        {
            source = Halve(source);
            levels.Add(source);
        }

        _mips = [.. levels];
    }

    /// <summary>
    /// The requested mip level, clamped to the chain. Level 0 (the full image) is always
    /// available, whether or not the chain has been built.
    /// </summary>
    public TextureMip GetMip(int level)
    {
        if (_mips is null || level <= 0)
        {
            return new TextureMip(Pixels, Width, Height);
        }

        return _mips[System.Math.Min(level, _mips.Length - 1)];
    }

    private static TextureMip Halve(in TextureMip source)
    {
        var width = System.Math.Max(1, source.Width >> 1);
        var height = System.Math.Max(1, source.Height >> 1);
        var pixels = new int[width * height];

        for (var y = 0; y < height; y++)
        {
            // For odd (or already 1-wide) sources the second sample clamps to the edge,
            // so every destination texel still averages four in-range source texels.
            var y0 = System.Math.Min(y * 2, source.Height - 1);
            var y1 = System.Math.Min(y * 2 + 1, source.Height - 1);

            for (var x = 0; x < width; x++)
            {
                var x0 = System.Math.Min(x * 2, source.Width - 1);
                var x1 = System.Math.Min(x * 2 + 1, source.Width - 1);

                var c00 = source.Pixels[x0 + y0 * source.Width];
                var c10 = source.Pixels[x1 + y0 * source.Width];
                var c01 = source.Pixels[x0 + y1 * source.Width];
                var c11 = source.Pixels[x1 + y1 * source.Width];

                var a = (((c00 >>> 24) & 0xFF) + ((c10 >>> 24) & 0xFF) + ((c01 >>> 24) & 0xFF) + ((c11 >>> 24) & 0xFF) + 2) >> 2;
                var r = (((c00 >> 16) & 0xFF) + ((c10 >> 16) & 0xFF) + ((c01 >> 16) & 0xFF) + ((c11 >> 16) & 0xFF) + 2) >> 2;
                var g = (((c00 >> 8) & 0xFF) + ((c10 >> 8) & 0xFF) + ((c01 >> 8) & 0xFF) + ((c11 >> 8) & 0xFF) + 2) >> 2;
                var b = ((c00 & 0xFF) + (c10 & 0xFF) + (c01 & 0xFF) + (c11 & 0xFF) + 2) >> 2;

                pixels[x + y * width] = a << 24 | r << 16 | g << 8 | b;
            }
        }

        return new TextureMip(pixels, width, height);
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
