using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Tests.Pipeline;

public class FogTests
{
    private static FogSettings LinearFog(float start, float end) => new()
    {
        Enabled = true,
        Mode = FogMode.Linear,
        Start = start,
        End = end,
        Color = ColorRGB.Blue,
    };

    [Fact]
    public void DefaultState_IsOpaqueWithoutFog()
    {
        var state = default(RasterState);

        Assert.True(state.IsOpaque);
        Assert.False(state.HasFog);
        Assert.Equal(1f, state.Alpha);
    }

    [Fact]
    public void From_DisabledFog_HasNoFog()
    {
        var fog = LinearFog(10f, 20f);
        fog.Enabled = false;

        Assert.False(RasterState.From(fog).HasFog);
    }

    [Fact]
    public void ApplyFog_BeforeStart_LeavesTheColor()
    {
        var state = RasterState.From(LinearFog(10f, 20f));

        Assert.Equal(ColorRGB.Red.Color, state.ApplyFog(ColorRGB.Red, 5f).ToColorRGB().Color);
    }

    [Fact]
    public void ApplyFog_PastEnd_IsPureFogColor()
    {
        var state = RasterState.From(LinearFog(10f, 20f));

        Assert.Equal(ColorRGB.Blue.Color, state.ApplyFog(ColorRGB.Red, 30f).ToColorRGB().Color);
    }

    [Fact]
    public void ApplyFog_Midway_BlendsHalfway()
    {
        var state = RasterState.From(LinearFog(10f, 20f));

        var foggy = state.ApplyFog(ColorRGB.Red, 15f);

        // Halfway in linear light, which is where mixing light is defined — not halfway in
        // sRGB bytes. Half the light of a full-intensity channel encodes to about 188, not
        // to 128; the latter is a good deal darker than half the light.
        Assert.Equal(0.5f, foggy.R, 3);
        Assert.Equal(0f, foggy.G);
        Assert.Equal(0.5f, foggy.B, 3);

        var encoded = foggy.ToColorRGB();
        Assert.InRange(encoded.R, 186, 190);
        Assert.Equal(0, encoded.G);
        Assert.InRange(encoded.B, 186, 190);
    }

    [Fact]
    public void ApplyFog_Exponential_ThickensWithDistance()
    {
        var state = RasterState.From(new FogSettings
        {
            Enabled = true,
            Mode = FogMode.Exponential,
            Density = 0.1f,
            Color = ColorRGB.Blue,
        });

        var near = state.ApplyFog(ColorRGB.Red, 1f).ToColorRGB();
        var far = state.ApplyFog(ColorRGB.Red, 50f).ToColorRGB();

        Assert.True(near.R > far.R);
        Assert.True(near.B < far.B);
        Assert.InRange(far.B, 250, 255); // e^-5 leaves under 1% of the surface color
    }

    [Fact]
    public void Fill_WithFogState_FogsByViewDepth()
    {
        var stats = new RenderStats();
        var surface = new FrameBuffer(64, 64) { Stats = stats };
        surface.SetDepthRange(1f, 100f);
        surface.Clear();

        var state = RasterState.From(LinearFog(10f, 20f));

        // All three vertices at w = 30, well past the fog end.
        ScanlineRasterizer.Fill(
            surface,
            new Vector3(10, 10, 100), new Vector3(30, 10, 100), new Vector3(10, 30, 100),
            1f / 30f, 1f / 30f, 1f / 30f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red),
            state,
            ScreenTile.Full);

        Assert.True(stats.DrawnPixelCount > 0);
        Assert.Equal(ColorRGB.Blue.Color, surface.GetColor(12, 12));
    }

    [Fact]
    public void Fill_WithFogState_NearGeometryStaysClear()
    {
        var surface = new FrameBuffer(64, 64) { Stats = new RenderStats() };
        surface.SetDepthRange(1f, 100f);
        surface.Clear();

        var state = RasterState.From(LinearFog(10f, 20f));

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(10, 10, 100), new Vector3(30, 10, 100), new Vector3(10, 30, 100),
            1f / 5f, 1f / 5f, 1f / 5f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red),
            state,
            ScreenTile.Full);

        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(12, 12));
    }
}
