using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Tests.Pipeline;

public class SuperSamplerTests
{
    private static FrameBuffer Filled(int width, int height, ColorRGB color)
    {
        var surface = new FrameBuffer(width, height);
        Array.Fill(surface.Screen, color.Color);
        return surface;
    }

    [Fact]
    public void ClampFactor_KeepsTheFactorUsable()
    {
        Assert.Equal(SuperSampler.MinFactor, SuperSampler.ClampFactor(0));
        Assert.Equal(SuperSampler.MinFactor, SuperSampler.ClampFactor(-3));
        Assert.Equal(SuperSampler.MaxFactor, SuperSampler.ClampFactor(99));
        Assert.Equal(2, SuperSampler.ClampFactor(2));
    }

    [Fact]
    public void Resolve_FactorOne_CopiesTheRenderTarget()
    {
        var surface = Filled(4, 4, ColorRGB.Red);
        var target = new int[16];

        SuperSampler.Resolve(surface, target, 4, 4, 1);

        Assert.All(target, pixel => Assert.Equal(ColorRGB.Red.Color, pixel));
    }

    [Fact]
    public void Resolve_UniformImage_KeepsTheColour()
    {
        var surface = Filled(8, 8, ColorRGB.Red);
        var target = new int[16];

        SuperSampler.Resolve(surface, target, 4, 4, 2);

        Assert.All(target, pixel => Assert.Equal(ColorRGB.Red.Color, pixel));
    }

    [Fact]
    public void Resolve_HalfCoveredPixel_AveragesInLinearLight()
    {
        var surface = new FrameBuffer(2, 2);
        var white = new ColorRGB(255, 255, 255).Color;
        var black = new ColorRGB(0, 0, 0).Color;

        surface.Screen[0] = white;
        surface.Screen[1] = black;
        surface.Screen[2] = white;
        surface.Screen[3] = black;

        var target = new int[1];
        SuperSampler.Resolve(surface, target, 1, 1, 2);

        var resolved = ColorRGB.FromPacked(target[0]);

        Assert.Equal(ColorSpace.ToSrgb(0.5f), resolved.R);
        Assert.True(resolved.R > 180, $"expected a linear-light average, got {resolved.R}");
    }

    [Fact]
    public void Resolve_EdgeOverClearedBackground_ComesOutPremultiplied()
    {
        var surface = new FrameBuffer(2, 2);
        var white = new ColorRGB(255, 255, 255).Color;

        surface.Screen[0] = white;
        surface.Screen[1] = 0;
        surface.Screen[2] = white;
        surface.Screen[3] = 0;

        var target = new int[1];
        SuperSampler.Resolve(surface, target, 1, 1, 2);

        var alpha = (target[0] >> 24) & 0xFF;

        Assert.Equal(128, alpha);
        Assert.Equal(ColorSpace.ToSrgb(0.5f), ColorRGB.FromPacked(target[0]).R);
    }

    [Fact]
    public void Resolve_EachBlockMapsToItsOwnPixel()
    {
        var surface = new FrameBuffer(4, 4);
        var red = ColorRGB.Red.Color;

        surface.Screen[0] = red;
        surface.Screen[1] = red;
        surface.Screen[4] = red;
        surface.Screen[5] = red;

        var target = new int[4];
        SuperSampler.Resolve(surface, target, 2, 2, 2);

        Assert.Equal(red, target[0]);
        Assert.Equal(0, target[1]);
        Assert.Equal(0, target[2]);
        Assert.Equal(0, target[3]);
    }

    [Fact]
    public void Resolve_SourceTooSmallForTheFactor_Throws()
    {
        var surface = Filled(4, 4, ColorRGB.Red);

        Assert.Throws<ArgumentException>(() => SuperSampler.Resolve(surface, new int[16], 4, 4, 2));
    }

    [Fact]
    public void Resolve_TargetTooSmall_Throws()
    {
        var surface = Filled(8, 8, ColorRGB.Red);

        Assert.Throws<ArgumentException>(() => SuperSampler.Resolve(surface, new int[4], 4, 4, 2));
    }
}
