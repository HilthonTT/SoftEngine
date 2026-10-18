using SoftEngine.Core.Picking;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Acceleration;

public sealed class Bvh
{
    private const int BinCount = 12;

    private const int MaxStackDepth = 64;

    private readonly Node[] _nodes;
    private readonly int[] _triangles;
    private readonly SceneGeometry _geometry;

    private Bvh(Node[] nodes, int[] triangles, SceneGeometry geometry, int leaves, int depth)
    {
        _nodes = nodes;
        _triangles = triangles;
        _geometry = geometry;

        NodeCount = nodes.Length;
        LeafCount = leaves;
        MaxDepth = depth;
    }

    public SceneGeometry Geometry => _geometry;

    public int NodeCount { get; }

    public int LeafCount { get; }

    public (Vector3 Min, Vector3 Max) Bounds => (_nodes[0].Min, _nodes[0].Max);

    public int MaxDepth { get; }

    public readonly record struct Hit(int Triangle, float Distance, float U, float V)
    {
        public float W => 1f - U - V;
    }

    public static Bvh Build(SceneGeometry geometry, int maxLeafSize = 4)
    {
        ArgumentNullException.ThrowIfNull(geometry, nameof(geometry));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLeafSize);

        var count = geometry.TriangleCount;

        var order = new int[count];
        var centroids = new Vector3[count];
        var minimums = new Vector3[count];
        var maximums = new Vector3[count];

        for (var i = 0; i < count; i++)
        {
            var (a, b, c) = geometry.Corners(i);

            order[i] = i;
            minimums[i] = Vector3.Min(a, Vector3.Min(b, c));
            maximums[i] = Vector3.Max(a, Vector3.Max(b, c));

            centroids[i] = (minimums[i] + maximums[i]) * 0.5f;
        }

        var nodes = new List<Node>(System.Math.Max(1, 2 * (count / maxLeafSize + 1)));

        var builder = new Builder(nodes, order, centroids, minimums, maximums, maxLeafSize);

