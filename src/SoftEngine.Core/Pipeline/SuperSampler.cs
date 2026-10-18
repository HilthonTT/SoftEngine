using SoftEngine.Core.Buffers;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Pipeline;

public static class SuperSampler
{
    public const int MinFactor = 1;

    public const int MaxFactor = 4;

    public static int ClampFactor(int factor) => System.Math.Clamp(factor, MinFactor, MaxFactor);

    public static void Resolve(FrameBuffer source, int[] target, int targetWidth, int targetHeight, int factor)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (target.Length < targetWidth * targetHeight)
        {
            throw new ArgumentException(
                $"Expected room for {targetWidth * targetHeight} pixels, got {target.Length}.", nameof(target));
        }

        if (factor <= 1)
        {
            Array.Copy(source.Screen, target, targetWidth * targetHeight);
            return;
        }

        if (source.Width < targetWidth * factor || source.Height < targetHeight * factor)
        {
            throw new ArgumentException(
                $"A {factor}× resolve of {targetWidth}×{targetHeight} needs a {targetWidth * factor}×{targetHeight * factor} " +
                $"source, got {source.Width}×{source.Height}.", nameof(source));
        }

        var screen = source.Screen;
        var sourceWidth = source.Width;
        var samples = factor * factor;
        var inverse = 1f / samples;

        Parallel.For(0, targetHeight, y =>
        {
            var targetRow = y * targetWidth;
            var sourceRow = y * factor * sourceWidth;

            for (var x = 0; x < targetWidth; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;

                for (var sy = 0; sy < factor; sy++)
                {
                    var row = sourceRow + sy * sourceWidth + x * factor;

                    for (var sx = 0; sx < factor; sx++)
                    {
                        var argb = unchecked((uint)screen[row + sx]);

                        a += (argb >> 24) & 0xFF;
                        r += ColorSpace.ToLinear((byte)((argb >> 16) & 0xFF));
                        g += ColorSpace.ToLinear((byte)((argb >> 8) & 0xFF));
                        b += ColorSpace.ToLinear((byte)(argb & 0xFF));
                    }
                }

                target[targetRow + x] = unchecked((int)(
                    ((uint)(a * inverse + 0.5f) << 24) |
                    ((uint)ColorSpace.ToSrgb(r * inverse) << 16) |
                    ((uint)ColorSpace.ToSrgb(g * inverse) << 8) |
                    ColorSpace.ToSrgb(b * inverse)));
            }
        });
    }
}
