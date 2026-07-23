using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class ScanlineRasterizerTests
{
    private static (FrameBuffer Surface, RenderStats Stats) MakeSurface(int size = 64)
    {
        var stats = new RenderStats();
        var surface = new FrameBuffer(size, size) { Stats = stats };
        surface.SetDepthRange(1f, 100f);
        surface.Clear();
        return (surface, stats);
    }

    [Fact]
    public void Fill_Triangle_DrawsPixels()
    {
        var (surface, stats) = MakeSurface();

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(10, 10, 100), new Vector3(30, 10, 100), new Vector3(10, 30, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        Assert.True(stats.DrawnPixelCount > 0);
        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(12, 12));
    }

    [Fact]
    public void Fill_UnsortedVertexOrder_DrawsSamePixelCount()
    {
        var (surfaceA, statsA) = MakeSurface();
        var (surfaceB, statsB) = MakeSurface();

        ScanlineRasterizer.Fill(
            surfaceA,
            new Vector3(10, 10, 100), new Vector3(30, 10, 100), new Vector3(10, 30, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        ScanlineRasterizer.Fill(
            surfaceB,
            new Vector3(10, 30, 100), new Vector3(30, 10, 100), new Vector3(10, 10, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        Assert.Equal(statsA.DrawnPixelCount, statsB.DrawnPixelCount);
    }

    [Fact]
    public void Fill_TwoTrianglesSharingAnEdge_CoverEachPixelExactlyOnce()
    {
        var (surface, stats) = MakeSurface();

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(10, 10, 100), new Vector3(20, 10, 100), new Vector3(20, 20, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(10, 10, 100), new Vector3(20, 20, 100), new Vector3(10, 20, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Blue));

        Assert.Equal(100, stats.DrawnPixelCount + stats.BehindZPixelCount);
        Assert.Equal(100, stats.DrawnPixelCount);
    }

    [Fact]
    public void Fill_TriangleOutsideSurfaceBounds_IsClampedWithoutDrawing()
    {
        var (surface, stats) = MakeSurface();

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(-50, -50, 100), new Vector3(-10, -50, 100), new Vector3(-50, -10, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        Assert.Equal(0, stats.DrawnPixelCount);
    }

    [Fact]
    public void Fill_NearerTriangleWinsDepthTest()
    {
        var (surface, stats) = MakeSurface();

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(10, 10, 50), new Vector3(30, 10, 50), new Vector3(10, 30, 50),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(10, 10, 200), new Vector3(30, 10, 200), new Vector3(10, 30, 200),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Blue));

        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(12, 12));
        Assert.True(stats.BehindZPixelCount > 0);
    }

    [Fact]
    public void Fill_RowSlices_PartitionTheTriangle()
    {
        var (sliced, slicedStats) = MakeSurface();
        var (full, fullStats) = MakeSurface();

        ScanlineRasterizer.Fill(
            full,
            new Vector3(10, 10, 100), new Vector3(40, 10, 100), new Vector3(10, 40, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        for (var s = 0; s < 4; s++)
        {
            ScanlineRasterizer.Fill(
                sliced,
                new Vector3(10, 10, 100), new Vector3(40, 10, 100), new Vector3(10, 40, 100),
                1f, 1f, 1f,
                default(EmptyVarying), default, default,
                new SolidColorShader(ColorRGB.Red),
                new RowSlice(s, 4));
        }

        Assert.Equal(fullStats.DrawnPixelCount, slicedStats.DrawnPixelCount);
    }
}
