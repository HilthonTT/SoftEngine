using SoftEngine.Core.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Rasterization;

/// <summary>
/// Shared scanline triangle fill. Sorts by screen Y, splits at the middle vertex,
/// walks two half-triangles, and interpolates depth plus an arbitrary varying payload.
/// The only thing painters supply is the varying type and the shader.
/// </summary>
public static class ScanlineRasterizer
{

    /// <summary>
    /// Fills a triangle given screen-space positions (X, Y in pixels, Z in depth units)
    /// and their varyings. Positions need not be pre-sorted.
    /// </summary>
    public static void Fill<TVarying, TShader>(
        FrameBuffer surface,
        Vector3 p0, Vector3 p1, Vector3 p2,
        TVarying v0, TVarying v1, TVarying v2,
        in TShader shader)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {

        if (p0.Y > p1.Y)
        { 
            (p0, p1) = (p1, p0); (v0, v1) = (v1, v0);
        }
        if (p1.Y > p2.Y) 
        {
            (p1, p2) = (p2, p1); (v1, v2) = (v2, v1);
        }
        if (p0.Y > p1.Y) 
        {
            (p0, p1) = (p1, p0); (v0, v1) = (v1, v0);
        }

        var yStart = (int)MathF.Max(p0.Y, 0);
        var yEnd = (int)MathF.Min(p2.Y, surface.Height - 1);

        if (yStart > yEnd)
        {
            return;
        }

        var yMiddle = System.Math.Clamp((int)p1.Y, yStart, yEnd);

        // Cross2D tells us which side the middle vertex sits on, which decides
        // whether the long edge p0->p2 is the left or the right boundary.
        if (Cross2D(p0, p1, p2) > 0)
        {
            //  p0
            //    p1        long edge on the left
            //  p2
            HalfTriangle(surface, yStart, yMiddle - 1,
                new Edge<TVarying>(p0, p2, v0, v2), new Edge<TVarying>(p0, p1, v0, v1), shader);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p0, p2, v0, v2), new Edge<TVarying>(p1, p2, v1, v2), shader);
        }
        else
        {
            //    p0
            //  p1          long edge on the right
            //    p2
            HalfTriangle(surface, yStart, yMiddle - 1,
                new Edge<TVarying>(p0, p1, v0, v1), new Edge<TVarying>(p0, p2, v0, v2), shader);
            HalfTriangle(surface, yMiddle, yEnd,
                new Edge<TVarying>(p1, p2, v1, v2), new Edge<TVarying>(p0, p2, v0, v2), shader);
        }
    }

    private static void HalfTriangle<TVarying, TShader>(
        FrameBuffer surface, int yStart, int yEnd,
        in Edge<TVarying> left, in Edge<TVarying> right,
        in TShader shader)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {

        var invLeft = left.InvHeight;
        var invRight = right.InvHeight;

        for (var y = yStart; y <= yEnd; y++)
        {

            var gl = System.Math.Clamp((y - left.A.Y) * invLeft, 0f, 1f);
            var gr = System.Math.Clamp((y - right.A.Y) * invRight, 0f, 1f);

            var sx = float.Lerp(left.A.X, left.B.X, gl);
            var ex = float.Lerp(right.A.X, right.B.X, gr);

            if (sx >= ex)
            {
                continue;
            }

            var sz = float.Lerp(left.A.Z, left.B.Z, gl);
            var ez = float.Lerp(right.A.Z, right.B.Z, gr);

            var sv = TVarying.Lerp(left.VA, left.VB, gl);
            var ev = TVarying.Lerp(right.VA, right.VB, gr);

            Scanline(surface, y, sx, ex, sz, ez, sv, ev, shader);
        }
    }

    private static void Scanline<TVarying, TShader>(
        FrameBuffer surface, int y,
        float sx, float ex, float sz, float ez,
        in TVarying sv, in TVarying ev,
        in TShader shader)
        where TVarying : struct, IVarying<TVarying>
        where TShader : struct, IPixelShader<TVarying>
    {

        var xStart = (int)MathF.Max(sx, 0);
        var xEnd = (int)MathF.Min(ex, surface.Width);

        if (xStart >= xEnd)
        {
            return;
        }

        var invSpan = 1f / (ex - sx);

        var dz = (ez - sz) * invSpan;
        var z = sz + (xStart - sx) * dz;

        for (var x = xStart; x < xEnd; x++)
        {
            var t = (x - sx) * invSpan;
            surface.PutPixel(x, y, (int)z, shader.Shade(TVarying.Lerp(sv, ev, t)));
            z += dz;
        }
    }

    /// <summary>Signed area of the triangle in screen space; the sign gives the winding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cross2D(in Vector3 p0, in Vector3 p1, in Vector3 p2) => 
        (p1.X - p0.X) * (p2.Y - p1.Y) - (p1.Y - p0.Y) * (p2.X - p1.X);
}
