using System.Numerics;

namespace SoftEngine.Core.Pipeline.Culling;

/// <summary>
/// A small depth buffer of the frame's biggest occluders, folded into a pyramid so that
/// "is this box behind everything in that rectangle of the screen?" is a handful of reads
/// rather than a walk over the rectangle.
///
/// <para>
/// It is the tile rasterizer's coarse depth bound moved to the other end of the pipeline. That
/// bound rejects a triangle once it has already been transformed, clipped, projected and
/// binned — everything except the pixels. This one rejects a whole mesh before any of that
/// happens, which is the only place the work can still be saved rather than merely spent
/// slightly faster.
/// </para>
///
/// <para>
/// Everything in it is built to fail in one direction. A buffer that under-reports occlusion
/// costs a mesh that could have been skipped; one that over-reports deletes geometry from the
/// frame, and does it in a way that looks like a bug in the rasterizer rather than like a bug
/// in a culling pass. So the depth written to a texel is the triangle's <em>farthest</em> point
/// anywhere within it, folding the pyramid takes the farthest of each group of four, an
/// unwritten texel sits at the far plane where it can hide nothing, and a query is answered a
/// level above the one that was rasterized — see <see cref="MinimumQueryLevel"/>, which is
/// where "a triangle reached this texel" becomes "geometry covers this region".
/// </para>
/// </summary>
public sealed class OcclusionBuffer
{
    /// <summary>The cleared value: nothing occludes, so nothing can be behind it.</summary>
    private const float Far = 1f;

    // Level 0 is the rasterized depth; each level above it halves the dimensions and holds
    // the farthest depth of the up-to-four texels below.
    private float[][] _levels = [];
    private int[] _levelWidth = [];
    private int[] _levelHeight = [];

    /// <summary>Width of level 0, in texels.</summary>
    public int Width { get; private set; }

    /// <summary>Height of level 0, in texels.</summary>
    public int Height { get; private set; }

    /// <summary>Levels in the pyramid, level 0 included.</summary>
    public int LevelCount => _levels.Length;

    /// <summary>
    /// Whether any texel was actually written since the last <see cref="Clear"/>.
    /// </summary>
    /// <remarks>
    /// Written from every rasterizing thread, which is safe because they all write the same
    /// value and none of them reads it. Counting anything finer than "something happened" would
    /// need synchronizing, and there is nothing finer worth knowing here.
    /// </remarks>
    public bool HasOccluders { get; private set; }

    /// <summary>Reads one texel of one level. For tests and for anything that wants to present the buffer.</summary>
    public float DepthAt(int level, int x, int y) => _levels[level][y * _levelWidth[level] + x];

    /// <summary>Dimensions of one level.</summary>
    public (int Width, int Height) SizeOf(int level) => (_levelWidth[level], _levelHeight[level]);

    /// <summary>Sizes the pyramid, reallocating only when the resolution actually changes.</summary>
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

    /// <summary>
    /// Rasterizes one triangle, given its three vertices in clip space.
    ///
    /// <para>
    /// A triangle with a vertex at or behind the eye is dropped rather than clipped. Clipping
    /// it would be the accurate thing and is not worth the code: an occluder that reaches
    /// through the near plane is one the camera is inside, which is the case where it hides
    /// least of the scene in front of it. Dropping it loses occlusion, which is free.
    /// </para>
    /// </summary>
    public void AddTriangle(Vector4 c0, Vector4 c1, Vector4 c2) => AddTriangle(c0, c1, c2, 0, Height);

    /// <inheritdoc cref="AddTriangle(Vector4, Vector4, Vector4)"/>
    /// <param name="rowFrom">First texel row this call may write, inclusive.</param>
    /// <param name="rowTo">Last texel row this call may write, exclusive.</param>
    /// <remarks>
    /// Restricting the rows is how the fill parallelizes. Every worker walks every triangle and
    /// writes only its own contiguous band, so two of them never touch the same texel and the
    /// buffer needs no locking — the same arrangement the shadow pass uses, and for the same
    /// reason.
    /// </remarks>
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

        // Outside the depth range in any corner: near-plane straddlers were already dropped,
        // and something reaching past the far plane cannot be trusted to occlude at the depth
        // its interpolation claims.
        if (p0.Z < 0f || p0.Z > 1f || p1.Z < 0f || p1.Z > 1f || p2.Z < 0f || p2.Z > 1f)
        {
            return;
        }

        var area = Edge(p0, p1, p2.X, p2.Y);

        if (MathF.Abs(area) < 1e-9f)
        {
            return;
        }

        // Dividing by the signed area normalizes the winding away, so one inside-test covers
        // both. Occluding, like shadow casting, has no front and no back — and a closed mesh's
        // back faces cannot make the buffer wrong, because each pixel keeps the nearest of the
        // depths written to it.
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

        // Barycentrics are affine in x and y, so a row starts from one evaluation and steps by
        // a constant — and their gradients are what both conservative rules are built from.
        var dw0dx = (p1.Y - p2.Y) * invArea;
        var dw0dy = (p2.X - p1.X) * invArea;
        var dw1dx = (p2.Y - p0.Y) * invArea;
        var dw1dy = (p0.X - p2.X) * invArea;
        var dw2dx = -(dw0dx + dw1dx);
        var dw2dy = -(dw0dy + dw1dy);

