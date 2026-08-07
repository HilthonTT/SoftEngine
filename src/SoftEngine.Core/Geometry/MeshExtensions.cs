using System.Numerics;

namespace SoftEngine.Core.Geometry;

public static class MeshExtensions
{
    /// <summary>
    /// Reads a flat float array as X, Y, Z triples. A trailing partial triple — which is what a
    /// truncated or malformed source array leaves behind — is dropped rather than read past the
    /// end of the array.
    /// </summary>
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
    ///
    /// <para>
    /// Every consumer has to agree about this number or they contradict each other about which
    /// meshes exist, so they all come through here rather than each writing the multiply out.
    /// </para>
    /// </summary>
    public static float WorldBoundingRadius(this IMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        return mesh.WorldBoundingRadius(mesh.WorldMatrix);
    }

    /// <summary>
    /// The same radius for a caller that has already composed the mesh's world matrix.
    ///
    /// <para>
    /// Which every per-frame one has: <see cref="IMesh.WorldMatrix"/> builds three matrices and
    /// walks the parent chain, and the cull, the shadow pass and the picker each need the matrix
    /// itself as well as the radius. This overload is what lets them share the helper without
    /// paying for it twice per mesh per frame.
    /// </para>
    /// </summary>
    public static float WorldBoundingRadius(this IMesh mesh, in Matrix4x4 worldMatrix)
    {
        ArgumentNullException.ThrowIfNull(mesh, nameof(mesh));

        return mesh.BoundingRadius * MaxScale(worldMatrix);
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

    /// <summary>
    /// Groups a flat index stream into triangles. A trailing one or two indices are dropped
    /// rather than read past the end of the array: an index count that is not a multiple of
    /// three is what a truncated <c>&lt;p&gt;</c> stream or a malformed face list produces, and
    /// an importer that throws there fails to open the whole model over its last corner.
    /// </summary>
    /// <summary>
    /// A copy of a mesh that shares its geometry and carries its own transform.
    ///
    /// <para>
    /// The vertex, index and UV arrays are <em>shared</em>, not copied: a duplicate is another
    /// instance of the same shape, and copying a hundred thousand vertices to place a second one is
    /// paying for a difference that does not exist. What is copied is everything a duplicate has to
    /// be able to change on its own — the transform, the visibility, the opacity — plus the
    /// per-triangle colours, which are shared by the primitives and would otherwise mean recolouring
    /// one cube recolours every copy of it.
    /// </para>
    ///
    /// <para>
    /// The result is a plain <see cref="Mesh"/> whatever it was made from, and the shared arrays are
    /// the reason: a <see cref="Skinning.SkinnedMesh"/> deforms its vertex array in place, so a
    /// duplicate of one would follow the original's pose exactly rather than being posed on its own,
    /// and calling it a SkinnedMesh would promise otherwise.
    /// </para>
    /// </summary>
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

            // The same material instance, so a duplicate reflects a change to the original's
            // shininess or maps. Materials are shared by every mesh an importer split off one of
            // them, so this is the behaviour the rest of the engine already has.
            Material = mesh.Material ?? new Material(),
        };
    }

    public static Triangle[] BuildTriangleIndices(this int[] indices)
    {
        var triangles = new List<Triangle>(indices.Length / 3);
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            triangles.Add(new Triangle(indices[i], indices[i + 1], indices[i + 2]));
        }

        return [.. triangles];
    }
}
