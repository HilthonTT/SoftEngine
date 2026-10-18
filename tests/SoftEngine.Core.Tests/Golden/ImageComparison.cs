using System.Globalization;

namespace SoftEngine.Core.Tests.Golden;

internal sealed record GoldenTolerance(
    int ChannelTolerance = 2,
    double MaxDifferingFraction = 0.001,
    double MaxMeanError = 0.25)
{
    public static GoldenTolerance Default { get; } = new();

    public static GoldenTolerance Exact { get; } = new(0, 0d, 0d);
}

internal readonly record struct ImageComparison(
    int Width,
    int Height,
    int MaxChannelDelta,
    double MeanChannelError,
    double DifferingFraction,
    int DifferingPixels,
    int FirstDifferenceX,
    int FirstDifferenceY)
{
    public bool IsWithin(GoldenTolerance tolerance) =>
        DifferingFraction <= tolerance.MaxDifferingFraction &&
        MeanChannelError <= tolerance.MaxMeanError;

    public string Describe(GoldenTolerance tolerance)
    {
        var culture = CultureInfo.InvariantCulture;

        var pixels = (long)Width * Height;

        return string.Join(
            Environment.NewLine,
            $"  size                 {Width}x{Height} ({pixels} pixels)",
            $"  differing pixels     {DifferingPixels} ({DifferingFraction.ToString("P4", culture)}), " +
            $"allowed {tolerance.MaxDifferingFraction.ToString("P4", culture)} " +
            $"above a per-channel tolerance of {tolerance.ChannelTolerance}",
            $"  mean channel error   {MeanChannelError.ToString("F4", culture)} of 255, " +
            $"allowed {tolerance.MaxMeanError.ToString("F4", culture)}",
            $"  worst channel delta  {MaxChannelDelta}",
            $"  first difference at  ({FirstDifferenceX}, {FirstDifferenceY})");
    }
}

internal static class ImageDiff
{
    public static ImageComparison Compare(ReadOnlySpan<int> expected, ReadOnlySpan<int> actual, int width, int height, GoldenTolerance tolerance)
    {
        var count = width * height;

        var maxDelta = 0;
        var totalError = 0L;
        var differing = 0;
        var firstX = -1;
        var firstY = -1;

        for (var i = 0; i < count; i++)
        {
            var a = expected[i];
            var b = actual[i];

            if (a == b)
            {
                continue;
            }

            var dr = System.Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF));
            var dg = System.Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF));
            var db = System.Math.Abs((a & 0xFF) - (b & 0xFF));

            totalError += dr + dg + db;

            var worst = System.Math.Max(dr, System.Math.Max(dg, db));

            if (worst > maxDelta)
            {
                maxDelta = worst;
            }

            if (worst > tolerance.ChannelTolerance)
            {
                if (differing == 0)
                {
                    firstX = i % width;
                    firstY = i / width;
                }

                differing++;
            }
        }

        return new ImageComparison(
            width,
            height,
            maxDelta,
            count == 0 ? 0d : totalError / (double)(count * 3),
            count == 0 ? 0d : differing / (double)count,
            differing,
            firstX,
            firstY);
    }

    public static int[] Render(ReadOnlySpan<int> expected, ReadOnlySpan<int> actual, int width, int height, GoldenTolerance tolerance)
    {
        var image = new int[width * height];

        for (var i = 0; i < image.Length; i++)
        {
            var a = expected[i];
            var b = actual[i];

            var dr = System.Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF));
            var dg = System.Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF));
            var db = System.Math.Abs((a & 0xFF) - (b & 0xFF));

            var worst = System.Math.Max(dr, System.Math.Max(dg, db));

            if (worst > tolerance.ChannelTolerance)
            {
                var t = System.Math.Min(worst / 64f, 1f);

                image[i] = unchecked((int)0xFF000000)
                    | (255 << 16)
                    | ((int)((1f - t) * 220f) << 8);

                continue;
            }

            var grey = (int)(((((b >> 16) & 0xFF) * 0.299f) + (((b >> 8) & 0xFF) * 0.587f) + ((b & 0xFF) * 0.114f)) * 0.35f);

            image[i] = unchecked((int)0xFF000000) | (grey << 16) | (grey << 8) | grey;
        }

        return image;
    }
}
