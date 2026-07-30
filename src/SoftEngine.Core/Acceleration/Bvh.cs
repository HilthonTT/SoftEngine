using SoftEngine.Core.Picking;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Acceleration;

/// <summary>
/// A bounding volume hierarchy over a <see cref="SceneGeometry"/>: the tree that turns "which of
/// these hundred thousand triangles does this ray hit" from a hundred thousand tests into about
/// twenty.
///
/// <para>
/// Every node holds a box containing its subtree. A ray that misses the box misses everything
/// inside it, so one test discards half the scene, and the same argument applies again to whatever
/// is left. The tree is built once and read by every ray, which is the opposite of the trade the
/// renderer makes everywhere else — the rasterizer's culling is rebuilt per frame because it is
/// asked one question per frame, and this is asked millions.
/// </para>
///
/// <para>
/// Splits are chosen by the <b>surface area heuristic</b>: the chance a random ray hits a box is
/// proportional to its surface area, so the expected cost of a split is the area of each side times
/// how many triangles it holds. Candidates are binned rather than sorted — twelve bins per axis
/// across the centroids, sweeping the partial sums — which finds a split within a few percent of
/// the best one for a fraction of the cost of considering every position.
/// </para>
///
/// <para>
/// Ray distances are in the units of the ray's own direction, exactly as <see cref="Ray"/>
/// promises. Traversal never renormalizes, so a caller that hands over an unnormalized direction
/// gets its parameter back on the same scale it gave.
/// </para>
/// </summary>
public sealed class Bvh
{
    /// <summary>Candidate split positions considered per axis.</summary>
    private const int BinCount = 12;

    /// <summary>
    /// Traversal stack depth. A tree deep enough to overflow this would have to be 2⁶⁴ triangles
    /// wide if it were balanced, so reaching it means the build has degenerated, not that a scene
    /// was large.
    /// </summary>
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

    /// <summary>The geometry this tree indexes.</summary>
    public SceneGeometry Geometry => _geometry;

    public int NodeCount { get; }

    public int LeafCount { get; }

    /// <summary>Deepest path from the root, which is what the traversal stack has to hold.</summary>
    public int MaxDepth { get; }

    /// <summary>What a ray ran into: the triangle, how far along, and where on it.</summary>
    public readonly record struct Hit(int Triangle, float Distance, float U, float V)
    {
        /// <summary>The third barycentric weight, which the other two determine.</summary>
        public float W => 1f - U - V;
    }

    /// <summary>
    /// Builds the tree. <paramref name="maxLeafSize"/> is the point below which splitting stops
    /// paying for itself — a handful of triangles tested straight through beats another box test
    /// and two more nodes to walk.
    /// </summary>
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

