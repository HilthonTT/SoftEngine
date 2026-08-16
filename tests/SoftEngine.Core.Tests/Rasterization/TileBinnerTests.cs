using SoftEngine.Core.Rasterization;

namespace SoftEngine.Core.Tests.Rasterization;

public class TileBinnerTests
{
    private static TileBinner Binner(int width = 256, int height = 128)
    {
        var binner = new TileBinner();
        binner.Reset(width, height);
        return binner;
    }

    [Fact]
    public void Reset_SizesTheGridToCoverTheSurface()
    {
        var binner = Binner(200, 100);

        Assert.True(binner.TilesX * TileBinner.TileSize >= 200);
        Assert.True(binner.TilesY * TileBinner.TileSize >= 100);
        Assert.Equal(binner.TilesX * binner.TilesY, binner.TileCount);
    }

    [Fact]
    public void TileAt_ClipsTheLastTileToTheSurface()
    {
        var binner = Binner(200, 100);

        var last = binner.TileAt(binner.TileCount - 1);

        Assert.Equal(200, last.XTo);
        Assert.Equal(100, last.YTo);
    }

    [Fact]
    public void Add_PutsASmallTriangleInOneTileOnly()
    {
        var binner = Binner();

        // Well inside the first tile.
        binner.Add(4f, 4f, 10f, 10f, 0f);
        binner.Build();

        Assert.Equal(new[] { 0 }, binner.TrianglesIn(0).ToArray());

        for (var tile = 1; tile < binner.TileCount; tile++)
        {
            Assert.Empty(binner.TrianglesIn(tile).ToArray());
        }
    }

    [Fact]
    public void Add_SpansEveryTileTheBoxTouches()
    {
        var binner = Binner();
        var size = TileBinner.TileSize;

        // Straddles the boundary between the first two tiles of the first two rows.
        binner.Add(size - 2f, size - 2f, size + 2f, size + 2f, 0f);
        binner.Build();

        var expected = new[]
        {
            0,
            1,
            binner.TilesX,
            binner.TilesX + 1,
        };

        foreach (var tile in expected)
        {
            Assert.Equal(new[] { 0 }, binner.TrianglesIn(tile).ToArray());
        }
    }

    [Fact]
    public void Add_KeepsDrawOrderWithinATile()
    {
        var binner = Binner();

        binner.Add(1f, 1f, 5f, 5f, 0f);
        binner.Add(2f, 2f, 6f, 6f, 0f);
        binner.Add(3f, 3f, 7f, 7f, 0f);
        binner.Build();

        Assert.Equal(new[] { 0, 1, 2 }, binner.TrianglesIn(0).ToArray());
    }

    [Fact]
    public void Add_OffScreenTriangle_LandsInNoTile()
    {
        var binner = Binner();

        binner.Add(-40f, -40f, -10f, -10f, 0f);
        binner.Build();

        for (var tile = 0; tile < binner.TileCount; tile++)
        {
            Assert.Empty(binner.TrianglesIn(tile).ToArray());
        }
    }

    [Fact]
    public void Add_RecordsTheNearestDepthPerTriangle()
    {
        var binner = Binner();

        binner.Add(1f, 1f, 5f, 5f, 1234f);
        binner.Add(1f, 1f, 5f, 5f, -7f);

        Assert.Equal(1234, binner.NearestDepth(0));

        // A depth in front of the near plane clamps rather than going negative, so the
        // comparison against a stored depth stays meaningful.
        Assert.Equal(0, binner.NearestDepth(1));
    }

    [Fact]
    public void Reset_ClearsThePreviousFrame()
    {
        var binner = Binner();

        binner.Add(1f, 1f, 5f, 5f, 0f);
        binner.Build();

        binner.Reset(256, 128);
        binner.Build();

        Assert.Equal(0, binner.Count);
        Assert.Empty(binner.TrianglesIn(0).ToArray());
    }
}
