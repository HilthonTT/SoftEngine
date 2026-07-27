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

    /// <summary>
    /// Radius of a world-space sphere around the mesh: its model-space
    /// <see cref="IMesh.BoundingRadius"/> grown by the largest scale factor its world matrix
    /// applies.
    ///
    /// A mesh's own <see cref="IMesh.Scale"/> is not enough. A mesh parented to a
    /// <see cref="Scenes.Graph.SceneNode"/> inherits everything the chain above it does, and
    /// exported rigs routinely carry a unit conversion — a factor of a hundred — on their top
    /// node. A sphere sized from the mesh's own scale alone is then smaller than the geometry
    /// it is meant to contain, and every test that trusts it rejects a mesh that is really
    /// there: the frustum cull drops it from the frame, the shadow pass sizes the light's
    /// projection too small for it, and a ray passes straight through it.
    /// </summary>
    public static float WorldBoundingRadius(this IMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        return mesh.BoundingRadius * MaxScale(mesh.WorldMatrix);
    }

    /// <summary>
    /// The largest scale factor a transform applies, read off the lengths of its rows — which
    /// are where a row-vector matrix keeps its basis vectors.
    /// </summary>
    public static float MaxScale(in Matrix4x4 matrix)
    {
        var x = new Vector3(matrix.M11, matrix.M12, matrix.M13).Length();
        var y = new Vector3(matrix.M21, matrix.M22, matrix.M23).Length();
        var z = new Vector3(matrix.M31, matrix.M32, matrix.M33).Length();

        return MathF.Max(x, MathF.Max(y, z));
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
