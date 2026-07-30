using SoftEngine.Core.Geometry;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Acceleration;

/// <summary>
/// The world flattened into one array of triangles in world space, with everything a ray needs
/// to shade what it hits.
///
/// <para>
/// The rasterizer never needs this. It walks meshes one at a time, transforming each into view
/// space as it goes, and a mesh's own space is exactly where its vertices already are. A ray does
/// not get that: it has no idea which mesh it is about to hit, so either every mesh's inverse
/// transform is applied to it in turn — which is what <see cref="Picking.ScenePicker"/> does, and
/// is fine for one ray — or the geometry is brought into a single space once and queried millions
/// of times. Past a few thousand rays the second is the only affordable answer, and it is the one
/// thing <see cref="Bvh"/> needs to be built over.
/// </para>
///
/// <para>
/// Positions are stored three to a triangle rather than indexed. It costs memory — a shared vertex
/// is stored once per triangle that uses it — and buys a traversal that touches one contiguous run
/// per intersection test instead of chasing indices into a second array. For a structure that
/// exists to be hit by rays, that is the right way round.
/// </para>
/// </summary>
public sealed class SceneGeometry
{
    private readonly Vector3[] _positions;
    private readonly Vector3[] _normals;
    private readonly Vector2[] _texCoords;
    private readonly Vector4[] _tangents;

    private readonly int[] _meshIndices;
    private readonly int[] _triangleIndices;
    private readonly IMesh[] _meshes;

    private readonly bool _hasTexCoords;
    private readonly bool _hasTangents;

    private SceneGeometry(
        Vector3[] positions,
        Vector3[] normals,
        Vector2[] texCoords,
        Vector4[] tangents,
        int[] meshIndices,
        int[] triangleIndices,
        IMesh[] meshes,
        bool hasTexCoords,
        bool hasTangents)
    {
        _positions = positions;
        _normals = normals;
        _texCoords = texCoords;
        _tangents = tangents;
        _meshIndices = meshIndices;
        _triangleIndices = triangleIndices;
        _meshes = meshes;
        _hasTexCoords = hasTexCoords;
        _hasTangents = hasTangents;

        TriangleCount = meshIndices.Length;
    }

    public int TriangleCount { get; }

    /// <summary>Whether any mesh contributed texture coordinates worth interpolating.</summary>
    public bool HasTexCoords => _hasTexCoords;

    /// <summary>Whether any mesh contributed tangents, so a normal map has a frame to tilt in.</summary>
    public bool HasTangents => _hasTangents;

    /// <summary>A triangle's three world-space corners.</summary>
    public (Vector3 A, Vector3 B, Vector3 C) Corners(int triangle)
    {
        var i = triangle * 3;
        return (_positions[i], _positions[i + 1], _positions[i + 2]);
    }

    public Vector3 Position(int triangle, int corner) => _positions[triangle * 3 + corner];

    public Vector3 Normal(int triangle, int corner) => _normals[triangle * 3 + corner];

    public Vector2 TexCoord(int triangle, int corner) => _texCoords[triangle * 3 + corner];

    public Vector4 Tangent(int triangle, int corner) => _tangents[triangle * 3 + corner];

    /// <summary>The mesh a triangle came from, and its index in the world's list.</summary>
    public IMesh Mesh(int triangle) => _meshes[_meshIndices[triangle]];

    public int MeshIndex(int triangle) => _meshIndices[triangle];

    /// <summary>The triangle's index within its own mesh — what a <see cref="Picking.PickHit"/> reports.</summary>
    public int SourceTriangle(int triangle) => _triangleIndices[triangle];

    /// <summary>
    /// Flattens every mesh the world would draw.
    ///
    /// The same two exclusions <see cref="Picking.ScenePicker"/> makes, for the same reasons: a
    /// mesh switched off in the object table is not there, and one faded to nothing is not there
    /// either. Partly transparent meshes stay — a ray can decide what to do about them, which is
    /// more than a bounding test could.
    /// </summary>
    public static SceneGeometry Build(IWorld world)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        var meshes = new List<IMesh>();
        var total = 0;

        foreach (var mesh in world.Meshes)
        {
            if (!mesh.Visible || mesh.Opacity <= 0f || mesh.Triangles.Length == 0)
            {
                continue;
            }

            meshes.Add(mesh);
            total += mesh.Triangles.Length;
        }

        var positions = new Vector3[total * 3];
        var normals = new Vector3[total * 3];
        var texCoords = new Vector2[total * 3];
        var tangents = new Vector4[total * 3];
        var meshIndices = new int[total];
        var triangleIndices = new int[total];

        var anyTexCoords = false;
        var anyTangents = false;

        var t = 0;