        if (count == 0)
        {
            nodes.Add(new Node(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity), 0, 0));
        }
        else
        {
            nodes.Add(default);
            builder.Fill(0, 0, count, 1);
        }

        return new Bvh([.. nodes], order, geometry, builder.Leaves, builder.Depth);
    }

    public bool Intersect(in Ray ray, float maxDistance, out Hit hit)
    {
        hit = default;

        if (_geometry.TriangleCount == 0)
        {
            return false;
        }

        var found = false;
        var nearest = maxDistance;

        var inverse = Reciprocal(ray.Direction);

        Span<int> stack = stackalloc int[MaxStackDepth];
        var depth = 0;

        stack[depth++] = 0;

        while (depth > 0)
        {
            ref readonly var node = ref _nodes[stack[--depth]];

            if (!IntersectsBox(node, ray.Origin, inverse, nearest))
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var triangle = _triangles[i];
                    var (a, b, c) = _geometry.Corners(triangle);

                    if (!IntersectsTriangle(ray, a, b, c, out var distance, out var u, out var v) ||
                        distance >= nearest)
                    {
                        continue;
                    }

                    nearest = distance;
                    hit = new Hit(triangle, distance, u, v);
                    found = true;
                }

                continue;
            }

            var left = node.Start;
            var right = left + 1;

            var (nearChild, farChild) = Distance(_nodes[left], ray.Origin, inverse) <=
                                        Distance(_nodes[right], ray.Origin, inverse)
                ? (left, right)
                : (right, left);

            if (depth + 2 > MaxStackDepth)
            {
                throw new InvalidOperationException($"BVH traversal exceeded {MaxStackDepth} levels.");
            }

            stack[depth++] = farChild;
            stack[depth++] = nearChild;
        }

        return found;
    }

    public bool Intersect(in Ray ray, out Hit hit) => Intersect(ray, float.PositiveInfinity, out hit);

    public bool IsOccluded(in Ray ray, float maxDistance)
    {
        if (_geometry.TriangleCount == 0)
        {
            return false;
        }

        var inverse = Reciprocal(ray.Direction);

        Span<int> stack = stackalloc int[MaxStackDepth];
        var depth = 0;

        stack[depth++] = 0;

        while (depth > 0)
        {
            ref readonly var node = ref _nodes[stack[--depth]];

            if (!IntersectsBox(node, ray.Origin, inverse, maxDistance))
            {
                continue;
            }

            if (node.Count > 0)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                {
                    var (a, b, c) = _geometry.Corners(_triangles[i]);

                    if (IntersectsTriangle(ray, a, b, c, out var distance, out _, out _) && distance < maxDistance)
                    {
                        return true;
                    }
                }

                continue;
            }

            if (depth + 2 > MaxStackDepth)
            {
                throw new InvalidOperationException($"BVH traversal exceeded {MaxStackDepth} levels.");
            }

            stack[depth++] = node.Start;
            stack[depth++] = node.Start + 1;
        }

        return false;
    }

    public static bool IntersectsTriangle(
        in Ray ray, Vector3 a, Vector3 b, Vector3 c,
        out float distance, out float u, out float v)
    {
        const float epsilon = 1e-8f;

        distance = 0f;
        u = 0f;
        v = 0f;

        var edge1 = b - a;
        var edge2 = c - a;

        var pivot = Vector3.Cross(ray.Direction, edge2);
        var determinant = Vector3.Dot(edge1, pivot);

        if (MathF.Abs(determinant) < epsilon)
        {
            return false;
        }

        var inverse = 1f / determinant;
        var toVertex = ray.Origin - a;

        u = Vector3.Dot(toVertex, pivot) * inverse;
        if (u < 0f || u > 1f)
        {
            return false;
        }

        var across = Vector3.Cross(toVertex, edge1);

        v = Vector3.Dot(ray.Direction, across) * inverse;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        distance = Vector3.Dot(edge2, across) * inverse;

        return distance > epsilon;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 Reciprocal(Vector3 direction) =>
        new(1f / direction.X, 1f / direction.Y, 1f / direction.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IntersectsBox(in Node node, Vector3 origin, Vector3 inverse, float limit) =>
        Distance(node, origin, inverse) < limit;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Distance(in Node node, Vector3 origin, Vector3 inverse)
    {
        var first = (node.Min - origin) * inverse;
        var second = (node.Max - origin) * inverse;

        var entry = Vector3.Min(first, second);
        var exit = Vector3.Max(first, second);

        var near = MathF.Max(MathF.Max(entry.X, entry.Y), MathF.Max(entry.Z, 0f));
        var far = MathF.Min(MathF.Min(exit.X, exit.Y), exit.Z);

        return near <= far ? near : float.PositiveInfinity;
    }

    private readonly struct Node(Vector3 min, Vector3 max, int start, int count)
    {
        public readonly Vector3 Min = min;
        public readonly Vector3 Max = max;

        public readonly int Start = start;

        public readonly int Count = count;
    }

    private sealed class Builder(
        List<Node> nodes,
        int[] order,
        Vector3[] centroids,
        Vector3[] minimums,
        Vector3[] maximums,
        int maxLeafSize)
    {
        public int Leaves { get; private set; }

        public int Depth { get; private set; }

        public void Fill(int index, int start, int end, int depth)
        {
            Depth = System.Math.Max(Depth, depth);

            var (min, max) = Bounds(start, end);
            var count = end - start;

            if (count <= maxLeafSize ||
                depth >= MaxStackDepth - 1 ||
                !TrySplit(start, end, out var middle))
            {
                nodes[index] = new Node(min, max, start, count);
                Leaves++;

                return;
            }

            var left = nodes.Count;

            nodes.Add(default);
            nodes.Add(default);

            nodes[index] = new Node(min, max, left, 0);

            Fill(left, start, middle, depth + 1);
            Fill(left + 1, middle, end, depth + 1);
        }

        private bool TrySplit(int start, int end, out int middle)
        {
            middle = 0;

            var count = end - start;

            var centroidMin = new Vector3(float.PositiveInfinity);
            var centroidMax = new Vector3(float.NegativeInfinity);

            for (var i = start; i < end; i++)
            {
                centroidMin = Vector3.Min(centroidMin, centroids[order[i]]);
                centroidMax = Vector3.Max(centroidMax, centroids[order[i]]);
            }

            var extent = centroidMax - centroidMin;

            var bestAxis = -1;
            var bestBin = -1;
            var bestCost = float.PositiveInfinity;

            Span<int> counts = stackalloc int[BinCount];
            Span<Vector3> binMin = stackalloc Vector3[BinCount];
            Span<Vector3> binMax = stackalloc Vector3[BinCount];

            Span<float> leftArea = stackalloc float[BinCount];
            Span<int> leftCount = stackalloc int[BinCount];

            for (var axis = 0; axis < 3; axis++)
            {
                var width = Component(extent, axis);

                if (width < 1e-12f)
                {
                    continue;
                }

                var scale = BinCount / width;
                var origin = Component(centroidMin, axis);

                counts.Clear();
                binMin.Fill(new Vector3(float.PositiveInfinity));
                binMax.Fill(new Vector3(float.NegativeInfinity));

                for (var i = start; i < end; i++)
                {
                    var triangle = order[i];
                    var bin = BinOf(Component(centroids[triangle], axis), origin, scale);

                    counts[bin]++;
                    binMin[bin] = Vector3.Min(binMin[bin], minimums[triangle]);
                    binMax[bin] = Vector3.Max(binMax[bin], maximums[triangle]);
                }

                var runningMin = new Vector3(float.PositiveInfinity);
                var runningMax = new Vector3(float.NegativeInfinity);
                var running = 0;

                for (var bin = 0; bin < BinCount; bin++)
                {
                    running += counts[bin];
                    runningMin = Vector3.Min(runningMin, binMin[bin]);
                    runningMax = Vector3.Max(runningMax, binMax[bin]);

                    leftCount[bin] = running;
                    leftArea[bin] = SurfaceArea(runningMin, runningMax);
                }

                runningMin = new Vector3(float.PositiveInfinity);
                runningMax = new Vector3(float.NegativeInfinity);
                running = 0;

                for (var bin = BinCount - 1; bin > 0; bin--)
                {
                    running += counts[bin];
                    runningMin = Vector3.Min(runningMin, binMin[bin]);
                    runningMax = Vector3.Max(runningMax, binMax[bin]);

                    if (leftCount[bin - 1] == 0 || running == 0)
                    {
                        continue;
                    }

                    var cost = leftArea[bin - 1] * leftCount[bin - 1] + SurfaceArea(runningMin, runningMax) * running;

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestBin = bin;
                    }
                }
            }

            if (bestAxis < 0)
            {
                return false;
            }

            var (nodeMin, nodeMax) = Bounds(start, end);

            if (bestCost >= SurfaceArea(nodeMin, nodeMax) * count)
            {
                return false;
            }

            var splitScale = BinCount / Component(extent, bestAxis);
            var splitOrigin = Component(centroidMin, bestAxis);

            var pivot = start;

            for (var i = start; i < end; i++)
            {
                if (BinOf(Component(centroids[order[i]], bestAxis), splitOrigin, splitScale) < bestBin)
                {
                    (order[pivot], order[i]) = (order[i], order[pivot]);
                    pivot++;
                }
            }

            if (pivot == start || pivot == end)
            {
                return false;
            }

            middle = pivot;
            return true;
        }

        private (Vector3 Min, Vector3 Max) Bounds(int start, int end)
        {
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);

            for (var i = start; i < end; i++)
            {
                min = Vector3.Min(min, minimums[order[i]]);
                max = Vector3.Max(max, maximums[order[i]]);
            }

            return (min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BinOf(float position, float origin, float scale) =>
            System.Math.Clamp((int)((position - origin) * scale), 0, BinCount - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Component(Vector3 v, int axis) => axis switch
        {
            0 => v.X,
            1 => v.Y,
            _ => v.Z,
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SurfaceArea(Vector3 min, Vector3 max)
        {
            var size = max - min;

            if (size.X < 0f || size.Y < 0f || size.Z < 0f)
            {
                return 0f;
            }

            return size.X * size.Y + size.Y * size.Z + size.Z * size.X;
        }
    }
}
