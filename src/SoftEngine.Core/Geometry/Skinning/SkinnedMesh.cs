using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Skinning;

/// <summary>
/// A mesh whose vertices follow a <see cref="Skeleton"/>: linear blend skinning, the standard
/// deformation model. Each vertex is transformed by every joint that claims it and the results
/// are mixed by weight — equivalently, and this is how it is computed here, the joint matrices
/// are mixed first and the vertex is transformed once.
///
/// The deformed positions are written back into the arrays <see cref="Mesh"/> already exposes,
/// so the renderer needs no knowledge of skinning at all: it transforms
/// <see cref="Mesh.Vertices"/> as it always has, and they simply happen to have moved since
/// the previous frame. The bind pose is kept privately, because deforming the deformed output
/// would compound the pose frame after frame.
///
/// Skinned vertices come out in the space the skeleton's world matrices are expressed in. A
/// skinned mesh therefore usually leaves its own <see cref="Mesh.Position"/>,
/// <see cref="Mesh.Rotation"/> and <see cref="IMesh.Parent"/> alone — anything set there
/// applies on top of the pose, which is occasionally what you want and never what you get by
/// accident.
/// </summary>
public sealed class SkinnedMesh : Mesh
{
    private readonly Vector3[] _bindVertices;
    private readonly Vector3[] _bindNormals;

    // Captured the first time tangents are built, for the same reason the positions are: the
    // pose has to be applied to the bind frame, not to the previous pose's.
    private Vector4[]? _bindTangents;

    private float _boundingRadius;

    /// <param name="bindVertices">Positions in the pose the mesh was modelled in.</param>
    /// <param name="bindShapeMatrix">
    /// An extra transform Collada applies to the geometry before skinning, folded into the
    /// bind pose here so it costs nothing per frame. Null means identity.
    /// </param>
    public SkinnedMesh(
        Vector3[] bindVertices,
        Triangle[] triangleIndices,
        Skeleton skeleton,
        SkinWeights weights,
        Vector3[]? vertexNormals = null,
        Matrix4x4? bindShapeMatrix = null,
        ColorRGB[]? triangleColors = null)
        : base(
            Bind(bindVertices, bindShapeMatrix),
            triangleIndices,
            BindNormals(vertexNormals, bindShapeMatrix),
            triangleColors)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(weights);

        Skeleton = skeleton;
        Weights = weights;

        // The base arrays are the live, deformed ones from here on; these are the pose to
        // deform from. Cloned rather than aliased for exactly that reason.
        _bindVertices = (Vector3[])Vertices.Clone();
        _bindNormals = (Vector3[])NormVertices.Clone();

        // A skin that covers fewer vertices than the mesh has leaves the rest at bind pose
        // rather than throwing — malformed weight tables are common in exported files, and a
        // model that renders with a stiff patch beats one that will not load.
        SkinnedVertexCount = System.Math.Min(_bindVertices.Length, weights.VertexCount);

