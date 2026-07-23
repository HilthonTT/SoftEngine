using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Clipping;

/// <summary>
/// Sutherland-Hodgman clipping of a single triangle against the near plane (clip-space
/// z ≥ 0, this projection's convention). Triangles that cross the plane are split into
/// one or two sub-triangles whose new vertices interpolate every attribute, so filled
/// geometry no longer disappears when it reaches the camera.
///
/// The near plane is the only one that needs true clipping: behind it w flips sign and
/// the perspective divide breaks, while the side and far planes are handled by the
/// rasterizer's screen bounds and whole-triangle rejection.
/// </summary>
public static class NearPlaneClipper
{
    /// <summary>
    /// Clips <paramref name="triangle"/> (which must straddle the near plane) and appends
    /// the resulting sub-triangles and vertices to <paramref name="vbx"/>. World positions
    /// and normals of the source vertices must already be computed. Returns the number of
    /// sub-triangles added to <paramref name="visible"/>.
    /// </summary>
    public static int Clip(
        VertexBuffer vbx,
        in Triangle triangle,
        int sourceTriangleIndex,
        int meshIndex,
        List<(int MeshIndex, int TriangleIndex)> visible)
    {
        var uvs = vbx.Mesh?.TexCoords;

        Span<int> input = [triangle.I0, triangle.I1, triangle.I2];

        // Clipping a triangle against one plane yields at most 4 vertices.
        Span<int> output = stackalloc int[4];
        var outputCount = 0;

        for (var i = 0; i < 3; i++)
        {
            var current = input[i];
            var next = input[(i + 1) % 3];

            var zCurrent = vbx.Vertices[current].Proj.Z;
            var zNext = vbx.Vertices[next].Proj.Z;

            if (zCurrent >= 0)
            {
                output[outputCount++] = current;
            }

            // Edge crosses the plane: emit the intersection point.
            if (zCurrent >= 0 != zNext >= 0)
            {
                var t = zCurrent / (zCurrent - zNext);

                var vertex = Vertices.Lerp(vbx.Vertices[current], vbx.Vertices[next], t);
                var uv = uvs is null ? Vector2.Zero : Vector2.Lerp(uvs[current], uvs[next], t);

                output[outputCount++] = vbx.AddClippedVertex(vertex, uv);
            }
        }

        // 3 vertices when one survived, 4 when two survived; fan-triangulate either way.
        // Sutherland-Hodgman preserves the winding order.
        var added = 0;
        for (var i = 2; i < outputCount; i++)
        {
            if (IsOutsideXYFar(vbx.GetVertex(output[0]).Proj, vbx.GetVertex(output[i - 1]).Proj, vbx.GetVertex(output[i]).Proj))
            {
                continue;
            }

            var index = vbx.AddClippedTriangle(new Triangle(output[0], output[i - 1], output[i]), sourceTriangleIndex);
            visible.Add((meshIndex, index));
            added++;
        }

        return added;
    }

    /// <summary>
    /// Whole-triangle rejection against the side and far planes, valid once all vertices
    /// are in front of the near plane (w &gt; 0). Mirrors <see cref="Triangle.IsOutsideFrustum"/>.
    /// </summary>
    private static bool IsOutsideXYFar(in Vector4 p0, in Vector4 p1, in Vector4 p2)
    {
        if (p0.X < -p0.W && p1.X < -p1.W && p2.X < -p2.W)
        {
            return true;
        }

        if (p0.X > p0.W && p1.X > p1.W && p2.X > p2.W)
        {
            return true;
        }

        if (p0.Y < -p0.W && p1.Y < -p1.W && p2.Y < -p2.W)
        {
            return true;
        }

        if (p0.Y > p0.W && p1.Y > p1.W && p2.Y > p2.W)
        {
            return true;
        }

        return p0.Z > p0.W && p1.Z > p1.W && p2.Z > p2.W;
    }
}
