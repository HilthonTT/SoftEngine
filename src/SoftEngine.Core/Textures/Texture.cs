using SoftEngine.Core.Diagnostics;

namespace SoftEngine.Core.Textures;

public readonly struct TextureMip(int[] pixels, int width, int height)
{
    public readonly int[] Pixels = pixels;
    public readonly int Width = width;
    public readonly int Height = height;
}

public sealed class Texture
{
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

    public int MipCount => _mips?.Length ?? 1;

    public ColorRGB Sample(float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);

        var x = System.Math.Min((int)(u * Width), Width - 1);
        var y = System.Math.Min((int)((1f - v) * Height), Height - 1);

        return ColorRGB.FromPacked(Pixels[x + y * Width]);
    }

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

    public static Texture Bumps(int size, int cells)
    {
        var pixels = new int[size * size];
        var cellSize = MathF.Max(1f, size / (float)System.Math.Max(1, cells));

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var u = (x % cellSize) / cellSize * 2f - 1f;
                var v = (y % cellSize) / cellSize * 2f - 1f;

                var radiusSquared = u * u + v * v;
                var height = radiusSquared >= 1f ? 0f : MathF.Sqrt(1f - radiusSquared);

                var level = (byte)(height * 255f);
                pixels[x + y * size] = new ColorRGB(level, level, level).Color;
            }
        }

        return new Texture(size, size, pixels);
    }
}
