using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.PostProcess;

namespace SoftEngine.Core.Tests;

public class PostProcessTests
{
    /// <summary>An enabled effect that changes nothing — isolates the stack's own decode/encode.</summary>
    private sealed class NoOpEffect : IPostEffect
    {
        public string Name => "No-op";

        public bool Enabled { get; set; } = true;

        public void Apply(PostProcessTarget target)
        {
        }
    }

    private static FrameBuffer Filled(int width, int height, ColorRGB color)
    {
        var surface = new FrameBuffer(width, height);
        Array.Fill(surface.Screen, color.Color);
        return surface;
    }

    private static (int R, int G, int B) At(FrameBuffer surface, int x, int y)
    {
        var color = ColorRGB.FromPacked(surface.GetColor(x, y));
        return (color.R, color.G, color.B);
    }

    [Fact]
    public void Apply_EmptyStack_LeavesTheImageAlone()
    {
        var surface = Filled(16, 16, new ColorRGB(10, 120, 250));
        var expected = (int[])surface.Screen.Clone();

        new PostProcessStack().Apply(surface);

        Assert.Equal(expected, surface.Screen);
    }

    [Fact]
    public void Apply_DisabledEffects_AreSkipped()
    {
        var surface = Filled(16, 16, new ColorRGB(10, 120, 250));
        var expected = (int[])surface.Screen.Clone();

        var stack = PostProcessStack.CreateDefault();

        Assert.False(stack.HasEffects);

        stack.Apply(surface);

        Assert.Equal(expected, surface.Screen);
    }

    [Fact]
    public void Apply_RoundTripThroughLinearSpace_PreservesTheImage()
    {
        var surface = new FrameBuffer(4, 4);
        for (var i = 0; i < surface.Screen.Length; i++)
        {
            surface.Screen[i] = new ColorRGB((byte)(i * 16), (byte)(255 - i * 16), 128).Color;
        }

        var expected = (int[])surface.Screen.Clone();

        var stack = new PostProcessStack();
        stack.Effects.Add(new NoOpEffect());
        stack.Apply(surface);

        for (var i = 0; i < expected.Length; i++)
        {
            var before = ColorRGB.FromPacked(expected[i]);
            var after = ColorRGB.FromPacked(surface.Screen[i]);

            // The encode table quantizes to 4096 steps, so a byte may move by one.
            Assert.InRange(after.R, before.R - 1, before.R + 1);
            Assert.InRange(after.G, before.G - 1, before.G + 1);
            Assert.InRange(after.B, before.B - 1, before.B + 1);
        }
    }

    [Fact]
    public void ToneMap_Reinhard_CompressesTowardWhiteWithoutReachingIt()
    {
        var surface = Filled(8, 8, ColorRGB.White);

        var stack = new PostProcessStack();
        stack.Effects.Add(new ToneMapEffect
        {
            Enabled = true,
            Operator = ToneMapOperator.Reinhard,
            Exposure = 1f,
        });

        stack.Apply(surface);

        // White is linear 1, and 1 / (1 + 1) is half the light — visibly darker than white.
        var (r, _, _) = At(surface, 4, 4);
        Assert.InRange(r, 170, 200);
    }

    [Fact]
    public void ToneMap_Exposure_BrightensBeforeTheCurve()
    {
        var dim = Filled(8, 8, new ColorRGB(60, 60, 60));
        var bright = Filled(8, 8, new ColorRGB(60, 60, 60));

        Stack(1f).Apply(dim);
        Stack(4f).Apply(bright);

        Assert.True(At(bright, 4, 4).R > At(dim, 4, 4).R);

        static PostProcessStack Stack(float exposure)
        {
            var stack = new PostProcessStack();
            stack.Effects.Add(new ToneMapEffect { Enabled = true, Exposure = exposure });
            return stack;
        }
    }

    [Fact]
    public void Vignette_DarkensTheCornersAndLeavesTheCentre()
    {
        var surface = Filled(64, 64, ColorRGB.White);

        var stack = new PostProcessStack();
        stack.Effects.Add(new VignetteEffect { Enabled = true, Intensity = 0.8f });

        stack.Apply(surface);

        Assert.Equal(255, At(surface, 32, 32).R);
        Assert.True(At(surface, 0, 0).R < 128);
    }

    [Fact]
    public void Bloom_SpreadsABrightPixelIntoItsNeighbours()
    {
        var surface = new FrameBuffer(64, 64);
        surface.Clear();

        // One small bright block, so the downsampled bright pass has something to catch.
        for (var y = 30; y < 34; y++)
        {
            for (var x = 30; x < 34; x++)
            {
                surface.Screen[x + y * 64] = ColorRGB.White.Color;
            }
        }

        var stack = new PostProcessStack();
        stack.Effects.Add(new BloomEffect { Enabled = true, Threshold = 0.2f, Intensity = 1f });

        stack.Apply(surface);

        // Pixels that were black now carry some of the block's light.
        Assert.True(At(surface, 26, 32).R > 0);
        Assert.True(At(surface, 32, 26).R > 0);

        // Far away it has faded back out.
        Assert.Equal(0, At(surface, 2, 2).R);
    }

    [Fact]
    public void Bloom_BelowTheThreshold_ChangesNothing()
    {
        var surface = Filled(64, 64, new ColorRGB(20, 20, 20));
        var expected = (int[])surface.Screen.Clone();

        var stack = new PostProcessStack();
        stack.Effects.Add(new BloomEffect { Enabled = true, Threshold = 0.9f, Intensity = 1f });

        stack.Apply(surface);

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(ColorRGB.FromPacked(surface.Screen[i]).R, 19, 21);
        }
    }

    [Fact]
    public void Fxaa_FlatImage_HasNoEdgesToSmooth()
    {
        var surface = Filled(32, 32, new ColorRGB(120, 120, 120));
        var expected = (int[])surface.Screen.Clone();

        var stack = new PostProcessStack();
        stack.Effects.Add(new FxaaEffect { Enabled = true });

        stack.Apply(surface);

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(ColorRGB.FromPacked(surface.Screen[i]).R, 119, 121);
        }
    }

    [Fact]
    public void Fxaa_HardEdge_GetsIntermediateValues()
    {
        var surface = new FrameBuffer(32, 32);

        // A black/white step down the middle of the image.
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                surface.Screen[x + y * 32] = (x < 16 ? ColorRGB.White : default).Color;
            }
        }

        var stack = new PostProcessStack();
        stack.Effects.Add(new FxaaEffect { Enabled = true });

        stack.Apply(surface);

        var straddling = At(surface, 16, 16).R;

        Assert.InRange(straddling, 1, 254);
    }

    [Fact]
    public void Find_ReturnsTheEffectFromTheDefaultStack()
    {
        var stack = PostProcessStack.CreateDefault();

        Assert.NotNull(stack.Find<BloomEffect>());
        Assert.NotNull(stack.Find<ToneMapEffect>());
        Assert.NotNull(stack.Find<FxaaEffect>());
        Assert.NotNull(stack.Find<VignetteEffect>());
    }

    [Fact]
    public void EnabledCount_TracksWhichEffectsWouldRun()
    {
        var stack = PostProcessStack.CreateDefault();

        Assert.Equal(0, stack.EnabledCount);

        stack.Find<VignetteEffect>()!.Enabled = true;

        Assert.Equal(1, stack.EnabledCount);
        Assert.True(stack.HasEffects);
    }
}
