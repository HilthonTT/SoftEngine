using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class FrameBufferTests
{
    [Fact]
    public void PutPixel_EmptyBuffer_DrawsAndStoresColor()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();

        var drawn = surface.PutPixel(5, 5, 100, ColorRGB.Red);

        Assert.True(drawn);
        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(5, 5));
        Assert.Equal(100, surface.GetDepth(5, 5));
    }

    [Fact]
    public void PutPixel_FartherThanExisting_IsRejected()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();
        surface.PutPixel(5, 5, 100, ColorRGB.Red);

        var drawn = surface.PutPixel(5, 5, 200, ColorRGB.Blue);

        Assert.False(drawn);
        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(5, 5));
    }

    [Fact]
    public void PutPixel_NearerThanExisting_Overwrites()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();
        surface.PutPixel(5, 5, 200, ColorRGB.Red);

        var drawn = surface.PutPixel(5, 5, 100, ColorRGB.Blue);

        Assert.True(drawn);
        Assert.Equal(ColorRGB.Blue.Color, surface.GetColor(5, 5));
        Assert.Equal(100, surface.GetDepth(5, 5));
    }

    [Fact]
    public void Clear_ResetsColorAndDepth()
    {
        var surface = new FrameBuffer(16, 16);
        surface.Clear();
        surface.PutPixel(5, 5, 100, ColorRGB.Red);

        surface.Clear();

        Assert.Equal(0, surface.GetColor(5, 5));
        Assert.Equal(FrameBuffer.DepthResolution, surface.GetDepth(5, 5));
    }

    [Fact]
    public void ToScreen3_NdcOrigin_MapsToScreenCenter()
    {
        var surface = new FrameBuffer(101, 101);
        surface.SetDepthRange(1f, 100f);

        var screen = surface.ToScreen3(new Vector4(0, 0, 0.5f, 1f));

        Assert.Equal(50f, screen.X, 3);
        Assert.Equal(50f, screen.Y, 3);
    }

    [Fact]
    public void ToScreen3_NdcCorners_MapToScreenCorners()
    {
        var surface = new FrameBuffer(101, 101);
        surface.SetDepthRange(1f, 100f);

        var topLeft = surface.ToScreen3(new Vector4(-1, 1, 0.5f, 1f));
        var bottomRight = surface.ToScreen3(new Vector4(1, -1, 0.5f, 1f));

        Assert.Equal(0f, topLeft.X, 3);
        Assert.Equal(0f, topLeft.Y, 3);
        Assert.Equal(100f, bottomRight.X, 3);
        Assert.Equal(100f, bottomRight.Y, 3);
    }

    [Fact]
    public void ToScreen3_DepthIncreasesWithDistance()
    {
        var surface = new FrameBuffer(100, 100);
        surface.SetDepthRange(1f, 100f);

        var near = surface.ToScreen3(new Vector4(0, 0, 0, 1f));
        var far = surface.ToScreen3(new Vector4(0, 0, 50, 50f));

        Assert.True(near.Z < far.Z);
    }
}
