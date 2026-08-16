using SoftEngine.Core.Imaging;
using SoftEngine.Core.Tests.Golden;

namespace SoftEngine.Core.Tests.Golden;

/// <summary>
/// The harness is code, and a golden-image suite that silently compares nothing is worse than
/// no suite at all — it reports success on every run and is believed. These cover the two ways
/// that happens: an encoder that does not round-trip, and a gate that admits everything or
/// nothing without saying so.
/// </summary>
public class GoldenHarnessTests
{
    private static int[] Noise(int width, int height, uint seed)
    {
        var pixels = new int[width * height];
        var state = seed | 1u;

        for (var i = 0; i < pixels.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;

            pixels[i] = unchecked((int)(state | 0xFF000000u));
        }

        return pixels;
    }

    [Fact]
    public void PngCodec_RoundTripsEveryPixelExactly()
    {
        const int width = 61;
        const int height = 37;

        var pixels = Noise(width, height, 0xBEEF);
        var path = Path.Combine(Path.GetTempPath(), $"softengine-golden-{Guid.NewGuid():N}.png");

        try
        {
            PngCodec.Save(path, pixels, width, height);

            var (decoded, decodedWidth, decodedHeight) = PngCodec.Load(path);

            Assert.Equal(width, decodedWidth);
            Assert.Equal(height, decodedHeight);
            Assert.Equal(pixels, decoded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Compare_IdenticalImages_FindsNoDifference()
    {
        var pixels = Noise(32, 32, 0x1234);

        var comparison = ImageDiff.Compare(pixels, pixels, 32, 32, GoldenTolerance.Default);

        Assert.Equal(0, comparison.DifferingPixels);
        Assert.Equal(0, comparison.MaxChannelDelta);
        Assert.Equal(0d, comparison.MeanChannelError);
        Assert.True(comparison.IsWithin(GoldenTolerance.Exact));
    }

    /// <summary>
    /// The bug this exists for was real and was mine: <c>GoldenTolerance</c> began life as a
    /// record <em>struct</em>, whose implicit parameterless constructor skips the primary
    /// constructor and its defaults. <c>Default</c> was initialised with <c>new()</c> and came
    /// out as all zeros — indistinguishable from <see cref="GoldenTolerance.Exact"/>, and
    /// reported as such in every failure message before anyone noticed.
    /// </summary>
    [Fact]
    public void DefaultTolerance_IsActuallyLenient()
    {
        Assert.True(GoldenTolerance.Default.ChannelTolerance > 0);
        Assert.True(GoldenTolerance.Default.MaxDifferingFraction > 0d);
        Assert.True(GoldenTolerance.Default.MaxMeanError > 0d);

        Assert.NotEqual(GoldenTolerance.Exact, GoldenTolerance.Default);
    }

    /// <summary>
    /// Rounding that lands differently on another machine is <em>scattered</em> — a shaded
    /// value near a channel boundary falls one way here and the other way there, on whichever
    /// pixels happen to sit on the boundary. It is not a bias, and modelling it as one (every
    /// channel of every pixel a step brighter) would be modelling something else entirely: a
    /// systematic shift, which is a real change and which the mean is there to fail on.
    /// </summary>
    [Fact]
    public void DefaultTolerance_AbsorbsScatteredRoundingDifferences()
    {
        const int size = 64;

        var expected = Noise(size, size, 0xC0DE);
        var actual = (int[])expected.Clone();

        var state = 0x5EEDu;

        for (var i = 0; i < actual.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;

            // A tenth of the pixels, up or down, by a step or two.
            if (state % 10 != 0)
            {
                continue;
            }

            var delta = (state & 2) == 0 ? 1 : -1;

            var r = System.Math.Clamp(((expected[i] >> 16) & 0xFF) + delta, 0, 255);
            var g = System.Math.Clamp(((expected[i] >> 8) & 0xFF) + delta, 0, 255);
            var b = System.Math.Clamp((expected[i] & 0xFF) + delta, 0, 255);

            actual[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        var comparison = ImageDiff.Compare(expected, actual, size, size, GoldenTolerance.Default);

        Assert.True(comparison.MaxChannelDelta <= GoldenTolerance.Default.ChannelTolerance);
        Assert.Equal(0, comparison.DifferingPixels);
        Assert.True(comparison.IsWithin(GoldenTolerance.Default));
    }

    [Fact]
    public void DefaultTolerance_RejectsASmallPatchThatChangedALot()
    {
        const int size = 64;

        var expected = new int[size * size];
        Array.Fill(expected, unchecked((int)0xFF404040));

        var actual = (int[])expected.Clone();

        // 64 pixels of 4,096 — a tenth of a percent of the frame is well inside the mean, and
        // the point of counting pixels separately is that this still fails.
        for (var i = 0; i < 64; i++)
        {
            actual[i] = unchecked((int)0xFFFFFFFF);
        }

        var comparison = ImageDiff.Compare(expected, actual, size, size, GoldenTolerance.Default);

        Assert.Equal(64, comparison.DifferingPixels);
        Assert.False(comparison.IsWithin(GoldenTolerance.Default));
    }

    [Fact]
    public void DefaultTolerance_RejectsAWholeImageThatShiftedSlightly()
    {
        const int size = 64;

        var expected = new int[size * size];
        Array.Fill(expected, unchecked((int)0xFF404040));

        var actual = new int[size * size];

        // Three channels off by three everywhere: no single pixel is dramatic, and a metric
        // that only asked "how bad is the worst one" would wave it through.
        Array.Fill(actual, unchecked((int)0xFF434343));

        var comparison = ImageDiff.Compare(expected, actual, size, size, GoldenTolerance.Default);

        Assert.Equal(3, comparison.MaxChannelDelta);
        Assert.Equal(3d, comparison.MeanChannelError);
        Assert.False(comparison.IsWithin(GoldenTolerance.Default));
    }
}
