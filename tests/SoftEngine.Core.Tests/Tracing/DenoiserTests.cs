using SoftEngine.Core.Tracing;

namespace SoftEngine.Core.Tests.Tracing;

public class DenoiserTests
{
    private const int Size = 32;

    private sealed class Noise(uint seed)
    {
        private uint _state = seed | 1u;

        public float Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return (_state >> 8) * (1f / 16777216f);
        }
    }

    private static (float[] Color, float[] Albedo, float[] Normal, float[] Distance) Flat(float level, float amplitude)
    {
        var pixels = Size * Size;

        var color = new float[pixels * 3];
        var albedo = new float[pixels * 3];
        var normal = new float[pixels * 3];
        var distance = new float[pixels];

        var noise = new Noise(0x1234);

        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var slot = pixel * 3;

            for (var channel = 0; channel < 3; channel++)
            {
                albedo[slot + channel] = 1f;
                color[slot + channel] = level + (noise.Next() - 0.5f) * amplitude;
            }

            normal[slot + 2] = 1f;
            distance[pixel] = 4f;
        }

        return (color, albedo, normal, distance);
    }

    private static float Variance(float[] color, int pixels)
    {
        var mean = 0f;

        for (var pixel = 0; pixel < pixels; pixel++)
        {
            mean += color[pixel * 3];
        }

        mean /= pixels;

        var sum = 0f;

        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var difference = color[pixel * 3] - mean;
            sum += difference * difference;
        }

        return sum / pixels;
    }

    [Fact]
    public void Apply_OnAFlatSurface_RemovesMostOfTheNoise()
    {
        var (color, albedo, normal, distance) = Flat(0.5f, 0.4f);

        var before = Variance(color, Size * Size);

        new Denoiser().Apply(color, albedo, normal, distance, Size, Size);

        var after = Variance(color, Size * Size);

        Assert.True(after < before * 0.1f, $"variance {before} -> {after}");
    }

    [Fact]
    public void Apply_OnAFlatSurface_KeepsTheAverageBrightness()
    {
        var (color, albedo, normal, distance) = Flat(0.5f, 0.4f);

        var pixels = Size * Size;

        var before = 0f;
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            before += color[pixel * 3];
        }

        new Denoiser().Apply(color, albedo, normal, distance, Size, Size);

        var after = 0f;
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            after += color[pixel * 3];
        }

        Assert.Equal(before / pixels, after / pixels, 2);
    }

    [Fact]
    public void Apply_KeepsTheTextureTheAlbedoCarries()
    {
        var pixels = Size * Size;

        var color = new float[pixels * 3];
        var albedo = new float[pixels * 3];
        var normal = new float[pixels * 3];
        var distance = new float[pixels];

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var slot = (x + y * Size) * 3;
                var checker = ((x + y) & 1) == 0 ? 0.9f : 0.1f;

                for (var channel = 0; channel < 3; channel++)
                {
                    albedo[slot + channel] = checker;
                    color[slot + channel] = checker * 0.5f;
                }

                normal[slot + 2] = 1f;
                distance[x + y * Size] = 4f;
            }
        }

        new Denoiser().Apply(color, albedo, normal, distance, Size, Size);

        var bright = color[((1 + 1 * Size)) * 3];
        var dark = color[((2 + 1 * Size)) * 3];

        Assert.True(bright > dark * 3f, $"checker flattened: {bright} vs {dark}");
    }

    [Fact]
    public void Apply_DoesNotTouchPixelsNothingWasHitAt()
    {
        var (color, albedo, normal, distance) = Flat(0.5f, 0.4f);

        for (var pixel = 0; pixel < Size * Size; pixel++)
        {
            distance[pixel] = float.PositiveInfinity;
        }

        var expected = (float[])color.Clone();

        new Denoiser().Apply(color, albedo, normal, distance, Size, Size);

        Assert.Equal(expected, color);
    }

    [Fact]
    public void Apply_AcrossAFoldInTheNormals_DoesNotBleedAcross()
    {
        var pixels = Size * Size;

        var color = new float[pixels * 3];
        var albedo = new float[pixels * 3];
        var normal = new float[pixels * 3];
        var distance = new float[pixels];

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var pixel = x + y * Size;
                var slot = pixel * 3;

                var left = x < Size / 2;

                for (var channel = 0; channel < 3; channel++)
                {
                    albedo[slot + channel] = 1f;
                    color[slot + channel] = left ? 1f : 0f;
                }

                normal[slot] = left ? 1f : -1f;
                distance[pixel] = 4f;
            }
        }

        new Denoiser().Apply(color, albedo, normal, distance, Size, Size);

        var lastLeft = color[(Size / 2 - 1 + (Size / 2) * Size) * 3];
        var firstRight = color[(Size / 2 + (Size / 2) * Size) * 3];

        Assert.True(lastLeft > 0.95f, $"left side pulled down to {lastLeft}");
        Assert.True(firstRight < 0.05f, $"right side pulled up to {firstRight}");
    }

    [Fact]
    public void Apply_WithNoIterations_LeavesTheImageAlone()
    {
        var (color, albedo, normal, distance) = Flat(0.5f, 0.4f);

        var expected = (float[])color.Clone();

        new Denoiser { Iterations = 0 }.Apply(color, albedo, normal, distance, Size, Size);

        Assert.Equal(expected, color);
    }
}
