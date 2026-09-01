using System.Numerics;

namespace SoftEngine.Core.Pipeline.Culling;

public sealed class OcclusionBuffer
{
    private const float Far = 1f;

    private float[][] _levels = [];
    private int[] _levelWidth = [];
    private int[] _levelHeight = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int LevelCount => _levels.Length;

    public bool HasOccluders { get; private set; }

    public float DepthAt(int level, int x, int y) => _levels[level][y * _levelWidth[level] + x];

    public (int Width, int Height) SizeOf(int level) => (_levelWidth[level], _levelHeight[level]);

    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width, nameof(width));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height, nameof(height));

        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;

        var levels = 1;
        for (int w = width, h = height; w > 1 || h > 1; levels++)
        {
            w = (w + 1) / 2;
            h = (h + 1) / 2;
        }

        _levels = new float[levels][];
        _levelWidth = new int[levels];
        _levelHeight = new int[levels];

        var levelW = width;
        var levelH = height;

        for (var i = 0; i < levels; i++)
        {
            _levels[i] = new float[levelW * levelH];
            _levelWidth[i] = levelW;
            _levelHeight[i] = levelH;

            levelW = (levelW + 1) / 2;
            levelH = (levelH + 1) / 2;
        }
    }

    public void Clear()
    {
        Array.Fill(_levels[0], Far);

        HasOccluders = false;
    }

    public void AddTriangle(Vector4 c0, Vector4 c1, Vector4 c2) => AddTriangle(c0, c1, c2, 0, Height);

    public void AddTriangle(Vector4 c0, Vector4 c1, Vector4 c2, int rowFrom, int rowTo)
    {
        const float minW = 1e-6f;

        if (c0.W <= minW || c1.W <= minW || c2.W <= minW)
        {
            return;
        }

        var p0 = ToPixels(c0);
        var p1 = ToPixels(c1);
        var p2 = ToPixels(c2);

        if (p0.Z < 0f || p0.Z > 1f || p1.Z < 0f || p1.Z > 1f || p2.Z < 0f || p2.Z > 1f)
        {
            return;
        }

        var area = Edge(p0, p1, p2.X, p2.Y);

        if (MathF.Abs(area) < 1e-9f)
        {
            return;
        }

        var invArea = 1f / area;

        var minX = System.Math.Max((int)MathF.Ceiling(MathF.Min(p0.X, MathF.Min(p1.X, p2.X)) - 0.5f), 0);
        var maxX = System.Math.Min((int)MathF.Floor(MathF.Max(p0.X, MathF.Max(p1.X, p2.X)) - 0.5f), Width - 1);

        if (minX > maxX)
        {
            return;
        }

        var minY = System.Math.Max((int)MathF.Ceiling(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)) - 0.5f), rowFrom);
        var maxY = System.Math.Min((int)MathF.Floor(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)) - 0.5f), rowTo - 1);

        if (minY > maxY)
        {
            return;
        }

        var dw0dx = (p1.Y - p2.Y) * invArea;
        var dw0dy = (p2.X - p1.X) * invArea;
        var dw1dx = (p2.Y - p0.Y) * invArea;
        var dw1dy = (p0.X - p2.X) * invArea;
        var dw2dx = -(dw0dx + dw1dx);
        var dw2dy = -(dw0dy + dw1dy);

        var dzdx = dw0dx * p0.Z + dw1dx * p1.Z + dw2dx * p2.Z;
        var dzdy = dw0dy * p0.Z + dw1dy * p1.Z + dw2dy * p2.Z;

        var zPad = 0.5f * (MathF.Abs(dzdx) + MathF.Abs(dzdy));

        var depth = _levels[0];

        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            var px = minX + 0.5f;

            var w0 = Edge(p1, p2, px, py) * invArea;
            var w1 = Edge(p2, p0, px, py) * invArea;

            var row = y * Width;

            for (var x = minX; x <= maxX; x++, w0 += dw0dx, w1 += dw1dx)
            {
                var w2 = 1f - w0 - w1;

                if (w0 < 0f || w1 < 0f || w2 < 0f)
                {
                    continue;
                }

                var z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z + zPad;

                var texel = row + x;

                HasOccluders = true;

                if (z < depth[texel])
                {
                    depth[texel] = z;
                }
            }
        }
    }

    public void Build()
    {
        for (var level = 1; level < _levels.Length; level++)
        {
            var source = _levels[level - 1];
            var sourceWidth = _levelWidth[level - 1];
            var sourceHeight = _levelHeight[level - 1];

            var target = _levels[level];
            var width = _levelWidth[level];
            var height = _levelHeight[level];

            for (var y = 0; y < height; y++)
            {
                var y0 = y * 2;
                var y1 = System.Math.Min(y0 + 1, sourceHeight - 1);

                for (var x = 0; x < width; x++)
                {
                    var x0 = x * 2;
                    var x1 = System.Math.Min(x0 + 1, sourceWidth - 1);

                    var a = source[y0 * sourceWidth + x0];
                    var b = source[y0 * sourceWidth + x1];
                    var c = source[y1 * sourceWidth + x0];
                    var d = source[y1 * sourceWidth + x1];

                    target[y * width + x] = MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
                }
            }
        }
    }

    public bool IsHidden(float minNdcX, float minNdcY, float maxNdcX, float maxNdcY, float nearestDepth)
    {
        if (!HasOccluders || nearestDepth < 0f || nearestDepth > 1f)
        {
            return false;
        }

        if (_levels.Length <= MinimumQueryLevel)
        {
            return false;
        }

        var x0 = (int)MathF.Floor((minNdcX + 1f) * 0.5f * Width);
        var x1 = (int)MathF.Ceiling((maxNdcX + 1f) * 0.5f * Width) - 1;
        var y0 = (int)MathF.Floor((1f - maxNdcY) * 0.5f * Height);
        var y1 = (int)MathF.Ceiling((1f - minNdcY) * 0.5f * Height) - 1;

        if (x0 < 0 || y0 < 0 || x1 >= Width || y1 >= Height || x1 < x0 || y1 < y0)
        {
            return false;
        }

        var level = LevelFor(x0, y0, x1, y1);

        var width = _levelWidth[level];
        var depth = _levels[level];

        var lx0 = x0 >> level;
        var lx1 = x1 >> level;
        var ly0 = y0 >> level;
        var ly1 = y1 >> level;

        var farthest = 0f;

        for (var y = ly0; y <= ly1; y++)
        {
            var row = y * width;

            for (var x = lx0; x <= lx1; x++)
            {
                var d = depth[row + x];

                if (d > farthest)
                {
                    farthest = d;
                }
            }
        }

        return nearestDepth > farthest;
    }

    private const int MaxSamples = 16;

    public const int MinimumQueryLevel = 1;

    private int LevelFor(int x0, int y0, int x1, int y1)
    {
        for (var level = MinimumQueryLevel; level < _levels.Length - 1; level++)
        {
            var across = (x1 >> level) - (x0 >> level) + 1;
            var down = (y1 >> level) - (y0 >> level) + 1;

            if (across * down <= MaxSamples)
            {
                return level;
            }
        }

        return _levels.Length - 1;
    }

    private Vector3 ToPixels(Vector4 clip)
    {
        var inverseW = 1f / clip.W;

        return new Vector3(
            (clip.X * inverseW + 1f) * 0.5f * Width,
            (1f - clip.Y * inverseW) * 0.5f * Height,
            clip.Z * inverseW);
    }

    private static float Edge(in Vector3 a, in Vector3 b, float x, float y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