            // The centroid of the *box*, not of the triangle. They differ, and the box's is what
            // the bins are measured against, so it is what the split has to be chosen on.
            centroids[i] = (minimums[i] + maximums[i]) * 0.5f;
        }

        // A binary tree over n leaves of at least one triangle has at most 2n − 1 nodes.
        var nodes = new List<Node>(System.Math.Max(1, 2 * (count / maxLeafSize + 1)));

        var builder = new Builder(nodes, order, centroids, minimums, maximums, maxLeafSize);

        if (count == 0)
        {
            // An empty tree still needs a root, so every query has a box to miss.
            nodes.Add(new Node(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity), 0, 0));
        }
        else
        {
            nodes.Add(default);
            builder.Fill(0, 0, count, 1);
        }

        return new Bvh([.. nodes], order, geometry, builder.Leaves, builder.Depth);
    }

    /// <summary>
    /// The nearest triangle the ray runs into within <paramref name="maxDistance"/>, or false when
    /// it hits nothing.
    ///
    /// Both faces count, as they do for picking: a ray is a question about geometry, not about
    /// winding, and a light bouncing around the inside of a room is hitting the backs of its walls.
    /// </summary>
    public bool Intersect(in Ray ray, float maxDistance, out Hit hit)
    {
        hit = default;

        // An empty tree's root is neither a leaf nor an interior node — it has no triangles to
        // report and no children to descend to — so it is answered here rather than given a box
        // that every ray would have to test and no ray could ever miss.
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

            // Children are adjacent, so pushing both costs one bounds test each on the way out.
            // The near one is pushed last so it is popped first: finding a close hit early is what
            // lets the far child's box test reject it outright.
            var left = node.Start;
            var right = left + 1;

            var (nearChild, farChild) = Distance(_nodes[left], ray.Origin, inverse) <=
                                        Distance(_nodes[right], ray.Origin, inverse)
                ? (left, right)
                : (right, left);

            if (depth + 2 > MaxStackDepth)
            {
                // Cannot happen for a tree this build produces; dropping the far child would
                // silently lose geometry, so say so instead.
                throw new InvalidOperationException($"BVH traversal exceeded {MaxStackDepth} levels.");
            }

            stack[depth++] = farChild;
            stack[depth++] = nearChild;
        }

        return found;
    }

    /// <summary>The nearest hit anywhere along the ray.</summary>
    public bool Intersect(in Ray ray, out Hit hit) => Intersect(ray, float.PositiveInfinity, out hit);

    /// <summary>
    /// Whether <em>anything</em> lies within <paramref name="maxDistance"/> along the ray — the
    /// question a shadow ray asks.
    ///
    /// It returns on the first hit rather than the nearest one, which is most of why shadow rays
    /// are cheaper than camera rays: there is nothing to compare, so the traversal can stop the
    /// moment it finds an occluder rather than proving one is closest.
    /// </summary>
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

    /// <summary>
    /// Möller-Trumbore, keeping the barycentric coordinates: the same solve
    /// <see cref="ScenePicker.IntersectsTriangle"/> does, except that a ray which is going to shade
    /// what it hits needs to know <em>where</em> on the triangle it landed, not just how far along.
    /// </summary>
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

    /// <summary>
    /// One over each component of the direction, which turns every slab test into a multiply.
    /// A zero component becomes an infinity on purpose: the comparisons below are written so that
    /// a ray running exactly parallel to a slab is inside it or outside it, never both.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 Reciprocal(Vector3 direction) =>
        new(1f / direction.X, 1f / direction.Y, 1f / direction.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IntersectsBox(in Node node, Vector3 origin, Vector3 inverse, float limit) =>
        Distance(node, origin, inverse) < limit;

    /// <summary>
    /// Where the ray enters the box, or <see cref="float.PositiveInfinity"/> when it misses. A ray
    /// starting inside enters at zero, which is what keeps a camera indoors from seeing through the
    /// walls it is between.
    /// </summary>
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

    /// <summary>
    /// One node: the box, and either a run of triangles (a leaf) or the first of two adjacent
    /// children. <see cref="Count"/> tells them apart, which is why a leaf may never be empty.
    /// </summary>
    private readonly struct Node(Vector3 min, Vector3 max, int start, int count)
    {
        public readonly Vector3 Min = min;
        public readonly Vector3 Max = max;

        /// <summary>First triangle of a leaf, or the left child of an interior node.</summary>
        public readonly int Start = start;

        /// <summary>Triangles in a leaf; zero marks an interior node.</summary>
        public readonly int Count = count;
    }

    /// <summary>
    /// The recursive build, carrying the scratch arrays so the recursion does not have to.
    /// </summary>
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

        /// <summary>
        /// Fills the already-reserved node at <paramref name="index"/> with the range
        /// <c>order[start, end)</c>, reserving and filling its children if it splits.
        ///
        /// The two children are reserved <em>together</em>, before either subtree is built, which
        /// is what makes them adjacent — traversal names only the left one and reaches the right by
        /// adding one, so a node costs one index rather than two.
        /// </summary>
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

        /// <summary>
        /// Chooses a split and partitions the range around it, or reports that no split is worth
        /// making — which happens when every centroid sits at the same point, and when the best
        /// candidate costs more than testing the triangles where they are.
        /// </summary>
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

            // Allocated once for all three axes: this runs per node of the build, and a stackalloc
            // inside the loop would grow the frame three times over for no reason.
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

                // Sweep from the left, then from the right, so every candidate's two halves are
                // known without re-accumulating either.
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

                    // The heuristic itself: area × population on each side. The constant cost of
                    // the box tests is left out because it is the same for every candidate.
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

            // Splitting is only worth it if the two halves together are expected to cost less than
            // testing the whole range where it is.
            if (bestCost >= SurfaceArea(nodeMin, nodeMax) * count)
            {
                return false;
            }

            var splitScale = BinCount / Component(extent, bestAxis);
            var splitOrigin = Component(centroidMin, bestAxis);

            // Partition in place: everything left of the chosen bin to the front.
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

        /// <summary>
        /// Half the box's surface area — the factor of two is common to every candidate, so it is
        /// left out. An empty box has none.
        /// </summary>
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
