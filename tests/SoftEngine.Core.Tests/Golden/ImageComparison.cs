using System.Globalization;

namespace SoftEngine.Core.Tests.Golden;

/// <summary>
/// How far a rendered frame is allowed to drift from its baseline before the test fails.
///
/// <para>
/// Exact equality would be the obvious rule, and it is the wrong one. The same scene rendered
/// twice on the same machine really is identical — the tiled fill gives each worker a disjoint
/// rectangle of pixels, so no result depends on the order the workers finish in — but the
/// floating-point work leading up to it does not have to agree bit for bit across machines.
/// Whether the JIT contracts a multiply and an add into one FMA, and how wide
/// <c>Vector&lt;float&gt;</c> is, are both properties of the host, and either can move a
/// shaded value by an ulp. An ulp lands on a channel boundary often enough that a
/// zero-tolerance baseline would fail somewhere other than where it was recorded.
/// </para>
///
/// <para>
/// So the gate is three numbers rather than one, because the failures worth catching have
/// different shapes. A shading term that changes by a few percent moves nearly every lit pixel
/// a little, which <see cref="MaxMeanError"/> sees and a per-pixel count would let through. A
/// geometry or culling bug moves a few pixels a great deal, which
/// <see cref="MaxDifferingFraction"/> sees and a mean would average away.
/// </para>
/// </summary>
/// <param name="ChannelTolerance">Per-channel difference a pixel may show without counting as differing at all.</param>
/// <param name="MaxDifferingFraction">Fraction of pixels allowed to exceed <paramref name="ChannelTolerance"/>.</param>
/// <param name="MaxMeanError">Mean absolute channel error, in 0-255 units, over the whole image.</param>
/// <remarks>
/// A record <em>class</em> rather than a struct, and the difference is not stylistic. The
/// defaults below live on the primary constructor, and a struct's implicit parameterless
/// constructor does not run it — so <c>new GoldenTolerance()</c> on a struct would zero every
/// field and hand back a gate that permits nothing, quietly, while reading like the lenient
/// one. Nothing here is on a hot path, so the type may as well be one that cannot be
/// constructed into a lie.
/// </remarks>
internal sealed record GoldenTolerance(
    int ChannelTolerance = 2,
    double MaxDifferingFraction = 0.001,
    double MaxMeanError = 0.25)
{
    /// <summary>The default gate: an ulp of drift passes, a percent of shading does not.</summary>
    public static GoldenTolerance Default { get; } = new();

    /// <summary>
    /// A gate that permits nothing at all. Used where the claim under test is that two renders
    /// are the *same* render — an optimization that must not change the image — rather than
    /// that a render still matches a recording of it. Both frames come from one process on one
    /// machine there, so the drift this type otherwise exists to absorb cannot arise.
    /// </summary>
    public static GoldenTolerance Exact { get; } = new(0, 0d, 0d);
}

/// <summary>What comparing two images found, whether or not it was within tolerance.</summary>
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

/// <summary>Compares two packed-ARGB images, and renders what it found as a third one.</summary>
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

            // Alpha is deliberately left out. The render target is presented, never
            // composited, and every path that resolves it forces alpha opaque — so a
            // difference there would be a difference in nothing anyone can see.
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

    /// <summary>
    /// A picture of the disagreement: the rendered frame dimmed to a grey backdrop, with every
    /// pixel outside tolerance painted in a colour ramped by how far out it is — yellow for a
    /// hair's breadth, red for a channel that changed completely.
    ///
    /// The backdrop is what makes it readable. A diff of the differences alone is a scatter of
    /// bright dots with nothing to locate them against, and the first thing anyone looking at a
    /// failed image test wants to know is *where on the model* it went wrong.
    /// </summary>
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
                // Ramp yellow to red over the first quarter of the range, so a one-channel
                // shift is still clearly visible rather than a nearly-black dot.
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
