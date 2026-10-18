using System.Numerics;

namespace SoftEngine.Core.Buffers;

public readonly struct DepthField(float[] depth, int width, int height, float projectionScaleX, float projectionScaleY)
{
    private readonly float[] _depth = depth;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public float ProjectionScaleX { get; } = projectionScaleX;

    public float ProjectionScaleY { get; } = projectionScaleY;

    public float[] Depth => _depth;

    public Vector3 PositionAt(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return new Vector3(0f, 0f, float.NegativeInfinity);
        }

        var w = _depth[x + y * Width];

        if (float.IsPositiveInfinity(w))
        {
            return new Vector3(0f, 0f, float.NegativeInfinity);
        }

        var ndcX = x * (2f / MathF.Max(Width - 1, 1)) - 1f;
        var ndcY = 1f - y * (2f / MathF.Max(Height - 1, 1));

        return new Vector3(ndcX * w / ProjectionScaleX, ndcY * w / ProjectionScaleY, -w);
    }

    public bool ProjectToScreen(Vector3 viewPosition, out int x, out int y, out float distance)
    {
        distance = -viewPosition.Z;

        if (distance <= 1e-6f)
        {
            x = 0;
            y = 0;
            return false;
        }

        var ndcX = viewPosition.X * ProjectionScaleX / distance;
        var ndcY = viewPosition.Y * ProjectionScaleY / distance;

        x = (int)((ndcX + 1f) * 0.5f * MathF.Max(Width - 1, 1) + 0.5f);
        y = (int)((1f - ndcY) * 0.5f * MathF.Max(Height - 1, 1) + 0.5f);

        return (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    public Vector3 NormalAt(int x, int y)
    {
        var width = Width;
        var height = Height;

        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return Vector3.Zero;
        }

        var here = _depth[x + y * width];

        var left = x > 0 ? _depth[x - 1 + y * width] : float.PositiveInfinity;
        var right = x < width - 1 ? _depth[x + 1 + y * width] : float.PositiveInfinity;
        var up = y > 0 ? _depth[x + (y - 1) * width] : float.PositiveInfinity;
        var down = y < height - 1 ? _depth[x + (y + 1) * width] : float.PositiveInfinity;

        var origin = PositionAt(x, y);

        var useLeft = x > 0 && (x == width - 1 || Closer(here, left, right));
        var useUp = y > 0 && (y == height - 1 || Closer(here, up, down));

        var horizontal = useLeft
            ? PositionAt(x - 1, y) - origin
            : origin - PositionAt(x + 1, y);

        var vertical = useUp
            ? PositionAt(x, y - 1) - origin
            : origin - PositionAt(x, y + 1);

        if (!float.IsFinite(horizontal.Z) || !float.IsFinite(vertical.Z))
        {
            return Vector3.Zero;
        }

        var normal = Vector3.Cross(vertical, horizontal);

        if (normal.LengthSquared() < 1e-16f)
        {
            return Vector3.Zero;
        }

        normal = Vector3.Normalize(normal);

        return Vector3.Dot(normal, origin) > 0f ? -normal : normal;
    }

    private static bool Closer(float here, float a, float b) =>
        MathF.Abs(a - here) < MathF.Abs(b - here);
}
