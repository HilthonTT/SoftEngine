using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;

namespace SoftEngine.Core.Tests.Rasterization;

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
    public void Fill_Tiles_PartitionTheTriangle()
    {
        const int size = 64;
        const int tile = 16;

        var (tiled, tiledStats) = MakeSurface(size);
        var (full, fullStats) = MakeSurface(size);

        ScanlineRasterizer.Fill(
            full,
            new Vector3(10, 10, 100), new Vector3(40, 10, 100), new Vector3(10, 40, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        for (var ty = 0; ty < size / tile; ty++)
        {
            for (var tx = 0; tx < size / tile; tx++)
            {
                ScanlineRasterizer.Fill(
                    tiled,
                    new Vector3(10, 10, 100), new Vector3(40, 10, 100), new Vector3(10, 40, 100),
                    1f, 1f, 1f,
                    default(EmptyVarying), default, default,
                    new SolidColorShader(ColorRGB.Red),
                    new ScreenTile(tx * tile, ty * tile, (tx + 1) * tile, (ty + 1) * tile));
            }
        }

        Assert.Equal(fullStats.DrawnPixelCount, tiledStats.DrawnPixelCount);

        // Every pixel, not just the count: a tile must cover exactly its own share, with
        // no pixel dropped at a seam and none drawn twice.
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                Assert.Equal(full.GetColor(x, y), tiled.GetColor(x, y));
            }
        }
    }

    [Fact]
    public void Fill_Tile_WritesNothingOutsideItsBounds()
    {
        var (surface, _) = MakeSurface();

        ScanlineRasterizer.Fill(
            surface,
            new Vector3(0, 0, 100), new Vector3(60, 0, 100), new Vector3(0, 60, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red),
            new ScreenTile(16, 16, 32, 32));

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var inside = x >= 16 && x < 32 && y >= 16 && y < 32;

                if (!inside)
                {
                    Assert.Equal(0, surface.GetColor(x, y));
                }
            }
        }

        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(20, 20));
    }
}
