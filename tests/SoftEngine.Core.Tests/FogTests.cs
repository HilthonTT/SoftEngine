using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Tests;

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

        Assert.Equal(ColorRGB.Red.Color, state.ApplyFog(ColorRGB.Red, 5f).Color);
    }

    [Fact]
    public void ApplyFog_PastEnd_IsPureFogColor()
    {
        var state = RasterState.From(LinearFog(10f, 20f));

        Assert.Equal(ColorRGB.Blue.Color, state.ApplyFog(ColorRGB.Red, 30f).Color);
    }

    [Fact]
    public void ApplyFog_Midway_BlendsHalfway()
    {
        var state = RasterState.From(LinearFog(10f, 20f));

        var foggy = state.ApplyFog(ColorRGB.Red, 15f);

        Assert.InRange(foggy.R, 126, 128);
        Assert.Equal(0, foggy.G);
        Assert.InRange(foggy.B, 126, 128);
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

        var near = state.ApplyFog(ColorRGB.Red, 1f);
        var far = state.ApplyFog(ColorRGB.Red, 50f);

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
            RowSlice.Full);

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
            RowSlice.Full);

        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(12, 12));
    }
}
