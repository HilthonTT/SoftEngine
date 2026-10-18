namespace SoftEngine.Core.Rasterization;

public sealed class TileBinner
{
    public const int TileSize = 32;

    private int[] _bounds = [];

    private int[] _nearest = [];

    private int[] _counts = [];
    private int[] _offsets = [];
    private int[] _cursor = [];
    private int[] _items = [];

    private int _width;
    private int _height;

    public int Count { get; private set; }

    public int TotalItems { get; private set; }

    public int TilesX { get; private set; }

    public int TilesY { get; private set; }

    public int TileCount => TilesX * TilesY;

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

    public void Add(float minX, float minY, float maxX, float maxY, float minZ)
    {
        var ordinal = Count++;

        if (_bounds.Length < Count * 4)
        {
            Array.Resize(ref _bounds, System.Math.Max(Count * 4, _bounds.Length * 2));
            Array.Resize(ref _nearest, _bounds.Length / 4);
        }

        _nearest[ordinal] = minZ >= int.MaxValue ? int.MaxValue : (int)MathF.Max(minZ, 0f);

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

    public int NearestDepth(int ordinal) => _nearest[ordinal];

    public ReadOnlySpan<int> TrianglesIn(int tileIndex) =>
        _items.AsSpan(_offsets[tileIndex], _offsets[tileIndex + 1] - _offsets[tileIndex]);

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
