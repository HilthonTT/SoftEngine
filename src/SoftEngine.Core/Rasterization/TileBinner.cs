namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Sorts a frame's triangles into the screen tiles they touch, so the parallel fill phase
/// can hand each worker the few triangles that reach its tile instead of the whole frame.
///
/// Every triangle used to be handed to every worker, which meant a 20 000-triangle model
/// paid its per-triangle setup — vertex sort, edge setup, texture binding — once per core
/// and then wrote a fraction of the rows. Binning pays that setup once per tile a triangle
/// actually covers, which for the small triangles a loaded model is made of is once.
///
/// The bins are built with a counting sort into flat arrays that are reused across frames,
/// so a frame binning tens of thousands of triangles allocates nothing. Triangles are added
/// in draw order and each bin keeps them in that order, which is what lets transparent
/// geometry blend correctly and the pixel probe report writes in the order they happened.
/// </summary>
public sealed class TileBinner
{
    /// <summary>Width and height of a tile in pixels.</summary>
    public const int TileSize = 32;

    // Four ints per triangle: the inclusive tile range it covers (x0, y0, x1, y1).
    // An empty range (x1 < x0) marks a triangle that fell outside the screen entirely.
    private int[] _bounds = [];

    // Each triangle's nearest depth, for the coarse depth rejection in the fill phase.
    private int[] _nearest = [];

    private int[] _counts = [];
    private int[] _offsets = [];
    private int[] _cursor = [];
    private int[] _items = [];

    private int _width;
    private int _height;

    /// <summary>Triangles added since the last <see cref="Reset"/>.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Triangle-in-tile pairs the last <see cref="Build"/> produced — one per tile each
    /// triangle reaches, so a triangle spanning forty tiles counts forty times.
    ///
    /// <para>
    /// It is the frame's fill cost in the unit the fill is actually divided into, which is
    /// what makes it the right thing to decide parallelism on. The triangle count is not:
    /// sixteen triangles that each cover the viewport are far more work than sixteen thousand
    /// that each cover a dozen pixels, and a threshold on the count sends the first of those
    /// down the single-threaded path.
    /// </para>
    /// </summary>
    public int TotalItems { get; private set; }

    public int TilesX { get; private set; }

    public int TilesY { get; private set; }

    public int TileCount => TilesX * TilesY;

    /// <summary>Starts a new frame's binning for a render target of the given size.</summary>
    public void Reset(int width, int height)
    {
        _width = width;
        _height = height;

        TilesX = System.Math.Max(1, (width + TileSize - 1) / TileSize);
        TilesY = System.Math.Max(1, (height + TileSize - 1) / TileSize);

        if (_counts.Length < TileCount)
        {
            _counts = new int[TileCount];
            _offsets = new int[TileCount + 1];
            _cursor = new int[TileCount];
        }
        else
        {
            Array.Clear(_counts, 0, TileCount);
        }

        Count = 0;
        TotalItems = 0;
    }

    /// <summary>
    /// Adds one triangle by its screen-space bounding box and nearest depth, in draw order.
    /// The ordinal a triangle gets back is its position in that order, which is what
    /// <see cref="TrianglesIn"/> hands out.
    /// </summary>
    public void Add(float minX, float minY, float maxX, float maxY, float minZ)
    {
        var ordinal = Count++;

        if (_bounds.Length < Count * 4)
        {
            Array.Resize(ref _bounds, System.Math.Max(Count * 4, _bounds.Length * 2));
            Array.Resize(ref _nearest, _bounds.Length / 4);
        }

        // Guarded rather than clamped: int.MaxValue as a float rounds up past what an int
        // can hold, so converting the clamp's own bound would overflow.
        _nearest[ordinal] = minZ >= int.MaxValue ? int.MaxValue : (int)MathF.Max(minZ, 0f);

        // The rasterizer samples pixel centres. Rounding outward here keeps the bin a
        // superset of the pixels the fill will touch: an extra tile only costs an early-out,
        // while a missing one would drop pixels the triangle owns.
        var x0 = System.Math.Max((int)MathF.Floor(minX - 0.5f), 0);
        var x1 = System.Math.Min((int)MathF.Ceiling(maxX - 0.5f), _width - 1);
        var y0 = System.Math.Max((int)MathF.Floor(minY - 0.5f), 0);
        var y1 = System.Math.Min((int)MathF.Ceiling(maxY - 0.5f), _height - 1);

        var slot = ordinal * 4;

        if (x0 > x1 || y0 > y1)
        {
            _bounds[slot] = 0;
            _bounds[slot + 1] = 0;
            _bounds[slot + 2] = -1;
            _bounds[slot + 3] = -1;
            return;
        }

        var tx0 = x0 / TileSize;
        var tx1 = x1 / TileSize;
        var ty0 = y0 / TileSize;
        var ty1 = y1 / TileSize;

        _bounds[slot] = tx0;
        _bounds[slot + 1] = ty0;
        _bounds[slot + 2] = tx1;
        _bounds[slot + 3] = ty1;

        for (var ty = ty0; ty <= ty1; ty++)
        {
            var row = ty * TilesX;
            for (var tx = tx0; tx <= tx1; tx++)
            {
                _counts[row + tx]++;
            }
        }
    }

    /// <summary>Turns the counts collected by <see cref="Add"/> into the per-tile triangle lists.</summary>
    public void Build()
    {
        var tiles = TileCount;

        var total = 0;
        for (var i = 0; i < tiles; i++)
        {
            _offsets[i] = total;
            _cursor[i] = total;
            total += _counts[i];
        }
        _offsets[tiles] = total;
        TotalItems = total;

        if (_items.Length < total)
        {
            _items = new int[System.Math.Max(total, _items.Length * 2)];
        }

        for (var ordinal = 0; ordinal < Count; ordinal++)
        {
            var slot = ordinal * 4;
            var tx0 = _bounds[slot];
            var ty0 = _bounds[slot + 1];
            var tx1 = _bounds[slot + 2];
            var ty1 = _bounds[slot + 3];

            for (var ty = ty0; ty <= ty1; ty++)
            {
                var row = ty * TilesX;
                for (var tx = tx0; tx <= tx1; tx++)
                {
                    _items[_cursor[row + tx]++] = ordinal;
                }
            }
        }
    }

    /// <summary>
    /// The nearest depth of the triangle added as <paramref name="ordinal"/>. A tile whose
    /// stored depth is everywhere nearer than this can skip the triangle entirely.
    /// </summary>
    public int NearestDepth(int ordinal) => _nearest[ordinal];

    /// <summary>The triangles that reach <paramref name="tileIndex"/>, in draw order.</summary>
    public ReadOnlySpan<int> TrianglesIn(int tileIndex) =>
        _items.AsSpan(_offsets[tileIndex], _offsets[tileIndex + 1] - _offsets[tileIndex]);

    /// <summary>The pixel rectangle <paramref name="tileIndex"/> owns.</summary>
    public ScreenTile TileAt(int tileIndex)
    {
        var tx = tileIndex % TilesX;
        var ty = tileIndex / TilesX;

        return new ScreenTile(
            tx * TileSize,
            ty * TileSize,
            System.Math.Min((tx + 1) * TileSize, _width),
            System.Math.Min((ty + 1) * TileSize, _height));
    }
}
