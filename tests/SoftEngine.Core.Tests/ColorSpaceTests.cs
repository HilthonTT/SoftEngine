using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Shading;

namespace SoftEngine.Core.Tests;

public class ColorSpaceTests
{
    [Fact]
    public void ToLinear_Endpoints_MapExactly()
    {
        Assert.Equal(0f, ColorSpace.ToLinear(0));
        Assert.Equal(1f, ColorSpace.ToLinear(255));
    }

    [Fact]
    public void ToSrgb_Endpoints_MapExactly()
    {
        Assert.Equal(0, ColorSpace.ToSrgb(0f));
        Assert.Equal(255, ColorSpace.ToSrgb(1f));
    }

    [Fact]
    public void ToSrgb_ClampsOutOfRangeInput()
    {
        Assert.Equal(0, ColorSpace.ToSrgb(-1f));
        Assert.Equal(255, ColorSpace.ToSrgb(2f));
    }

    [Fact]
    public void RoundTrip_StaysWithinOneStep()
    {
        for (var i = 0; i <= 255; i++)
        {
            var back = ColorSpace.ToSrgb(ColorSpace.ToLinear((byte)i));
            Assert.InRange(back, System.Math.Max(0, i - 1), System.Math.Min(255, i + 1));
        }
    }

    [Fact]
    public void ScaleLinear_HalfIntensity_IsBrighterThanNaiveScaling()
    {
        // Halving in linear light must land far above the naive byte halving (127):
        // sRGB packs more steps into the darks, so 50% light is ~188 encoded.
        var scaled = ColorSpace.ScaleLinear(ColorRGB.White, 0.5f);

        Assert.InRange(scaled.R, 186, 190);
        Assert.Equal(scaled.R, scaled.G);
        Assert.Equal(scaled.R, scaled.B);
    }

    [Fact]
    public void ScaleLinear_FullIntensity_PreservesTheColor()
    {
        var color = new ColorRGB(200, 100, 30);
        var scaled = ColorSpace.ScaleLinear(color, 1f);

        Assert.InRange(scaled.R, 199, 201);
        Assert.InRange(scaled.G, 99, 101);
        Assert.InRange(scaled.B, 29, 31);
    }

    [Fact]
    public void LambertShader_GammaCorrect_LightensMidtones()
    {
        var varying = new IntensityVarying(0.5f);

        var naive = new LambertShader(ColorRGB.White).Shade(varying);
        var gamma = new LambertShader(ColorRGB.White, gammaCorrect: true).Shade(varying);

        Assert.Equal(127, naive.R);
        Assert.InRange(gamma.R, 186, 190);
    }
}
