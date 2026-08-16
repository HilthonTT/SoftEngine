using SoftEngine.Core.Buffers;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Shared scanline triangle fill. Sorts by screen Y, splits at the middle vertex,
/// walks two half-triangles, and interpolates depth plus an arbitrary varying payload.
/// The only thing painters supply is the varying type and the shader.
///
/// Pixels are sampled at their centers over half-open [start, end) spans, so two
/// triangles sharing an edge cover every pixel along it exactly once — no cracks and
/// no double-drawn seams. Varyings are perspective-correct: the caller passes 1/w per
/// vertex, varying/w and 1/w are interpolated linearly in screen space, and the true
/// varying is recovered per pixel.
/// </summary>
public static class ScanlineRasterizer
{
    /// <summary>
    /// Fills a triangle given screen-space positions (X, Y in pixels, Z in depth units),
    /// the 1/w of each vertex in clip space, and the vertex varyings.
    /// Positions need not be pre-sorted.
    /// </summary>
    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        float invW0, float invW1, float invW2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
        => Fill(surface, p0, p1, p2, invW0, invW1, invW2, v0, v1, v2, shader, default, ScreenTile.Full);

    /// <summary>
    /// Same as the tile-less overload, but only writes pixels owned by <paramref name="tile"/> —
    /// the unit of work for parallel rasterization.
    /// </summary>
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

    /// <summary>
    /// Full form: <paramref name="state"/> adds fog and alpha blending, applied per pixel
    /// after the shader. The default state is opaque with no fog.
    /// </summary>
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
        if (p0.Y > p1.Y)
        {
            (p0, p1) = (p1, p0); (v0, v1) = (v1, v0); (invW0, invW1) = (invW1, invW0);
        }
        if (p1.Y > p2.Y)
        {
            (p1, p2) = (p2, p1); (v1, v2) = (v2, v1); (invW1, invW2) = (invW2, invW1);
        }
        if (p0.Y > p1.Y)
        {
            (p0, p1) = (p1, p0); (v0, v1) = (v1, v0); (invW0, invW1) = (invW1, invW0);
        }

        var yStart = System.Math.Max(FirstCenterAtOrAfter(p0.Y), System.Math.Max(tile.YFrom, 0));
        var yEnd = System.Math.Min(FirstCenterAtOrAfter(p2.Y), System.Math.Min(tile.YTo, surface.Height)); // exclusive

        if (yStart >= yEnd)
        {
            return;
        }

        // Pre-divide the varyings by w; in this form they interpolate linearly in screen space.
        v0 = TVarying.Scale(v0, invW0);
        v1 = TVarying.Scale(v1, invW1);
        v2 = TVarying.Scale(v2, invW2);

        var yMiddle = System.Math.Clamp(FirstCenterAtOrAfter(p1.Y), yStart, yEnd);

        // Pixel counts are accumulated across the whole triangle and handed over once, rather
        // than once per scanline. The counters are shared by every tile the fill phase is
        // running in parallel, so what a flush costs is not the addition but the cache line
        // it takes away from the other workers — and a triangle a few pixels tall used to
        // pay that several times over for a handful of pixels.
        var drawn = 0;
        var behindZ = 0;

