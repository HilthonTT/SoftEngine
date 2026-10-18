using SoftEngine.Core.Buffers;
using SoftEngine.Core.Rasterization.Shaders;
using SoftEngine.Core.Rasterization.Varyings;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

public static class ScanlineRasterizer
{
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

        var yStart = System.Math.Max(RasterMath.FirstCenterAtOrAfter(p0.Y), System.Math.Max(tile.YFrom, 0));
        var yEnd = System.Math.Min(RasterMath.FirstCenterAtOrAfter(p2.Y), System.Math.Min(tile.YTo, surface.Height));

        if (yStart >= yEnd)
        {
            return;
        }

        v0 = TVarying.Scale(v0, invW0);
        v1 = TVarying.Scale(v1, invW1);
        v2 = TVarying.Scale(v2, invW2);

        var yMiddle = System.Math.Clamp(RasterMath.FirstCenterAtOrAfter(p1.Y), yStart, yEnd);

        var drawn = 0;
        var behindZ = 0;

        if (Cross2D(p0, p1, p2) > 0)
        {
            HalfTriangle(surface, yStart, yMiddle,
                new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), new Edge<TVarying>(p0, p1, v0, v1, invW0, invW1), shader, state, tile, ref drawn, ref behindZ);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), new Edge<TVarying>(p1, p2, v1, v2, invW1, invW2), shader, state, tile, ref drawn, ref behindZ);
        }
        else
        {
            HalfTriangle(surface, yStart, yMiddle,
                new Edge<TVarying>(p0, p1, v0, v1, invW0, invW1), new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), shader, state, tile, ref drawn, ref behindZ);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p1, p2, v1, v2, invW1, invW2), new Edge<TVarying>(p0, p2, v0, v2, invW0, invW2), shader, state, tile, ref drawn, ref behindZ);
        }

        surface.Stats?.AddPixelCounts(drawn, behindZ);
    }

    public static bool VectorizedSpans { get; set; } = Vector.IsHardwareAccelerated;

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
        var xStart = System.Math.Max(RasterMath.FirstCenterAtOrAfter(sx), xFloor);
        var xEnd = System.Math.Min(RasterMath.FirstCenterAtOrAfter(ex), xLimit);

        if (xStart >= xEnd)
        {
            return;
        }

        var invSpan = 1f / (ex - sx);

        var dz = (ez - sz) * invSpan;

        var zBase = sz + (0.5f - sx) * dz;

        var probing = surface.IsProbing;

        var sinks = new PixelSinks(surface, state);

        var x = xStart;

        if (VectorizedSpans && Vector.IsHardwareAccelerated && !probing &&
            xEnd - xStart >= Vector<float>.Count)
        {
            x = VectorSpan(
                surface, y, xStart, xEnd, sx, sw, ew, sv, ev, invSpan, dz, zBase,
                shader, state, sinks,
                ref drawn, ref behindZ);
        }

        var blockEnd = Vector.IsHardwareAccelerated && !probing
            ? xEnd - Vector<int>.Count
            : int.MinValue;

        for (; x < xEnd; x++)
        {
            if (x <= blockEnd && surface.DepthPassMask(x, y, RasterMath.DepthRun(zBase, dz, x)) == Vector<int>.Zero)
            {
                behindZ += Vector<int>.Count;
                x += Vector<int>.Count - 1;
                continue;
            }

            var depth = RasterMath.QuantizeDepth(zBase + x * dz);

            if (!probing && !surface.DepthTest(x, y, depth))
            {
                behindZ++;
                continue;
            }

            var t = (x + 0.5f - sx) * invSpan;

            var oneOverW = float.Lerp(sw, ew, t);
            var w = 1f / oneOverW;

            RasterMath.WritePixel(
                surface, x, y, depth, w,
                TVarying.Lerp(sv, ev, t), shader, state, sinks, ref drawn, ref behindZ);
        }
    }

    private static int VectorSpan<TVarying, TShader>(
        FrameBuffer surface, int y, int xStart, int xEnd,
        float sx, float sw, float ew,
        in TVarying sv, in TVarying ev,
        float invSpan, float dz, float zBase,
        in TShader shader,
        in RasterState state,
        in PixelSinks sinks,
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
            var depths = RasterMath.DepthRun(zBase, dz, x);
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
                    oneOverW[lane] = 1f;
                    continue;
                }

                var t = (x + lane + 0.5f - sx) * invSpan;

                parameters[lane] = t;
                oneOverW[lane] = float.Lerp(sw, ew, t);
            }

            (Vector<float>.One / new Vector<float>(oneOverW)).CopyTo(w);

            for (var lane = 0; lane < lanes; lane++)
            {
                if (passes[lane] == 0)
                {
                    behindZ++;
                    continue;
                }

                RasterMath.WritePixel(
                    surface, x + lane, y, depths[lane], w[lane],
                    TVarying.Lerp(sv, ev, parameters[lane]), shader, state, sinks, ref drawn, ref behindZ);
            }
        }

        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cross2D(in Vector3 p0, in Vector3 p1, in Vector3 p2) =>
        (p1.X - p0.X) * (p2.Y - p1.Y) - (p1.Y - p0.Y) * (p2.X - p1.X);
}