        _boundingRadius = base.BoundingRadius;
    }

    public Skeleton Skeleton { get; }

    public SkinWeights Weights { get; }

    /// <summary>How many of this mesh's vertices the skin actually has weights for.</summary>
    public int SkinnedVertexCount { get; }

    /// <summary>
    /// The radius of the mesh <em>as currently posed</em>. The renderer culls whole meshes
    /// against their bounding sphere, and a raised arm reaches outside the sphere the bind
    /// pose fit in — so the sphere is remeasured with every pose rather than fixed at load.
    /// </summary>
    public override float BoundingRadius => _boundingRadius;

    public override void EnsureTangents()
    {
        base.EnsureTangents();

        // Built from whatever pose is current; snapshot it as the frame to deform, and re-run
        // the deformation so the tangents match the positions immediately.
        if (Tangents is not null && _bindTangents is null)
        {
            _bindTangents = (Vector4[])Tangents.Clone();
            ApplyPose();
        }
    }

    /// <summary>
    /// Refreshes the skeleton's world and skinning matrices, then deforms this mesh into them.
    /// </summary>
    public void UpdatePose()
    {
        Skeleton.UpdatePose();
        ApplyPose();
    }

    /// <summary>
    /// Deforms the mesh using the skeleton's current skinning matrices, without recomputing
    /// them. Use when several meshes share one skeleton and it has already been updated.
    /// </summary>
    public void ApplyPose()
    {
        var matrices = Skeleton.SkinningMatrixArray;
        var jointIndices = Weights.JointIndices;
        var weights = Weights.Weights;

        var positions = Vertices;
        var normals = NormVertices;
        var tangents = _bindTangents is null ? null : Tangents;

        var maxLengthSquared = 0f;

        for (var vertex = 0; vertex < SkinnedVertexCount; vertex++)
        {
            var slot = vertex * SkinWeights.InfluencesPerVertex;
            var first = jointIndices[slot];

            // Unweighted: the modeller never painted this vertex, so nothing should move it.
            if (first < 0 || first >= matrices.Length)
            {
                positions[vertex] = _bindVertices[vertex];
                normals[vertex] = _bindNormals[vertex];
                maxLengthSquared = MathF.Max(maxLengthSquared, positions[vertex].LengthSquared());
                continue;
            }

            // The common case by a wide margin — most of a body is rigidly attached to one
            // joint, and only the vertices near a crease blend. Worth its own path: it skips
            // four matrix scales and three matrix adds.
            var blended = matrices[first];

            if (weights[slot] < 1f)
            {
                blended *= weights[slot];

                for (var influence = 1; influence < SkinWeights.InfluencesPerVertex; influence++)
                {
                    var joint = jointIndices[slot + influence];
                    var weight = weights[slot + influence];

                    if (joint < 0 || weight <= 0f || joint >= matrices.Length)
                    {
                        continue;
                    }

                    blended += matrices[joint] * weight;
                }
            }

            var position = Vector3.Transform(_bindVertices[vertex], blended);
            positions[vertex] = position;

            // The 3×3 part only. Strictly a normal wants the inverse transpose, which differs
            // once a joint scales non-uniformly; joints rotate and translate, and the blend of
            // rotations is close enough to one that renormalizing covers the difference.
            var normal = Vector3.TransformNormal(_bindNormals[vertex], blended);
            var lengthSquared = normal.LengthSquared();
            normals[vertex] = lengthSquared > 0f ? normal / MathF.Sqrt(lengthSquared) : _bindNormals[vertex];

            if (tangents is not null && _bindTangents is not null)
            {
                var bind = _bindTangents[vertex];
                var tangent = Vector3.TransformNormal(new Vector3(bind.X, bind.Y, bind.Z), blended);

                // W is the bitangent's handedness, not a direction — it survives the pose.
                tangents[vertex] = new Vector4(Vector3.Normalize(tangent), bind.W);
            }

            maxLengthSquared = MathF.Max(maxLengthSquared, position.LengthSquared());
        }

        // Vertices past the skin's reach never move, but they still count toward the extent.
        for (var vertex = SkinnedVertexCount; vertex < _bindVertices.Length; vertex++)
        {
            maxLengthSquared = MathF.Max(maxLengthSquared, _bindVertices[vertex].LengthSquared());
        }

        _boundingRadius = MathF.Sqrt(maxLengthSquared);
    }

    private static Vector3[] Bind(Vector3[] bindVertices, Matrix4x4? bindShapeMatrix)
    {
        ArgumentNullException.ThrowIfNull(bindVertices);

        var bound = new Vector3[bindVertices.Length];

        if (bindShapeMatrix is not { } matrix || matrix.IsIdentity)
        {
            Array.Copy(bindVertices, bound, bindVertices.Length);
            return bound;
        }

        for (var i = 0; i < bindVertices.Length; i++)
        {
            bound[i] = Vector3.Transform(bindVertices[i], matrix);
        }

        return bound;
    }

    private static Vector3[]? BindNormals(Vector3[]? vertexNormals, Matrix4x4? bindShapeMatrix)
    {
        if (vertexNormals is null)
        {
            // Null lets the base compute them, which it does from the already-bound positions.
            return null;
        }

        var bound = new Vector3[vertexNormals.Length];

        if (bindShapeMatrix is not { } matrix || matrix.IsIdentity)
        {
            Array.Copy(vertexNormals, bound, vertexNormals.Length);
            return bound;
        }

        for (var i = 0; i < vertexNormals.Length; i++)
        {
            var normal = Vector3.TransformNormal(vertexNormals[i], matrix);
            bound[i] = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : vertexNormals[i];
        }

        return bound;
    }
}
