using System.Numerics;

namespace SoftEngine.Core.Geometry;

public static class MeshExtensions
{
    public static IEnumerable<Vector3> BuildVector3s(this float[] vertices)
    {
        for (int i = 0; i + 2 < vertices.Length; i += 3)
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

    public static float WorldBoundingRadius(this IMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        return mesh.WorldBoundingRadius(mesh.WorldMatrix);
    }

    public static float WorldBoundingRadius(this IMesh mesh, in Matrix4x4 worldMatrix)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        return mesh.BoundingRadius * MaxScale(worldMatrix);
    }

    public static float MaxScale(in Matrix4x4 matrix)
    {
        var x = new Vector3(matrix.M11, matrix.M12, matrix.M13).Length();
        var y = new Vector3(matrix.M21, matrix.M22, matrix.M23).Length();
        var z = new Vector3(matrix.M31, matrix.M32, matrix.M33).Length();

        return MathF.Max(x, MathF.Max(y, z));
    }

    public static Mesh Duplicate(this IMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        return new Mesh(mesh.Vertices, mesh.Triangles, mesh.NormVertices, [.. mesh.TriangleColors])
        {
            Position = mesh.Position,
            Rotation = new Math.Rotation3D(mesh.Rotation.XPitch, mesh.Rotation.YYaw, mesh.Rotation.ZRoll),
            Scale = mesh.Scale,
            Visible = mesh.Visible,
            Opacity = mesh.Opacity,
            TexCoords = mesh.TexCoords,
            Tangents = mesh.Tangents,
            Parent = mesh.Parent,

            Material = mesh.Material ?? new Material(),
        };
    }

    public static Triangle[] BuildTriangleIndices(this int[] indices, int vertexCount)
    {
        var triangles = new List<Triangle>(indices.Length / 3);

        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);

            if ((uint)a < (uint)vertexCount && (uint)b < (uint)vertexCount && (uint)c < (uint)vertexCount)
            {
                triangles.Add(new Triangle(a, b, c));
            }
        }

        return [.. triangles];
    }
}
