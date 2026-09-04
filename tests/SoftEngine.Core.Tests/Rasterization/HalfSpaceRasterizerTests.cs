using SoftEngine.Core.Buffers;
using SoftEngine.Core.Diagnostics;
using SoftEngine.Core.Rasterization;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;

namespace SoftEngine.Core.Tests.Rasterization;

public class HalfSpaceRasterizerTests
{
    private static (FrameBuffer Surface, RenderStats Stats) MakeSurface(int size = 64)
    {
        var stats = new RenderStats();
        var surface = new FrameBuffer(size, size) { Stats = stats };
        surface.SetDepthRange(1f, 100f);
        surface.Clear();
        return (surface, stats);
    }

    private static void FillRed(FrameBuffer surface, Vector3 p0, Vector3 p1, Vector3 p2) =>
        HalfSpaceRasterizer.Fill(
            surface, p0, p1, p2,
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

    [Fact]
    public void Fill_Triangle_DrawsPixels()
    {
        var (surface, stats) = MakeSurface();

        FillRed(surface,
            new Vector3(10, 10, 100), new Vector3(30, 10, 100), new Vector3(10, 30, 100));

        Assert.True(stats.DrawnPixelCount > 0);
        Assert.Equal(ColorRGB.Red.Color, surface.GetColor(12, 12));
    }

    [Fact]
    public void Fill_EitherWinding_CoversTheSamePixels()
    {
        var (clockwise, _) = MakeSurface();
        var (counterClockwise, _) = MakeSurface();

        var a = new Vector3(10, 10, 100);
        var b = new Vector3(30, 10, 100);
        var c = new Vector3(10, 30, 100);

        FillRed(clockwise, a, b, c);
        FillRed(counterClockwise, a, c, b);

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                Assert.Equal(clockwise.GetColor(x, y), counterClockwise.GetColor(x, y));
            }
        }
    }

    [Fact]
    public void Fill_TwoTrianglesSharingAnEdge_CoverEachPixelExactlyOnce()
    {
        var (surface, stats) = MakeSurface();

        // The two halves of a quad, meeting along the diagonal. Without a top-left fill rule the
        // pixels sitting exactly on that diagonal belong to both and get drawn twice.
        var topLeft = new Vector3(8, 8, 100);
        var topRight = new Vector3(40, 8, 100);
        var bottomLeft = new Vector3(8, 40, 100);
        var bottomRight = new Vector3(40, 40, 100);

        FillRed(surface, topLeft, topRight, bottomRight);
        FillRed(surface, topLeft, bottomRight, bottomLeft);

        Assert.Equal(32 * 32, stats.DrawnPixelCount);
    }

    [Fact]
    public void Fill_TrianglesTilingAStrip_LeaveNoGapAlongTheirSeams()
    {
        var (surface, stats) = MakeSurface();

        // Four quads side by side. Every interior seam is shared, so a fill rule that is too
        // eager double-draws and one that is too strict leaves a hairline gap.
        for (var quad = 0; quad < 4; quad++)
        {
            var left = 8 + quad * 8;
            var right = left + 8;

            var topLeft = new Vector3(left, 8, 100);
            var topRight = new Vector3(right, 8, 100);
            var bottomLeft = new Vector3(left, 24, 100);
            var bottomRight = new Vector3(right, 24, 100);

            FillRed(surface, topLeft, topRight, bottomRight);
            FillRed(surface, topLeft, bottomRight, bottomLeft);
        }

        Assert.Equal(32 * 16, stats.DrawnPixelCount);

        for (var y = 8; y < 24; y++)
        {
            for (var x = 8; x < 40; x++)
            {
                Assert.Equal(ColorRGB.Red.Color, surface.GetColor(x, y));
            }
        }
    }

    [Theory]
    [InlineData(66.9f, 18.9f, 71.91473f, 52.010212f, 85.9f, 55.4f, 104.22417f, 32.408176f)]
    [InlineData(65.62f, 75.98f, 78.837234f, 96.17978f, 99.22f, 90.38f, 87.71091f, 58.770565f)]
    public void Fill_TrianglesSharingAnEdgeAtFractionalCoordinates_AreWatertight(
        float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        // Integer vertices make every edge product exact, so the seam tests above cannot tell
        // whether the two triangles agree on their shared edge by construction or by luck. Both of
        // these diagonals pass through a pixel centre to within float error: the first quad
        // double-drew (74,33) and the second left a hole at (78,81) before each edge was measured
        // from one agreed endpoint and one screen-aligned block origin.
        AssertWatertight(
            [new Vector2(x0, y0), new Vector2(x1, y1), new Vector2(x2, y2), new Vector2(x3, y3)],
            128);
    }

    [Fact]
    public void Fill_ManyQuadsSplitThroughAPixelCentre_AreWatertight()
    {
        var random = new Random(20240905);

        for (var quad = 0; quad < 300; quad++)
        {
            AssertWatertight(QuadSplitThroughAPixelCentre(random, 128), 128);
        }
    }

    /// <summary>
    /// Fills the two halves of a convex quad and checks that no pixel was drawn twice and that
    /// every pixel comfortably inside the quad was drawn once — the seam along the diagonal is
    /// where either can go wrong.
    /// </summary>
    private static void AssertWatertight(Vector2[] quad, int size)
    {
        var width = size;
        var height = size;

        var surface = new FrameBuffer(width, height) { Stats = new RenderStats() };
        surface.SetDepthRange(1f, 100f);
        surface.SetOverdrawCounting(true);
        surface.Clear();

        Vector3 At(int i) => new(quad[i].X, quad[i].Y, 100);

        FillRed(surface, At(0), At(1), At(2));
        FillRed(surface, At(0), At(2), At(3));

        // Inside is whichever side of every edge the quad's winding puts it on; a pixel centre at
        // least a hundredth of a pixel inside all four is inside beyond any rounding doubt.
        var orientation = System.Math.Sign(Cross(quad[0], quad[1], quad[2]));

        var overdraw = surface.Overdraw;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var count = overdraw[x + y * width];

                Assert.True(count <= 1, $"({x},{y}) was drawn {count} times");

                var centre = new Vector2(x + 0.5f, y + 0.5f);

                var deepInside = true;

                for (var edge = 0; edge < 4; edge++)
                {
                    var a = quad[edge];
                    var b = quad[(edge + 1) % 4];

                    var distance = orientation * Cross(a, b, centre) / Vector2.Distance(a, b);

                    deepInside &= distance >= 0.01;
                }

                if (deepInside)
                {
                    Assert.True(count == 1, $"({x},{y}) is inside the quad but was drawn {count} times");
                }
            }
        }
    }

    private static double Cross(Vector2 a, Vector2 b, Vector2 p) =>
        ((double)b.X - a.X) * ((double)p.Y - a.Y) - ((double)b.Y - a.Y) * ((double)p.X - a.X);

    /// <summary>
    /// A convex quad whose diagonal (first to third vertex) runs through a pixel centre, to within
    /// float error. A random diagonal almost never does, and it is only there that the last bits
    /// of the two triangles' edge values decide who owns the pixel.
    /// </summary>
    private static Vector2[] QuadSplitThroughAPixelCentre(Random random, int size)
    {
        while (true)
        {
            var direction = new Vector2(random.Next(1, 99) / 100f, random.Next(-99, 99) / 100f);
            var centre = new Vector2(random.Next(30, size - 30) + 0.5f, random.Next(30, size - 30) + 0.5f);

            var first = centre - direction * random.Next(10, 40);
            var third = centre + direction * random.Next(10, 40);

            var normal = Vector2.Normalize(new Vector2(-direction.Y, direction.X));
            var middle = (first + third) / 2;

            var second = middle + normal * (random.Next(1000, 3000) / 100f) + (third - first) * (random.Next(-30, 30) / 100f);
            var fourth = middle - normal * (random.Next(1000, 3000) / 100f) + (third - first) * (random.Next(-30, 30) / 100f);

            Vector2[] points = [first, second, third, fourth];

            if (points.Any(p => p.X < 1 || p.Y < 1 || p.X > size - 2 || p.Y > size - 2))
            {
                continue;
            }

            var orientation = System.Math.Sign(Cross(points[0], points[1], points[2]));

            var convex = true;

            for (var i = 0; i < 4 && convex; i++)
            {
                convex = System.Math.Sign(Cross(points[i], points[(i + 1) % 4], points[(i + 2) % 4])) == orientation;
            }

            if (convex)
            {
                return points;
            }
        }
    }

    [Fact]
    public void Fill_TriangleOutsideSurfaceBounds_IsClampedWithoutDrawing()
    {
        var (surface, stats) = MakeSurface();

        FillRed(surface,
            new Vector3(-80, -80, 100), new Vector3(-60, -80, 100), new Vector3(-80, -60, 100));

        Assert.Equal(0, stats.DrawnPixelCount);
    }

    [Fact]
    public void Fill_DegenerateTriangle_DrawsNothing()
    {
        var (surface, stats) = MakeSurface();

        FillRed(surface,
            new Vector3(10, 10, 100), new Vector3(30, 10, 100), new Vector3(20, 10, 100));

        Assert.Equal(0, stats.DrawnPixelCount);
    }

    [Fact]
    public void Fill_NearerTriangleWinsDepthTest()
    {
        var (surface, _) = MakeSurface();

        var a = new Vector3(10, 10, 900);
        var b = new Vector3(40, 10, 900);
        var c = new Vector3(10, 40, 900);

        HalfSpaceRasterizer.Fill(
            surface, a, b, c, 1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        HalfSpaceRasterizer.Fill(
            surface,
            a with { Z = 100 }, b with { Z = 100 }, c with { Z = 100 },
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Green));

        Assert.Equal(ColorRGB.Green.Color, surface.GetColor(15, 15));
    }

    [Fact]
    public void Fill_Tiles_PartitionTheTriangle()
    {
        var (whole, wholeStats) = MakeSurface();
        var (tiled, tiledStats) = MakeSurface();

        var a = new Vector3(4, 4, 100);
        var b = new Vector3(58, 12, 100);
        var c = new Vector3(20, 60, 100);

        FillRed(whole, a, b, c);

        // A triangle split across tiles has to come out as the same pixels, once each — the tile
        // bounds cut across blocks and so exercise the partial-block path from both sides.
        for (var tileY = 0; tileY < 64; tileY += 16)
        {
            for (var tileX = 0; tileX < 64; tileX += 16)
            {
                HalfSpaceRasterizer.Fill(
                    tiled, a, b, c, 1f, 1f, 1f,
                    default(EmptyVarying), default, default,
                    new SolidColorShader(ColorRGB.Red),
                    default,
                    new ScreenTile(tileX, tileY, tileX + 16, tileY + 16));
            }
        }

        Assert.Equal(wholeStats.DrawnPixelCount, tiledStats.DrawnPixelCount);

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                Assert.Equal(whole.GetColor(x, y), tiled.GetColor(x, y));
            }
        }
    }

    [Fact]
    public void Fill_Tile_WritesNothingOutsideItsBounds()
    {
        var (surface, _) = MakeSurface();

        HalfSpaceRasterizer.Fill(
            surface,
            new Vector3(4, 4, 100), new Vector3(60, 4, 100), new Vector3(4, 60, 100),
            1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red),
            default,
            new ScreenTile(16, 16, 32, 32));

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var inside = x >= 16 && x < 32 && y >= 16 && y < 32;

                if (!inside)
                {
                    Assert.NotEqual(ColorRGB.Red.Color, surface.GetColor(x, y));
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(17)]
    public void Fill_MatchesTheScanlineFillPixelForPixel(int size)
    {
        // Sizes either side of the block edge, so blocks that are trivially inside, trivially
        // outside and straddling the triangle all take part.
        var (scanline, scanlineStats) = MakeSurface();
        var (halfSpace, halfSpaceStats) = MakeSurface();

        var a = new Vector3(6.25f, 5.5f, 100);
        var b = new Vector3(6.25f + size, 5.5f, 100);
        var c = new Vector3(6.25f, 5.5f + size, 100);

        ScanlineRasterizer.Fill(
            scanline, a, b, c, 1f, 1f, 1f,
            default(EmptyVarying), default, default,
            new SolidColorShader(ColorRGB.Red));

        FillRed(halfSpace, a, b, c);

        Assert.Equal(scanlineStats.DrawnPixelCount, halfSpaceStats.DrawnPixelCount);

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                Assert.Equal(scanline.GetColor(x, y), halfSpace.GetColor(x, y));
            }
        }
    }

    [Fact]
    public void Fill_InterpolatesPerspectiveCorrectly()
    {
        var (scanline, _) = MakeSurface();
        var (halfSpace, _) = MakeSurface();

        var a = new Vector3(8, 8, 100);
        var b = new Vector3(56, 10, 400);
        var c = new Vector3(12, 56, 700);

        // Wildly different w per vertex, so anything interpolating linearly in screen space
        // instead of in 1/w lands somewhere else entirely.
        void Draw(FrameBuffer surface, bool halfSpaceFill)
        {
            var v0 = new IntensityVarying(0f);
            var v1 = new IntensityVarying(1f);
            var v2 = new IntensityVarying(0.5f);

            var shader = new LambertShader(ColorRGB.White, gammaCorrect: false);

            if (halfSpaceFill)
            {
                HalfSpaceRasterizer.Fill(surface, a, b, c, 1f, 0.25f, 0.1f, v0, v1, v2, shader);
            }
            else
            {
                ScanlineRasterizer.Fill(surface, a, b, c, 1f, 0.25f, 0.1f, v0, v1, v2, shader);
            }
        }

        Draw(scanline, halfSpaceFill: false);
        Draw(halfSpace, halfSpaceFill: true);

        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var expected = ColorRGB.FromPacked(scanline.GetColor(x, y));
                var actual = ColorRGB.FromPacked(halfSpace.GetColor(x, y));

                Assert.True(
                    System.Math.Abs(expected.R - actual.R) <= 1 &&
                    System.Math.Abs(expected.G - actual.G) <= 1 &&
                    System.Math.Abs(expected.B - actual.B) <= 1,
                    $"({x},{y}) scanline {expected.R},{expected.G},{expected.B} " +
                    $"vs half-space {actual.R},{actual.G},{actual.B}");
            }
        }
    }
}
