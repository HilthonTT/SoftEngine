using SoftEngine.Core.Buffers;
using SoftEngine.Core.Geometry;
using System.Numerics;

namespace SoftEngine.Core.Pipeline.Clipping;

public static class NearPlaneClipper
{
    public static int Clip(
        VertexBuffer vbx,
        in Triangle triangle,
        int sourceTriangleIndex,
        int meshIndex,
        List<(int MeshIndex, int TriangleIndex)> visible)
    {
        var uvs = vbx.Mesh?.TexCoords;
        var tangents = vbx.Mesh?.Tangents;

        Span<int> input = [triangle.I0, triangle.I1, triangle.I2];

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

            if (zCurrent >= 0 != zNext >= 0)
            {
                var t = zCurrent / (zCurrent - zNext);

                var vertex = Vertices.Lerp(vbx.Vertices[current], vbx.Vertices[next], t);
                var uv = uvs is null ? Vector2.Zero : Vector2.Lerp(uvs[current], uvs[next], t);

                var tangent = tangents is null ? Vector4.Zero : Vector4.Lerp(tangents[current], tangents[next], t);

                output[outputCount++] = vbx.AddClippedVertex(vertex, uv, tangent);
            }
        }

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