        for (var m = 0; m < meshes.Count; m++)
        {
            var mesh = meshes[m];

            // A normal map needs a UV frame, and building one walks every triangle — the same
            // call the painters make from Prepare, for the same reason.
            if (mesh.Material?.NeedsTangents == true)
            {
                mesh.EnsureTangents();
            }

            var worldMatrix = mesh.WorldMatrix;

            // A normal is transformed by the inverse transpose, which is what keeps it
            // perpendicular to the surface through a non-uniform scale.
            var normalMatrix = Matrix4x4.Invert(worldMatrix, out var inverse)
                ? Matrix4x4.Transpose(inverse)
                : Matrix4x4.Identity;

            var vertices = mesh.Vertices;
            var vertexNormals = mesh.NormVertices;
            var uvs = mesh.TexCoords;
            var meshTangents = mesh.Tangents;

            var hasVertexNormals = vertexNormals.Length == vertices.Length;
            var hasUvs = uvs is not null && uvs.Length == vertices.Length;
            var hasTangents = meshTangents is not null && meshTangents.Length == vertices.Length;

            anyTexCoords |= hasUvs;
            anyTangents |= hasTangents;

            var triangles = mesh.Triangles;

            for (var i = 0; i < triangles.Length; i++)
            {
                var triangle = triangles[i];
                var corners = (triangle.I0, triangle.I1, triangle.I2);

                var slot = t * 3;

                var a = Vector3.Transform(vertices[corners.Item1], worldMatrix);
                var b = Vector3.Transform(vertices[corners.Item2], worldMatrix);
                var c = Vector3.Transform(vertices[corners.Item3], worldMatrix);

                positions[slot] = a;
                positions[slot + 1] = b;
                positions[slot + 2] = c;

                if (hasVertexNormals)
                {
                    normals[slot] = Vector3.TransformNormal(vertexNormals[corners.Item1], normalMatrix);
                    normals[slot + 1] = Vector3.TransformNormal(vertexNormals[corners.Item2], normalMatrix);
                    normals[slot + 2] = Vector3.TransformNormal(vertexNormals[corners.Item3], normalMatrix);
                }
                else
                {
                    // No shading normals at all: the triangle's own plane, which is what a flat
                    // painter would have used.
                    var face = Vector3.Cross(b - a, c - a);

                    normals[slot] = face;
                    normals[slot + 1] = face;
                    normals[slot + 2] = face;
                }

                if (hasUvs)
                {
                    texCoords[slot] = uvs![corners.Item1];
                    texCoords[slot + 1] = uvs[corners.Item2];
                    texCoords[slot + 2] = uvs[corners.Item3];
                }

                if (hasTangents)
                {
                    // The direction U grows in is a direction, so it transforms like the surface
                    // rather than like the normal; W is a handedness sign and does not transform.
                    for (var corner = 0; corner < 3; corner++)
                    {
                        var index = corner switch
                        {
                            0 => corners.Item1,
                            1 => corners.Item2,
                            _ => corners.Item3,
                        };

                        var tangent = meshTangents![index];
                        var direction = Vector3.TransformNormal(new Vector3(tangent.X, tangent.Y, tangent.Z), worldMatrix);

                        tangents[slot + corner] = new Vector4(direction, tangent.W);
                    }
                }

                meshIndices[t] = m;
                triangleIndices[t] = i;

                t++;
            }
        }

        return new SceneGeometry(
            positions, normals, texCoords, tangents,
            meshIndices, triangleIndices, [.. meshes],
            anyTexCoords, anyTangents);
    }

    /// <summary>
    /// A cheap stamp of the geometry this was built from, for noticing that it has moved.
    ///
    /// Not a hash of the vertices: that would cost as much as rebuilding. It folds in every
    /// mesh's world matrix — which catches anything dragged, parented or re-posed — plus the ends
    /// of each vertex array, which catches a skin being deformed in place. A deformation that
    /// leaves both end vertices exactly where they were slips through, and the cost of that is a
    /// stale acceleration structure rather than a wrong answer to any question about the one it
    /// holds.
    /// </summary>
    public static int Stamp(IWorld world)
    {
        ArgumentNullException.ThrowIfNull(world, nameof(world));

        var stamp = new HashCode();

        foreach (var mesh in world.Meshes)
        {
            stamp.Add(mesh.Visible);
            stamp.Add(mesh.Opacity);
            stamp.Add(mesh.WorldMatrix);
            stamp.Add(mesh.Triangles.Length);

            var vertices = mesh.Vertices;
            stamp.Add(vertices.Length);

            if (vertices.Length > 0)
            {
                stamp.Add(vertices[0]);
                stamp.Add(vertices[^1]);
            }
        }

        return stamp.ToHashCode();
    }
}