        // Depth is affine in screen space too — that is the whole reason a z-buffer can
        // interpolate it linearly — so the same trick gives the farthest depth over a pixel
        // rather than the depth at its centre.
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

                // Plain centre sampling, and the coverage rule is enforced a level up instead.
                // Demanding that one triangle fill a whole texel by itself looks like the
                // conservative choice and is a trap: two triangles sharing an edge — which is
                // what every quad in every scene is — leave a seam of texels along it that
                // neither one fills alone, so a wall built the only way walls are built
                // acquires a diagonal crack straight through the middle of it and stops
                // occluding anything that crosses it. Sampling centres is watertight across a
                // shared edge, so coverage accumulates the way it has to.
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

    /// <summary>
    /// Folds level 0 up the pyramid, each texel taking the farthest of the four below it.
    ///
    /// <para>
    /// The farthest, because of what a level is a claim about. Level 0 says "at this texel,
    /// anything beyond <em>d</em> is hidden". For that sentence to stay true of a region of
    /// four texels, the depth has to be the weakest of their four claims — which is the
    /// largest. Taking the nearest would produce a pyramid that hides things nothing is in
    /// front of.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Whether everything at or beyond <paramref name="nearestDepth"/> inside the given screen
    /// rectangle is already covered. The rectangle is in normalized device coordinates — x and
    /// y in [-1, 1], y upward — and the depth in the [0, 1] the projection produces.
    /// </summary>
    public bool IsHidden(float minNdcX, float minNdcY, float maxNdcX, float maxNdcY, float nearestDepth)
    {
        if (!HasOccluders || nearestDepth < 0f || nearestDepth > 1f)
        {
            return false;
        }

        // Too small to have a level above the rasterized one, and so too small to say anything
        // about coverage.
        if (_levels.Length <= MinimumQueryLevel)
        {
            return false;
        }

        // NDC y runs up the screen and texel rows run down it, so the vertical bounds swap.
        var x0 = (int)MathF.Floor((minNdcX + 1f) * 0.5f * Width);
        var x1 = (int)MathF.Ceiling((maxNdcX + 1f) * 0.5f * Width) - 1;
        var y0 = (int)MathF.Floor((1f - maxNdcY) * 0.5f * Height);
        var y1 = (int)MathF.Ceiling((1f - minNdcY) * 0.5f * Height) - 1;

        // Anything reaching past the edge of the buffer is left alone. The part of it that is
        // off screen is covered by nothing, so no rectangle that leaves the frame can honestly
        // be called hidden — and the frustum cull has already dealt with the ones entirely out.
        if (x0 < 0 || y0 < 0 || x1 >= Width || y1 >= Height || x1 < x0 || y1 < y0)
        {
            return false;
        }

        // The finest level at which the rectangle covers few enough texels to read directly.
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

    /// <summary>
    /// Most texels a single test is willing to read. Sixteen rather than the four a strict
    /// "two texels each way" rule would allow, because the level matters more than the count
    /// does: a rectangle that spans a little more than a texel is pushed up two whole levels by
    /// the strict rule, and at that level the four texels it reads cover several times the area
    /// it asked about. Everything outside the rectangle but inside those texels is empty
    /// buffer, and empty buffer is at the far plane — so the test fails on geometry nothing
    /// was ever in front of. Reading a 4×4 instead costs twelve array lookups and keeps the
    /// question close to the one that was asked.
    /// </summary>
    private const int MaxSamples = 16;

    /// <summary>
    /// The finest level a query is allowed to read, and the other half of the coverage rule.
    ///
    /// <para>
    /// Level 0 is centre-sampled, so a texel there is written whenever a triangle reaches its
    /// middle — which is not the same as covering it, and on its own would let a triangle
    /// occlude a strip half a texel wider than itself all the way around. Folding takes the
    /// farthest of four, and an unwritten child is at the far plane, so a level-1 texel carries
    /// a real depth only where all four of its children were sampled inside the geometry. That
    /// is coverage, measured on a grid twice as fine as the answer is given on, and it is why
    /// level 0 exists but is never read.
    /// </para>
    /// </summary>
    public const int MinimumQueryLevel = 1;

    /// <summary>
    /// The finest level of the pyramid at which the rectangle covers no more than
    /// <see cref="MaxSamples"/> texels. Every level is a valid answer — the fold guarantees
    /// each one bounds the levels below it — so this is purely a choice of how tight a bound
    /// to spend a few reads on.
    /// </summary>
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

    /// <summary>Clip space to texel space: perspective divide, then the viewport transform.</summary>
    private Vector3 ToPixels(Vector4 clip)
    {
        var inverseW = 1f / clip.W;

        return new Vector3(
            (clip.X * inverseW + 1f) * 0.5f * Width,
            (1f - clip.Y * inverseW) * 0.5f * Height,
            clip.Z * inverseW);
    }

    /// <summary>Twice the signed area of (a, b, point); its sign says which side of the edge the point is on.</summary>
    private static float Edge(in Vector3 a, in Vector3 b, float x, float y) =>
        (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
