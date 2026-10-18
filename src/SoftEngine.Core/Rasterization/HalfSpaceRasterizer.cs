using SoftEngine.Core.Buffers;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Block-based half-space triangle fill.
/// <para>
/// Where <see cref="ScanlineRasterizer"/> walks two edges down the triangle and fills the span
/// between them, this evaluates one signed distance per edge — positive inside — and keeps every
/// pixel where all three are. Each is linear in screen space, so a whole block of pixels can be
/// classified from its four corners: if any edge is negative at all four the block is outside and
/// costs three comparisons, and if all three are positive at all four the block is wholly inside
/// and no pixel needs testing at all. Only blocks an edge actually crosses are examined pixel by
/// pixel, and there coverage is a comparison per lane rather than a branch per pixel.
/// </para>
/// <para>
/// Depth, 1/w and the varyings divided by w are all linear in screen space too, so none of them
/// is ever recomputed: each is evaluated once at a block's first pixel and stepped by its gradient
/// from there. The edge functions alone are evaluated outright at every pixel, from an origin the
/// triangle shares with its neighbours, because they decide which of two triangles a pixel on
/// their common edge belongs to — see <see cref="TrianglePlanes"/> for why stepping would not do.
/// </para>
/// </summary>
public static class HalfSpaceRasterizer
{
    /// <summary>
    /// Edge of the square of pixels classified together. Eight matches the usual vector width; a
    /// wider machine uses its own so a block is never too small to fill one.
    /// </summary>
    public static readonly int BlockSize = System.Math.Max(8, Vector<float>.Count);

    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float invW0, float invW1, float invW2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
        => Fill(surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2, shader, default, ScreenTile.Full);

    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float invW0, float invW1, float invW2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader,
        in ScreenTile tile)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
        => Fill(surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2, shader, default, tile);

    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float invW0, float invW1, float invW2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader,
        in RasterState state,
        in ScreenTile tile)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var area = ScanlineRasterizer.Cross2D(p0, p1, p2);

        if (!float.IsFinite(area) || area == 0f)
        {
            return;
        }

        // Fix one winding so a single sign means "inside" for all three edges. Swapping two
        // vertices flips the sign and carries their varyings with them.
        if (area < 0f)
        {
            (p1, p2) = (p2, p1);
            (v1, v2) = (v2, v1);
            (invW1, invW2) = (invW2, invW1);

            area = -area;
        }

        var xFrom = System.Math.Max(
            RasterMath.FirstCenterAtOrAfter(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))),
            System.Math.Max(tile.XFrom, 0));

        var xTo = System.Math.Min(
            RasterMath.FirstCenterAtOrAfter(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))),
            System.Math.Min(tile.XTo, surface.Width));

        var yFrom = System.Math.Max(
            RasterMath.FirstCenterAtOrAfter(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))),
            System.Math.Max(tile.YFrom, 0));

        var yTo = System.Math.Min(
            RasterMath.FirstCenterAtOrAfter(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))),
            System.Math.Min(tile.YTo, surface.Height));

        if (xFrom >= xTo || yFrom >= yTo)
        {
            return;
        }

        // Everything below interpolates varying/w and divides by the interpolated 1/w, which is
        // what makes the result perspective-correct rather than merely linear across the screen.
        v0 = TVarying.Scale(v0, invW0);
        v1 = TVarying.Scale(v1, invW1);
        v2 = TVarying.Scale(v2, invW2);

        var planes = new TrianglePlanes(p0, p1, p2, invW0, invW1, invW2, area, xFrom, yFrom);

        // How much varying/w moves for a one-pixel step. An edge gradient weights the vertices
        // exactly as the barycentrics do, so the same weighted sum that evaluates a pixel gives
        // the step between two of them — with weights that sum to zero rather than one.
        var gradients = new VaryingGradients<TVarying>(planes, v0, v1, v2);

        var sinks = new PixelSinks(surface, state);

        var drawn = 0;
        var behindZ = 0;

        var blockSize = BlockSize;

        // Probing records every write to one pixel in order, so it takes the plain path where each
        // pixel is offered to the frame buffer exactly once and nothing is skipped ahead of it.
        var canVectorize = Vector.IsHardwareAccelerated && !surface.IsProbing;

        // The vector constants depend on the triangle alone, so they are built once here rather
        // than once per block — and not at all for a triangle too narrow to fill a single vector.
        var vectors = canVectorize && xTo - xFrom >= Vector<float>.Count
            ? new VectorPlanes<TVarying>(planes, gradients)
            : default;

        // Blocks are aligned to the screen rather than to the triangle, so every triangle that
        // touches a pixel measures it from the same block origin: half of what makes a shared
        // edge watertight (the other half is in TrianglePlanes).
        var firstBlockX = xFrom / blockSize * blockSize;
        var firstBlockY = yFrom / blockSize * blockSize;

        // A triangle within one block has nothing to classify: there is only the one block and it
        // is about to be filled either way, so the corner test would be pure overhead. Most
        // triangles of a dense mesh land here.
        var manyBlocks =
            (xTo - 1) / blockSize != xFrom / blockSize ||
            (yTo - 1) / blockSize != yFrom / blockSize;

        for (var blockY = firstBlockY; blockY < yTo; blockY += blockSize)
        {
            var blockYFrom = System.Math.Max(blockY, yFrom);
            var blockYTo = System.Math.Min(blockY + blockSize, yTo);

            for (var blockX = firstBlockX; blockX < xTo; blockX += blockSize)
            {
                var blockXFrom = System.Math.Max(blockX, xFrom);
                var blockXTo = System.Math.Min(blockX + blockSize, xTo);

                var edges = new EdgeOrigins(planes, blockX, blockY);

                var whollyInside = false;

                if (manyBlocks)
                {
                    var corner = new BlockCorners(
                        planes, edges,
                        blockXFrom - blockX, blockXTo - 1 - blockX,
                        blockYFrom - blockY, blockYTo - 1 - blockY);

                    if (corner.Rejects)
                    {
                        continue;
                    }

                    whollyInside = corner.Contains;
                }

                // Re-evaluating exactly at each block keeps the stepping from drifting: no value
                // is ever more than a block away from one computed outright.
                var origin = new BlockOrigin<TVarying>(planes, v0, v1, v2, blockXFrom, blockYFrom);

                if (canVectorize && blockXTo - blockXFrom >= Vector<float>.Count)
                {
                    VectorBlock(
                        surface, planes, gradients, vectors, edges, origin,
                        blockX, blockY, blockXFrom, blockYFrom, blockXTo, blockYTo,
                        whollyInside, shader, state, sinks,
                        ref drawn, ref behindZ);
                }
                else
                {
                    ScalarBlock(
                        surface, planes, gradients, edges, origin,
                        blockX, blockY, blockXFrom, blockYFrom, blockXTo, blockYTo,
                        whollyInside, shader, state, sinks,
                        ref drawn, ref behindZ);
                }
            }
        }

        surface.Stats?.AddPixelCounts(drawn, behindZ);
    }

    /// <summary>
    /// Fills a block a vector of pixels at a time. Coverage, depth and the perspective reciprocal
    /// are all linear ramps, so a whole run is classified with one comparison per edge and the
    /// divide that a scanline fill owes every pixel is paid once for the run.
    /// </summary>
    private static void VectorBlock<TVarying, TShader>(
        FrameBuffer surface,
        in TrianglePlanes planes,
        in VaryingGradients<TVarying> gradients,
        in VectorPlanes<TVarying> vectors,
        in EdgeOrigins edges,
        in BlockOrigin<TVarying> origin,
        int blockX, int blockY,
        int xFrom, int yFrom, int xTo, int yTo,
        bool whollyInside,
        in TShader shader,
        in RasterState state,
        in PixelSinks sinks,
        ref int drawn,
        ref int behindZ)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var lanes = Vector<float>.Count;

        // Reading a lane straight out of a Vector<T> makes the JIT spill the whole register to the
        // stack for each access, which at several reads a pixel costs more than the vector work
        // saves. The lanes are written out once per run and read back as plain memory instead.
        Span<int> depthLanes = stackalloc int[lanes];
        Span<int> passingLanes = stackalloc int[lanes];
        Span<float> wLanes = stackalloc float[lanes];

        var rowZ = origin.Z;
        var rowW = origin.W;
        var rowV = origin.Varying;

        for (var y = yFrom; y < yTo; y++)
        {
            var dy = y - blockY;

            var rowE0 = edges.E0 + planes.Ay0 * dy;
            var rowE1 = edges.E1 + planes.Ay1 * dy;
            var rowE2 = edges.E2 + planes.Ay2 * dy;

            var z = new Vector<float>(rowZ) + vectors.LaneZ;
            var oneOverW = new Vector<float>(rowW) + vectors.LaneW;

            // The varying is wanted one pixel at a time, so it is stepped alongside the vectors
            // rather than gathered back out of them.
            var runV = rowV;

            var x = xFrom;

            for (; x <= xTo - lanes; x += lanes)
            {
                var covered = _everyLane;

                if (!whollyInside)
                {
                    // Lane by lane, the same arithmetic Row does for one pixel: the row's value plus
                    // this column's offset along the edge, so a pixel gets the same answer whichever
                    // path reaches it.
                    var dx = new Vector<float>(x - blockX) + RasterMath.LaneOffsets;

                    covered = Coverage(
                        new Vector<float>(rowE0) + dx * vectors.Ax0,
                        new Vector<float>(rowE1) + dx * vectors.Ax1,
                        new Vector<float>(rowE2) + dx * vectors.Ax2,
                        vectors.Bias0, vectors.Bias1, vectors.Bias2);
                }

                if (covered != Vector<int>.Zero)
                {
                    var depths = RasterMath.QuantizeDepths(z);

                    var passing = Vector.BitwiseAnd(covered, surface.DepthPassMask(x, y, depths));

                    // Every covered lane the depth test turned away is behind something. A mask
                    // lane is all ones or zero, so the sum of a mask is minus its lane count.
                    behindZ -= Vector.Sum(Vector.AndNot(covered, passing));

                    if (passing != Vector<int>.Zero)
                    {
                        (Vector<float>.One / oneOverW).CopyTo(wLanes);

                        depths.CopyTo(depthLanes);
                        passing.CopyTo(passingLanes);

                        var laneV = runV;

                        for (var lane = 0; lane < lanes; lane++)
                        {
                            if (passingLanes[lane] != 0)
                            {
                                RasterMath.WritePixel(
                                    surface, x + lane, y, depthLanes[lane], wLanes[lane],
                                    laneV, shader, state, sinks, ref drawn, ref behindZ);
                            }

                            laneV = TVarying.Add(laneV, gradients.Dx);
                        }
                    }
                }

                z += vectors.StrideZ;
                oneOverW += vectors.StrideW;

                runV = TVarying.Add(runV, vectors.StrideV);
            }

            if (x < xTo)
            {
                var lead = x - xFrom;

                Row(
                    surface, planes, gradients,
                    blockX, x, y, xTo,
                    rowE0, rowE1, rowE2,
                    rowZ + planes.Dzdx * lead,
                    rowW + planes.Dwdx * lead,
                    runV,
                    whollyInside, shader, state, sinks, ref drawn, ref behindZ);
            }

            rowZ += planes.Dzdy;
            rowW += planes.Dwdy;
            rowV = TVarying.Add(rowV, gradients.Dy);
        }
    }

    /// <summary>
    /// Fills a block one pixel at a time. This is what a triangle covering a handful of pixels
    /// takes, and any block narrower than a vector, so it sets up none of the vector constants a
    /// wider run would amortise them over.
    /// </summary>
    private static void ScalarBlock<TVarying, TShader>(
        FrameBuffer surface,
        in TrianglePlanes planes,
        in VaryingGradients<TVarying> gradients,
        in EdgeOrigins edges,
        in BlockOrigin<TVarying> origin,
        int blockX, int blockY,
        int xFrom, int yFrom, int xTo, int yTo,
        bool whollyInside,
        in TShader shader,
        in RasterState state,
        in PixelSinks sinks,
        ref int drawn,
        ref int behindZ)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var rowZ = origin.Z;
        var rowW = origin.W;
        var rowV = origin.Varying;

        for (var y = yFrom; y < yTo; y++)
        {
            var dy = y - blockY;

            Row(
                surface, planes, gradients,
                blockX, xFrom, y, xTo,
                edges.E0 + planes.Ay0 * dy,
                edges.E1 + planes.Ay1 * dy,
                edges.E2 + planes.Ay2 * dy,
                rowZ, rowW, rowV,
                whollyInside, shader, state, sinks, ref drawn, ref behindZ);

            rowZ += planes.Dzdy;
            rowW += planes.Dwdy;
            rowV = TVarying.Add(rowV, gradients.Dy);
        }
    }

    /// <summary>
    /// One row of pixels. Depth, 1/w and the varying are stepped by their gradients; each edge is
    /// evaluated outright from the row's value at the block origin, so that a pixel on an edge two
    /// triangles share is computed identically — up to an exact change of sign — by both.
    /// </summary>
    private static void Row<TVarying, TShader>(
        FrameBuffer surface,
        in TrianglePlanes planes,
        in VaryingGradients<TVarying> gradients,
        int blockX, int xFrom, int y, int xTo,
        float rowE0, float rowE1, float rowE2,
        float z, float oneOverW,
        TVarying varying,
        bool whollyInside,
        in TShader shader,
        in RasterState state,
        in PixelSinks sinks,
        ref int drawn,
        ref int behindZ)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var probing = surface.IsProbing;

        for (var x = xFrom; x < xTo; x++)
        {
            var dx = x - blockX;

            if (whollyInside ||
                (rowE0 + planes.Ax0 * dx >= planes.Bias0 &&
                 rowE1 + planes.Ax1 * dx >= planes.Bias1 &&
                 rowE2 + planes.Ax2 * dx >= planes.Bias2))
            {
                var depth = RasterMath.QuantizeDepth(z);

                if (probing || surface.DepthTest(x, y, depth))
                {
                    RasterMath.WritePixel(
                        surface, x, y, depth, 1f / oneOverW,
                        varying, shader, state, sinks, ref drawn, ref behindZ);
                }
                else
                {
                    behindZ++;
                }
            }

            z += planes.Dzdx;
            oneOverW += planes.Dwdx;

            varying = TVarying.Add(varying, gradients.Dx);
        }
    }

    private static readonly Vector<int> _everyLane = new(-1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<int> Coverage(
        in Vector<float> e0, in Vector<float> e1, in Vector<float> e2,
        in Vector<float> bias0, in Vector<float> bias1, in Vector<float> bias2) =>
        Vector.BitwiseAnd(
            Vector.AsVectorInt32(Vector.GreaterThanOrEqual(e0, bias0)),
            Vector.BitwiseAnd(
                Vector.AsVectorInt32(Vector.GreaterThanOrEqual(e1, bias1)),
                Vector.AsVectorInt32(Vector.GreaterThanOrEqual(e2, bias2))));

    /// <summary>
    /// The three edge functions and the depth and 1/w ramps, all as a value at one origin pixel
    /// plus a gradient per axis — every quantity the fill needs is linear in screen space, so this
    /// is the whole triangle.
    /// <para>
    /// Each edge is measured from whichever of its two endpoints is lower — smaller x, then smaller
    /// y — rather than from wherever the winding happens to start it. The neighbour across that
    /// edge traverses it the other way, so its coefficients are the exact negatives of these, and
    /// with the same endpoint as reference every value the two triangles compute for a pixel is an
    /// exact negative too, since IEEE rounding is symmetric in sign. That is what lets the top-left
    /// rule hand a pixel centre on the edge to exactly one of them: measured from different
    /// endpoints the two values would differ in their last bits, and a pixel could land on the
    /// inside of both or of neither.
    /// </para>
    /// </summary>
    private readonly struct TrianglePlanes
    {
        public readonly float Ax0, Ay0, Cx0, Cy0, Bias0;
        public readonly float Ax1, Ay1, Cx1, Cy1, Bias1;
        public readonly float Ax2, Ay2, Cx2, Cy2, Bias2;

        public readonly float Z, Dzdx, Dzdy;
        public readonly float W, Dwdx, Dwdy;

        public readonly float InverseArea;

        public readonly int OriginX, OriginY;

        public TrianglePlanes(
            in Vector3 p0, in Vector3 p1, in Vector3 p2,
            float invW0, float invW1, float invW2,
            float area,
            int originX, int originY)
        {
            // Edge i faces vertex i, so its value there is the whole area and zero along itself —
            // dividing by the area turns it straight into that vertex's barycentric weight.
            Ax0 = p1.Y - p2.Y;
            Ay0 = p2.X - p1.X;
            (Cx0, Cy0) = Lower(p1, p2);

            Ax1 = p2.Y - p0.Y;
            Ay1 = p0.X - p2.X;
            (Cx1, Cy1) = Lower(p2, p0);

            Ax2 = p0.Y - p1.Y;
            Ay2 = p1.X - p0.X;
            (Cx2, Cy2) = Lower(p0, p1);

            Bias0 = InclusionThreshold(Ax0, Ay0);
            Bias1 = InclusionThreshold(Ax1, Ay1);
            Bias2 = InclusionThreshold(Ax2, Ay2);

            OriginX = originX;
            OriginY = originY;

            var x = originX + 0.5f;
            var y = originY + 0.5f;

            var e0 = E0At(x, y);
            var e1 = E1At(x, y);
            var e2 = E2At(x, y);

            InverseArea = 1f / area;

            Z = (e0 * p0.Z + e1 * p1.Z + e2 * p2.Z) * InverseArea;
            Dzdx = (Ax0 * p0.Z + Ax1 * p1.Z + Ax2 * p2.Z) * InverseArea;
            Dzdy = (Ay0 * p0.Z + Ay1 * p1.Z + Ay2 * p2.Z) * InverseArea;

            W = (e0 * invW0 + e1 * invW1 + e2 * invW2) * InverseArea;
            Dwdx = (Ax0 * invW0 + Ax1 * invW1 + Ax2 * invW2) * InverseArea;
            Dwdy = (Ay0 * invW0 + Ay1 * invW1 + Ay2 * invW2) * InverseArea;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float E0At(float x, float y) => Ax0 * (x - Cx0) + Ay0 * (y - Cy0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float E1At(float x, float y) => Ax1 * (x - Cx1) + Ay1 * (y - Cy1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float E2At(float x, float y) => Ax2 * (x - Cx2) + Ay2 * (y - Cy2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (float X, float Y) Lower(in Vector3 a, in Vector3 b) =>
            a.X < b.X || (a.X == b.X && a.Y < b.Y) ? (a.X, a.Y) : (b.X, b.Y);

        /// <summary>
        /// The value an edge function has to reach for a pixel sitting exactly on it to count —
        /// the top-left rule. A pixel centre landing precisely on a shared edge is inside both
        /// triangles that meet there, and filling it twice shows up as a double-blended seam along
        /// every diagonal and as overdraw the scene never asked for. Giving each edge to exactly
        /// one of its two triangles settles it: an edge is kept if the triangle lies to its right
        /// (a left edge), or, when it is horizontal, below it (a top edge), and is otherwise left
        /// to the neighbour.
        /// <para>
        /// (<see cref="Ax0"/>, <see cref="Ay0"/>) is the edge's inward normal, so its x component
        /// decides left and, on a horizontal edge, its y component decides top. Excluding an edge
        /// means requiring the function to be greater than zero rather than at least zero, and for
        /// IEEE floats those are the same test against <see cref="float.Epsilon"/> — no tolerance
        /// to pick, and nothing that drifts with the size of the triangle.
        /// </para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InclusionThreshold(float ax, float ay) =>
            ax > 0f || (ax == 0f && ay > 0f) ? 0f : float.Epsilon;
    }

    /// <summary>How much varying/w moves for a one-pixel step in each direction.</summary>
    private readonly struct VaryingGradients<TVarying>
        where TVarying : struct, IVarying<TVarying>
    {
        public readonly TVarying Dx;
        public readonly TVarying Dy;

        public VaryingGradients(
            in TrianglePlanes planes, in TVarying v0, in TVarying v1, in TVarying v2)
        {
            Dx = TVarying.Combine(
                v0, v1, v2,
                planes.Ax0 * planes.InverseArea,
                planes.Ax1 * planes.InverseArea,
                planes.Ax2 * planes.InverseArea);

            Dy = TVarying.Combine(
                v0, v1, v2,
                planes.Ay0 * planes.InverseArea,
                planes.Ay1 * planes.InverseArea,
                planes.Ay2 * planes.InverseArea);
        }
    }

    /// <summary>
    /// The vector fill's constants: each edge's x gradient and the inclusion thresholds broadcast
    /// per lane, and the depth and 1/w ramps' offsets across one vector of lanes and their stride
    /// for advancing a whole vector. All of it depends on the triangle alone, so it is built once
    /// per triangle rather than once per block.
    /// </summary>
    private readonly struct VectorPlanes<TVarying>
        where TVarying : struct, IVarying<TVarying>
    {
        public readonly Vector<float> Ax0, Ax1, Ax2;
        public readonly Vector<float> Bias0, Bias1, Bias2;
        public readonly Vector<float> LaneZ, LaneW;
        public readonly Vector<float> StrideZ, StrideW;
        public readonly TVarying StrideV;

        public VectorPlanes(in TrianglePlanes planes, in VaryingGradients<TVarying> gradients)
        {
            var lanes = Vector<float>.Count;

            Ax0 = new Vector<float>(planes.Ax0);
            Ax1 = new Vector<float>(planes.Ax1);
            Ax2 = new Vector<float>(planes.Ax2);

            Bias0 = new Vector<float>(planes.Bias0);
            Bias1 = new Vector<float>(planes.Bias1);
            Bias2 = new Vector<float>(planes.Bias2);

            // A vector carries one lane per pixel, so advancing it moves on by its whole width.
            LaneZ = RasterMath.LaneOffsets * planes.Dzdx;
            LaneW = RasterMath.LaneOffsets * planes.Dwdx;

            StrideZ = new Vector<float>(planes.Dzdx * lanes);
            StrideW = new Vector<float>(planes.Dwdx * lanes);

            StrideV = TVarying.Scale(gradients.Dx, lanes);
        }
    }

    /// <summary>
    /// The three edge functions at a block's origin pixel centre — the block's, not the triangle's,
    /// so every triangle touching the block starts its pixels from the same value.
    /// </summary>
    private readonly struct EdgeOrigins
    {
        public readonly float E0, E1, E2;

        public EdgeOrigins(in TrianglePlanes planes, int blockX, int blockY)
        {
            var x = blockX + 0.5f;
            var y = blockY + 0.5f;

            E0 = planes.E0At(x, y);
            E1 = planes.E1At(x, y);
            E2 = planes.E2At(x, y);
        }
    }

    /// <summary>The depth, 1/w and varying ramps evaluated outright at a block's first pixel centre.</summary>
    private readonly struct BlockOrigin<TVarying>
        where TVarying : struct, IVarying<TVarying>
    {
        public readonly float Z, W;
        public readonly TVarying Varying;

        public BlockOrigin(
            in TrianglePlanes planes,
            in TVarying v0, in TVarying v1, in TVarying v2,
            int xFrom, int yFrom)
        {
            var dx = xFrom - planes.OriginX;
            var dy = yFrom - planes.OriginY;

            Z = planes.Z + planes.Dzdx * dx + planes.Dzdy * dy;
            W = planes.W + planes.Dwdx * dx + planes.Dwdy * dy;

            var x = xFrom + 0.5f;
            var y = yFrom + 0.5f;

            Varying = TVarying.Combine(
                v0, v1, v2,
                planes.E0At(x, y) * planes.InverseArea,
                planes.E1At(x, y) * planes.InverseArea,
                planes.E2At(x, y) * planes.InverseArea);
        }
    }

    /// <summary>
    /// Classifies a block against all three edges at once. Each edge is linear, so its extremes
    /// over the block are at two of the corners and the whole block can be settled without looking
    /// at a single pixel.
    /// </summary>
    private readonly ref struct BlockCorners
    {
        public readonly bool Rejects;
        public readonly bool Contains;

        public BlockCorners(
            in TrianglePlanes planes, in EdgeOrigins edges,
            int dxFrom, int dxTo, int dyFrom, int dyTo)
        {
            Extremes(edges.E0, planes.Ax0, planes.Ay0, dxFrom, dxTo, dyFrom, dyTo, out var lowest0, out var highest0);
            Extremes(edges.E1, planes.Ax1, planes.Ay1, dxFrom, dxTo, dyFrom, dyTo, out var lowest1, out var highest1);
            Extremes(edges.E2, planes.Ax2, planes.Ay2, dxFrom, dxTo, dyFrom, dyTo, out var lowest2, out var highest2);

            Rejects = highest0 < planes.Bias0 || highest1 < planes.Bias1 || highest2 < planes.Bias2;
            Contains = lowest0 >= planes.Bias0 && lowest1 >= planes.Bias1 && lowest2 >= planes.Bias2;
        }

        /// <summary>
        /// The four corner pixels, evaluated exactly as <c>Row</c> evaluates any pixel — the row's
        /// value first, then the column's offset. Rounding is monotone, so the block's extremes are
        /// among those four, and a block settled here agrees with every pixel it would otherwise
        /// have tested one by one.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Extremes(
            float origin, float ax, float ay,
            int dxFrom, int dxTo, int dyFrom, int dyTo,
            out float lowest, out float highest)
        {
            var near = origin + ay * dyFrom;
            var far = origin + ay * dyTo;

            var nearFrom = near + ax * dxFrom;
            var nearTo = near + ax * dxTo;
            var farFrom = far + ax * dxFrom;
            var farTo = far + ax * dxTo;

            lowest = MathF.Min(MathF.Min(nearFrom, nearTo), MathF.Min(farFrom, farTo));
            highest = MathF.Max(MathF.Max(nearFrom, nearTo), MathF.Max(farFrom, farTo));
        }
    }
}
