using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Buffers;

public sealed class Backdrop
{
    private const int MaxLevels = 6;

    private const int SmallestLevel = 8;

    private float[][] _levels = [];
    private int[] _widths = [];
    private int[] _heights = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int LevelCount => _levels.Length;

    public bool IsFilled { get; private set; }

    public void Reset() => IsFilled = false;

    public void Capture(FrameBuffer surface)
    {
        ArgumentNullException.ThrowIfNull(surface, nameof(surface));

        if (surface.Width <= 0 || surface.Height <= 0)
        {
            IsFilled = false;
            return;
        }

        Resize(surface.Width, surface.Height);

        if (surface.IsHighDynamicRange)
        {
            surface.HdrColor.AsSpan(0, Width * Height * 3).CopyTo(_levels[0]);
        }
        else
        {
            Decode(surface.Screen, _levels[0], Width * Height);
        }

        for (var level = 1; level < _levels.Length; level++)
        {
            Downsample(level);
        }

        IsFilled = true;
    }

    public LinearColor Sample(float x, float y, float roughness)
    {
        if (!IsFilled)
        {
            return LinearColor.Black;
        }

        var last = _levels.Length - 1;

        if (last == 0)
        {
            return SampleLevel(0, x, y);
        }

        var blurred = System.Math.Clamp(roughness, 0f, 1f);
        var position = blurred * blurred * last;

        var level = System.Math.Min((int)position, last - 1);
        var fraction = position - level;

        return LinearColor.Lerp(
            SampleLevel(level, x, y),
            SampleLevel(level + 1, x, y),
            System.Math.Clamp(fraction, 0f, 1f));
    }

    private LinearColor SampleLevel(int level, float x, float y)
    {
        var width = _widths[level];
        var height = _heights[level];

        var scale = (float)width / Width;

        var u = (x + 0.5f) * scale - 0.5f;
        var v = (y + 0.5f) * ((float)height / Height) - 0.5f;

        var x0 = (int)MathF.Floor(u);
        var y0 = (int)MathF.Floor(v);

        var fx = u - x0;
        var fy = v - y0;

        var x1 = System.Math.Clamp(x0 + 1, 0, width - 1);
        var y1 = System.Math.Clamp(y0 + 1, 0, height - 1);

        x0 = System.Math.Clamp(x0, 0, width - 1);
        y0 = System.Math.Clamp(y0, 0, height - 1);

        var data = _levels[level];

        var top = LinearColor.Lerp(At(data, width, x0, y0), At(data, width, x1, y0), fx);
        var bottom = LinearColor.Lerp(At(data, width, x0, y1), At(data, width, x1, y1), fx);

        return LinearColor.Lerp(top, bottom, fy);
    }

    private static LinearColor At(float[] data, int width, int x, int y)
    {
        var slot = (x + y * width) * 3;

        return new LinearColor(data[slot], data[slot + 1], data[slot + 2]);
    }

    private void Downsample(int level)
    {
        var sourceWidth = _widths[level - 1];
        var source = _levels[level - 1];

        var width = _widths[level];
        var height = _heights[level];
        var destination = _levels[level];

        for (var y = 0; y < height; y++)
        {
            var sy0 = y * 2;
            var sy1 = System.Math.Min(sy0 + 1, _heights[level - 1] - 1);

            for (var x = 0; x < width; x++)
            {
                var sx0 = x * 2;
                var sx1 = System.Math.Min(sx0 + 1, sourceWidth - 1);

                var a = (sx0 + sy0 * sourceWidth) * 3;
                var b = (sx1 + sy0 * sourceWidth) * 3;
                var c = (sx0 + sy1 * sourceWidth) * 3;
                var d = (sx1 + sy1 * sourceWidth) * 3;

                var slot = (x + y * width) * 3;

                destination[slot] = (source[a] + source[b] + source[c] + source[d]) * 0.25f;
                destination[slot + 1] = (source[a + 1] + source[b + 1] + source[c + 1] + source[d + 1]) * 0.25f;
                destination[slot + 2] = (source[a + 2] + source[b + 2] + source[c + 2] + source[d + 2]) * 0.25f;
            }
        }
    }

    private static void Decode(int[] screen, float[] destination, int count)
    {
        for (var pixel = 0; pixel < count; pixel++)
        {
            var argb = screen[pixel];
            var slot = pixel * 3;

            destination[slot] = ColorSpace.ToLinear((byte)((argb >> 16) & 0xFF));
            destination[slot + 1] = ColorSpace.ToLinear((byte)((argb >> 8) & 0xFF));
            destination[slot + 2] = ColorSpace.ToLinear((byte)(argb & 0xFF));
        }
    }

    private void Resize(int width, int height)
    {
        if (Width == width && Height == height && _levels.Length > 0)
        {
            return;
        }

        Width = width;
        Height = height;

        var count = 1;

        while (count < MaxLevels &&
               (width >> count) >= SmallestLevel &&
               (height >> count) >= SmallestLevel)
        {
            count++;
        }

        _levels = new float[count][];
        _widths = new int[count];
        _heights = new int[count];

        for (var level = 0; level < count; level++)
        {
            var levelWidth = System.Math.Max(1, width >> level);
            var levelHeight = System.Math.Max(1, height >> level);

            _widths[level] = levelWidth;
            _heights[level] = levelHeight;
            _levels[level] = new float[levelWidth * levelHeight * 3];
        }
    }
}
