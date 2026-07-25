using SoftEngine.Core.Buffers;
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

        // Cross2D tells us which side the middle vertex sits on, which decides
        // whether the long edge p0->p2 is the left or the right boundary.
        if (Cross2D(p0, p1, p2) > 0)
        {
            //  p0
            //    p1        long edge on the left
            //  p2
            HalfTriangle(surface, yStart, yMiddle,
                new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), new Edge<TVarying>(p0, p1, v0, v1, invW0, invW1), shader, state, tile);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), new Edge<TVarying>(p1, p2, v1, v2, invW1, invW2), shader, state, tile);
        }
        else
        {
            //    p0
            //  p1          long edge on the right
            //    p2
            HalfTriangle(surface, yStart, yMiddle,
                new Edge<TVarying>(p0, p1, v0, v1, invW0, invW1), new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), shader, state, tile);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p1, p2, v1, v2, invW1, invW2), new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), shader, state, tile);
        }
    }

    /// <summary>Index of the first pixel whose center (index + 0.5) lies at or after <paramref name="coordinate"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FirstCenterAtOrAfter(float coordinate) => (int)MathF.Ceiling(coordinate - 0.5f);

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
        in ScreenTile tile)
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

            Scanline(surface, y, sx, ex, sz, ez, sw, ew, sv, ev, shader, state, xFloor, xLimit);
        }
    }

    private static void Scanline<TVarying, TShader>(
        FrameBuffer surface, int y,
        float sx, float ex, float sz, float ez, float sw, float ew,
        in TVarying sv, in TVarying ev,
        in TShader shader,
        in RasterState state,
        int xFloor, int xLimit)
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

        // Pixel stats are accumulated locally and flushed once per scanline, so parallel
        // tiles don't contend on the shared counters at every pixel.
        var drawn = 0;
        var behindZ = 0;

        // While probing, every rejected write must still be shaded so the pixel history
        // can show the colour the depth test discarded; otherwise pixels that fail the
        // depth test skip interpolation and shading entirely.
        var probing = surface.IsProbing;

        // Runs of pixels that are entirely behind the z-buffer — the common case in a scene
        // with depth complexity — are rejected a vector at a time and never shaded. The
        // block test only runs where a whole vector fits inside the span.
        var blockEnd = Vector.IsHardwareAccelerated && !probing
            ? xEnd - Vector<int>.Count
            : int.MinValue;

        for (var x = xStart; x < xEnd; x++)
        {
            if (x <= blockEnd && surface.NoDepthPasses(x, y, BlockDepths(zBase, dz, x)))
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
            var varying = TVarying.Scale(TVarying.Lerp(sv, ev, t), 1f / oneOverW);

            var color = shader.Shade(varying);

            // 1/w is already at hand, and w is the view-space depth fog runs on.
            if (state.HasFog)
            {
                color = state.ApplyFog(color, 1f / oneOverW);
            }

            var written = state.IsOpaque
                ? surface.PutPixel(x, y, depth, color)
                : surface.PutPixelBlend(x, y, depth, color, state.Alpha);

            if (written)
            {
                drawn++;
            }
            else
            {
                behindZ++;
            }
        }

        surface.Stats?.AddPixelCounts(drawn, behindZ);
    }

    /// <summary>Signed area of the triangle in screen space; the sign gives the winding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cross2D(in Vector3 p0, in Vector3 p1, in Vector3 p2) =>
        (p1.X - p0.X) * (p2.Y - p1.Y) - (p1.Y - p0.Y) * (p2.X - p1.X);
}
