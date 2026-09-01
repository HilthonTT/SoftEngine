using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Pipeline.PostProcess;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Shading;
using System.Numerics;

namespace SoftEngine.Core.Tests.Shading;

public class HighDynamicRangeTests
{
    private readonly struct ConstantShader(LinearColor color) : IPixelShader<EmptyVarying>
    {
        private readonly LinearColor _color = color;

        public LinearColor Shade(in EmptyVarying _) => _color;
    }

    private static FrameBuffer Surface(int size, bool hdr)
    {
        var surface = new FrameBuffer(size, size) { Stats = new RenderStats() };
        surface.SetHighDynamicRange(hdr);
        surface.SetDepthRange(1f, 100f);
        surface.Clear();
        return surface;
    }

    private static void FillQuarter(FrameBuffer surface, LinearColor color) =>
        ScanlineRasterizer.Fill(
            surface,
            new Vector3(2, 2, 100), new Vector3(30, 2, 100), new Vector3(2, 30, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new ConstantShader(color),
            default,
            ScreenTile.Full);

    [Fact]
    public void LinearColor_RoundTripsThroughSrgb()
    {
        for (var i = 0; i < 256; i++)
        {
            var original = new ColorRGB((byte)i, (byte)(255 - i), (byte)((i * 7) % 256));
            var round = ((LinearColor)original).ToColorRGB();

            Assert.Equal(original.Color, round.Color);
        }
    }

    [Fact]
    public void LinearColor_AddsWithoutSaturating()
    {
        var sum = LinearColor.White + LinearColor.White + LinearColor.White;

        Assert.Equal(3f, sum.R);
        Assert.Equal(3f, sum.G);
        Assert.Equal(3f, sum.B);

        Assert.Equal(255, sum.ToColorRGB().R);
    }

    [Fact]
    public void LdrTarget_ClipsAboveWhite()
    {
        var surface = Surface(32, hdr: false);

        FillQuarter(surface, new LinearColor(4f, 4f, 4f));

        var pixel = ColorRGB.FromPacked(surface.GetColor(8, 8));
        Assert.Equal(255, pixel.R);
    }

    [Fact]
    public void HdrTarget_KeepsLightAboveWhite()
    {
        var surface = Surface(32, hdr: true);

        FillQuarter(surface, new LinearColor(4f, 2f, 1f));

        var i = (8 + 8 * surface.Width) * 3;
        Assert.Equal(4f, surface.HdrColor[i]);
        Assert.Equal(2f, surface.HdrColor[i + 1]);
        Assert.Equal(1f, surface.HdrColor[i + 2]);

        Assert.Equal(0, surface.GetColor(8, 8));
    }

    [Fact]
    public void ResolveToScreen_EncodesAndClamps()
    {
        var surface = Surface(32, hdr: true);

        FillQuarter(surface, new LinearColor(4f, 0.5f, 0f));
        surface.ResolveToScreen();

        var pixel = ColorRGB.FromPacked(surface.GetColor(8, 8));

        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 186, 190);
        Assert.Equal(0, pixel.B);
        Assert.Equal(0xFF, (surface.GetColor(8, 8) >> 24) & 0xFF);
    }

    [Fact]
    public void ToneMap_OverAnHdrTarget_SeparatesHighlightsAnLdrTargetWouldFlatten()
    {
        var stack = new PostProcessStack();
        stack.Effects.Add(new ToneMapEffect { Enabled = true, Exposure = 1f });

        static float Resolve(bool hdr, float intensity)
        {
            var surface = Surface(32, hdr);
            FillQuarter(surface, new LinearColor(intensity, intensity, intensity));

            var stack = new PostProcessStack();
            stack.Effects.Add(new ToneMapEffect { Enabled = true, Exposure = 1f });
            stack.Apply(surface);

            return ColorRGB.FromPacked(surface.GetColor(8, 8)).R;
        }

        Assert.Equal(Resolve(hdr: false, 2f), Resolve(hdr: false, 8f));

        Assert.True(Resolve(hdr: true, 2f) < Resolve(hdr: true, 8f));
    }

    [Fact]
    public void Bloom_OverAnHdrTarget_ScalesWithTheLightItIsGiven()
    {
        static float Bleed(float intensity)
        {
            var surface = Surface(64, hdr: true);
            FillQuarter(surface, new LinearColor(intensity, intensity, intensity));

            var stack = new PostProcessStack();
            stack.Effects.Add(new BloomEffect { Enabled = true, Threshold = 0.8f, Intensity = 1f });
            stack.Apply(surface);

            return ColorRGB.FromPacked(surface.GetColor(50, 50)).R;
        }

        Assert.True(Bleed(8f) > Bleed(1.2f));
    }

    [Fact]
    public void HdrTarget_BlendsTransparencyInLinearLight()
    {
        var surface = Surface(8, hdr: true);

        surface.PutPixel(1, 1, 100, LinearColor.Black);
        Assert.True(surface.PutPixelBlend(1, 1, 50, LinearColor.White, 0.5f));

        var i = (1 + 1 * surface.Width) * 3;
        Assert.Equal(0.5f, surface.HdrColor[i], 5);
    }

    [Fact]
    public void SwitchingModes_DoesNotCarryStalePixelsOver()
    {
        var surface = new FrameBuffer(16, 16) { Stats = new RenderStats() };
        surface.SetDepthRange(1f, 100f);

        surface.SetHighDynamicRange(true);
        surface.Clear();
        FillQuarter(surface, new LinearColor(3f, 3f, 3f));

        surface.SetHighDynamicRange(false);
        surface.Clear();

        Assert.False(surface.IsHighDynamicRange);
        Assert.Equal(0, surface.GetColor(4, 4));
    }
}
