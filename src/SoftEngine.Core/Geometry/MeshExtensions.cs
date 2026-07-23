using System.Numerics;

namespace SoftEngine.Core.Geometry;

public static class MeshExtensions
{
    public static IEnumerable<Vector3> BuildVector3s(this float[] vertices)
    {
        for (int i = 0; i < vertices.Length; i += 3)
        {
            yield return new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]);
        }
    }

    public static IEnumerable<int> GetTriangleIndexesHaving(this Vector3 vertex, IMesh mesh)
    {
        for (var i = 0; i < mesh.Triangles.Length; i++)
        {
            if (mesh.Triangles[i].Contains(vertex, mesh.Vertices))
            {
                yield return i;
            }
        }
    }

    public static Vector3 CalculateVertexNormal(this Vector3 vertex, IMesh mesh)
    {
        IEnumerable<int> inTriangles = vertex.GetTriangleIndexesHaving(mesh);
        if (!inTriangles.Any())
        {
            return Vector3.Zero;
        }

        // Zero-area triangles produce NaN normals (Normalize of a zero cross product);
        // the LengthSquared filter drops both those and exact zero vectors.
        Vector3 sum = inTriangles
            .Select(idx => mesh.Triangles[idx].CalculateNormal(mesh.Vertices))
            .Where(normal => normal.LengthSquared() > 1e-12f)
            .Distinct()
            .Aggregate(Vector3.Zero, (v1, v2) => v1 + v2);

        return sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : Vector3.Zero;
    }

    public static IEnumerable<Vector3> CalculateVertexNormals(this IMesh mesh)
    {
        foreach (Vector3 vertex in mesh.Vertices)
        {
            yield return vertex.CalculateVertexNormal(mesh);
        }
    }

    public static Triangle[] BuildTriangleIndices(this int[] indices)
    {
        var triangles = new List<Triangle>();
        for (var i = 0; i < indices.Length; i += 3)
        {
            triangles.Add(new Triangle(indices[i], indices[i + 1], indices[i + 2]));
        }

        return [.. triangles];
    }
}