        // Cross2D tells us which side the middle vertex sits on, which decides
        // whether the long edge p0->p2 is the left or the right boundary.
        if (Cross2D(p0, p1, p2) > 0)
        {
            //  p0
            //    p1        long edge on the left
            //  p2
            HalfTriangle(surface, yStart, yMiddle,
                new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), new Edge<TVarying>(p0, p1, v0, v1, invW0, invW1), shader, state, tile, ref drawn, ref behindZ);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), new Edge<TVarying>(p1, p2, v1, v2, invW1, invW2), shader, state, tile, ref drawn, ref behindZ);
        }
        else
        {
            //    p0
            //  p1          long edge on the right
            //    p2
            HalfTriangle(surface, yStart, yMiddle,
                new Edge<TVarying>(p0, p1, v0, v1, invW0, invW1), new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), shader, state, tile, ref drawn, ref behindZ);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p1, p2, v1, v2, invW1, invW2), new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), shader, state, tile, ref drawn, ref behindZ);
        }

        surface.Stats?.AddPixelCounts(drawn, behindZ);
    }

    /// <summary>Index of the first pixel whose center (index + 0.5) lies at or after <paramref name="coordinate"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FirstCenterAtOrAfter(float coordinate) => (int)MathF.Ceiling(coordinate - 0.5f);

    /// <summary>
    /// Whether spans are filled a vector of pixels at a time (see <see cref="Scanline"/>).
    /// On by default wherever the hardware has vectors to do it with.
    ///
    /// <para>
    /// It is settable because the claim the block path makes is that it draws the same image
    /// the scalar one does, and a claim like that is worth testing rather than asserting: the
    /// test renders a scene both ways in one process and compares the frames pixel for pixel.
    /// A diagnostic seam, not a rendering option — nothing in the pipeline reads it, and the
    /// front-end offers no way to change it.
    /// </para>
    /// </summary>
    public static bool VectorizedSpans { get; set; } = Vector.IsHardwareAccelerated;

    // Lane offsets 0, 1, 2, … so one vector can hold a run of consecutive pixels.
    private static readonly Vector<float> _laneOffsets = CreateLaneOffsets();

    // The largest float below the depth buffer's resolution. Converting the resolution
    // itself would round up to 2^31, which no longer fits an int.
    private static readonly float _maxDepth = MathF.BitDecrement(FrameBuffer.DepthResolution);

    private static Vector<float> CreateLaneOffsets()
    {
        Span<float> lanes = stackalloc float[Vector<float>.Count];

        for (var i = 0; i < lanes.Length; i++)
        {
            lanes[i] = i;
        }

        return new Vector<float>(lanes);
    }

    /// <summary>
    /// Quantized depth of the run of pixels starting at <paramref name="x"/>, one lane each.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<int> BlockDepths(float zBase, float dz, int x)
    {
        var z = new Vector<float>(zBase) + (new Vector<float>(x) + _laneOffsets) * dz;

        return Vector.ConvertToInt32(
            Vector.Min(Vector.Max(z, Vector<float>.Zero), new Vector<float>(_maxDepth)));
    }

    /// <summary>
    /// Device depth as the buffer stores it. Clamping is what keeps this and
    /// <see cref="BlockDepths"/> in agreement: a float-to-int conversion outside the int
    /// range is not defined to produce the same answer in both.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int QuantizeDepth(float z) => (int)System.Math.Clamp(z, 0f, _maxDepth);

    private static void HalfTriangle<TVarying, TShader>(
        FrameBuffer surface, int yStart, int yEnd,
        in Edge<TVarying> left, in Edge<TVarying> right,
        in TShader shader,
        in RasterState state,
        in ScreenTile tile,
        ref int drawn,
        ref int behindZ)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var invLeft = left.InvHeight;
        var invRight = right.InvHeight;

        var xLimit = System.Math.Min(tile.XTo, surface.Width);
        var xFloor = System.Math.Max(tile.XFrom, 0);

        for (var y = yStart; y < yEnd; y++)
        {
            var yCenter = y + 0.5f;

            var gl = System.Math.Clamp((yCenter - left.A.Y) * invLeft, 0f, 1f);
            var gr = System.Math.Clamp((yCenter - right.A.Y) * invRight, 0f, 1f);

            var sx = float.Lerp(left.A.X, left.B.X, gl);
            var ex = float.Lerp(right.A.X, right.B.X, gr);

            if (sx >= ex)
            {
                continue;
            }

            var sz = float.Lerp(left.A.Z, left.B.Z, gl);
            var ez = float.Lerp(right.A.Z, right.B.Z, gr);

            var sw = float.Lerp(left.WA, left.WB, gl);
            var ew = float.Lerp(right.WA, right.WB, gr);

            var sv = TVarying.Lerp(left.VA, left.VB, gl);
            var ev = TVarying.Lerp(right.VA, right.VB, gr);

            Scanline(surface, y, sx, ex, sz, ez, sw, ew, sv, ev, shader, state, xFloor, xLimit, ref drawn, ref behindZ);
        }
    }

    private static void Scanline<TVarying, TShader>(
        FrameBuffer surface, int y,
        float sx, float ex, float sz, float ez, float sw, float ew,
        in TVarying sv, in TVarying ev,
        in TShader shader,
        in RasterState state,
        int xFloor, int xLimit,
        ref int drawn,
        ref int behindZ)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var xStart = System.Math.Max(FirstCenterAtOrAfter(sx), xFloor);
        var xEnd = System.Math.Min(FirstCenterAtOrAfter(ex), xLimit); // exclusive

        if (xStart >= xEnd)
        {
            return;
        }

        var invSpan = 1f / (ex - sx);

        var dz = (ez - sz) * invSpan;

        // Depth as an affine function of x rather than a running sum: the value at a pixel
        // no longer depends on how many pixels came before it, which is what lets a whole
        // run of them be tested at once below and still agree with this loop exactly.
        var zBase = sz + (0.5f - sx) * dz;

        // While probing, every rejected write must still be shaded so the pixel history
        // can show the colour the depth test discarded; otherwise pixels that fail the
        // depth test skip interpolation and shading entirely. The block path below rejects
        // without shading by design, so probing stays on the scalar one.
        var probing = surface.IsProbing;

        // Hoisted out of the pixel loops: with the mip-level view closed — which is every
        // frame but the ones someone is looking at it — this is one predictable branch per
        // drawn pixel against a local, and no call.
        var recordMips = surface.IsRecordingMipLevels;
        var mipLevel = state.MipLevel;

        // Hoisted for the same reason, and separately: the mip-level view is open on the
        // frames somebody is looking at it, while reflectance is recorded on every frame the
        // reflection pass is enabled. They are never on together by accident.
        var recordReflectance = surface.IsRecordingReflectance;
        var reflectance = state.PackedReflectance;

        var x = xStart;

        // A span shorter than one vector cannot fill a block, and asking costs a call and a
        // frame's worth of stack. Worth guarding rather than letting the loop decline to run:
        // a dense model is mostly triangles a few pixels across, so this is the common span,
        // not the edge case.
        if (VectorizedSpans && Vector.IsHardwareAccelerated && !probing &&
            xEnd - xStart >= Vector<float>.Count)
        {
            x = VectorSpan(
                surface, y, xStart, xEnd, sx, sw, ew, sv, ev, invSpan, dz, zBase,
                shader, state, recordMips, mipLevel, recordReflectance, reflectance,
                ref drawn, ref behindZ);
        }

        // Runs entirely behind the z-buffer are rejected a vector at a time here too, so that
        // turning the block path off leaves the fill exactly as it was before there was one —
        // scalar interpolation over a vectorized rejection — rather than something slower than
        // either. That is what makes `--compare spans` a measurement of this change and not of
        // an optimization it removed. With the block path on, the tail is shorter than a vector
        // by construction and this never fires.
        var blockEnd = Vector.IsHardwareAccelerated && !probing
            ? xEnd - Vector<int>.Count
            : int.MinValue;

        // The tail — the pixels left over once no whole vector fits — and the whole span
        // when the block path is off or a probe is recording.
        for (; x < xEnd; x++)
        {
            if (x <= blockEnd && surface.DepthPassMask(x, y, BlockDepths(zBase, dz, x)) == Vector<int>.Zero)
            {
                behindZ += Vector<int>.Count;
                x += Vector<int>.Count - 1;
                continue;
            }

            var depth = QuantizeDepth(zBase + x * dz);

            if (!probing && !surface.DepthTest(x, y, depth))
            {
                behindZ++;
                continue;
            }

            var t = (x + 0.5f - sx) * invSpan;

            // Recover the perspective-correct varying: (varying/w) / (1/w).
            var oneOverW = float.Lerp(sw, ew, t);
            var w = 1f / oneOverW;

            // Written out here rather than called, and the duplication is deliberate. This is
            // the loop every triangle too small to fill a vector spends its whole life in —
            // most of them, in a dense model — and behind a call it measured about a tenth
            // slower than it does inline.
            var varying = TVarying.Scale(TVarying.Lerp(sv, ev, t), w);

            // Folded away entirely for every shader that cuts nothing out — HasAlphaTest is a
            // constant per instantiation, not a field. A rejected pixel leaves the depth
            // buffer alone as well as the colour: a cutout leaf must not occlude what is
            // behind the hole it made.
            if (TShader.HasAlphaTest && !shader.IsCovered(varying))
            {
                continue;
            }

            var color = shader.Shade(varying);

            if (state.HasFog)
            {
                color = state.ApplyFog(color, w);
            }

            var written = state.IsOpaque
                ? surface.PutPixel(x, y, depth, color)
                : surface.PutPixelBlend(x, y, depth, color, state.Alpha);

            if (written)
            {
                drawn++;

                if (recordMips)
                {
                    surface.RecordMipLevel(x, y, mipLevel);
                }

                if (recordReflectance)
                {
                    surface.RecordReflectance(x, y, reflectance);
                }
            }
            else
            {
                behindZ++;
            }
        }
    }

    /// <summary>
    /// Fills as much of a span as whole vectors of pixels cover, and returns the x the tail
    /// resumes at.
    ///
    /// <para>
    /// Three things happen per block that the scalar loop pays for per pixel. Depth is
    /// computed for the whole run at once, as an affine function of x rather than a running
    /// sum, so a lane's value does not depend on how many pixels preceded it. One load of the
    /// z-buffer answers the depth test for every lane: a run entirely behind what is already
    /// drawn — the common case wherever a scene has depth complexity — is dropped without
    /// shading a pixel of it, and a run with survivors tells each of them it passed instead of
    /// making it ask. And the perspective divide, the one genuinely expensive arithmetic
    /// operation in the loop, is done for eight pixels in the time one costs.
    /// </para>
    ///
    /// <para>
    /// What is <em>not</em> vectorized is the interpolation either side of that divide.
    /// <see cref="float.Lerp"/> contracts its multiply and add into an FMA, and no arrangement
    /// of vector multiplies and adds reproduces its result bit for bit — measurably so, on a
    /// seventh of random inputs. Since the varyings must be interpolated per lane anyway, the
    /// two stay together on the scalar path, where they produce exactly the value they always
    /// did. Vector division carries no such caveat: IEEE-754 division is correctly rounded, so
    /// the packed and scalar forms agree exactly, which is why this is the operation worth
    /// lifting out.
    /// </para>
    /// </summary>
    private static int VectorSpan<TVarying, TShader>(
        FrameBuffer surface, int y, int xStart, int xEnd,
        float sx, float sw, float ew,
        in TVarying sv, in TVarying ev,
        float invSpan, float dz, float zBase,
        in TShader shader,
        in RasterState state,
        bool recordMips,
        int mipLevel,
        bool recordReflectance,
        uint reflectance,
        ref int drawn,
        ref int behindZ)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {
        var lanes = Vector<float>.Count;

        Span<float> parameters = stackalloc float[lanes];
        Span<float> oneOverW = stackalloc float[lanes];
        Span<float> w = stackalloc float[lanes];

        var x = xStart;

        for (; x <= xEnd - lanes; x += lanes)
        {
            var depths = BlockDepths(zBase, dz, x);
            var passes = surface.DepthPassMask(x, y, depths);

            if (passes == Vector<int>.Zero)
            {
                behindZ += lanes;
                continue;
            }

            for (var lane = 0; lane < lanes; lane++)
            {
                if (passes[lane] == 0)
                {
                    // Nothing will read this lane's result. It is set to a harmless value
                    // rather than left alone because the divide below takes the whole vector,
                    // and a stale 1/w from an earlier block could be a zero or a denormal —
                    // arithmetic nobody wants to pay for on a result nobody wants.
                    oneOverW[lane] = 1f;
                    continue;
                }

                // (x + lane) is exact in a float at any screen coordinate, so this is the
                // same parameter the scalar loop computes at the same pixel.
                var t = (x + lane + 0.5f - sx) * invSpan;

                parameters[lane] = t;
                oneOverW[lane] = float.Lerp(sw, ew, t);
            }

            // One divide for the block however many lanes survived. It is the whole reason
            // the lanes are gathered up like this: a packed divide costs about what a scalar
            // one does, so a block with eight survivors pays for one where it used to pay
            // for eight, and a block with one survivor pays no more than it did before.
            (Vector<float>.One / new Vector<float>(oneOverW)).CopyTo(w);

            for (var lane = 0; lane < lanes; lane++)
            {
                if (passes[lane] == 0)
                {
                    behindZ++;
                    continue;
                }

                var lanePosition = w[lane];

                // The same body the tail loop runs, for the same reason it is written out
                // there: behind a call, the shader stops being inlined into the loop that
                // drives it and the whole fill pays for it.
                var varying = TVarying.Scale(TVarying.Lerp(sv, ev, parameters[lane]), lanePosition);

                // Per lane rather than per block: the mask is a picture, and a run of eight
                // pixels across a leaf's edge is exactly the run where some are in and some
                // are out. Rejecting the whole block would cut the silhouette to a
                // vector-wide staircase and disagree with the scalar tail beside it.
                if (TShader.HasAlphaTest && !shader.IsCovered(varying))
                {
                    continue;
                }

                var color = shader.Shade(varying);

                if (state.HasFog)
                {
                    color = state.ApplyFog(color, lanePosition);
                }

                var written = state.IsOpaque
                    ? surface.PutPixel(x + lane, y, depths[lane], color)
                    : surface.PutPixelBlend(x + lane, y, depths[lane], color, state.Alpha);

                if (written)
                {
                    drawn++;

                    if (recordMips)
                    {
                        surface.RecordMipLevel(x + lane, y, mipLevel);
                    }

                    if (recordReflectance)
                    {
                        surface.RecordReflectance(x + lane, y, reflectance);
                    }
                }
                else
                {
                    behindZ++;
                }
            }
        }

        return x;
    }

    /// <summary>Signed area of the triangle in screen space; the sign gives the winding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cross2D(in Vector3 p0, in Vector3 p1, in Vector3 p2) =>
        (p1.X - p0.X) * (p2.Y - p1.Y) - (p1.Y - p0.Y) * (p2.X - p1.X);
}
