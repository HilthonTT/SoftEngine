using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Skinning;

public sealed class SkinnedMesh : Mesh
{
    private readonly Vector3[] _bindVertices;
    private readonly Vector3[] _bindNormals;

    private Vector4[]? _bindTangents;

    private float _boundingRadius;

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

        _bindVertices = (Vector3[])Vertices.Clone();
        _bindNormals = (Vector3[])NormVertices.Clone();

        SkinnedVertexCount = System.Math.Min(_bindVertices.Length, weights.VertexCount);

        _boundingRadius = base.BoundingRadius;
    }

    public Skeleton Skeleton { get; }

    public SkinWeights Weights { get; }

    public int SkinnedVertexCount { get; }

    public override float BoundingRadius => _boundingRadius;

    public override void EnsureTangents()
    {
        base.EnsureTangents();

        if (Tangents is not null && _bindTangents is null)
        {
            _bindTangents = (Vector4[])Tangents.Clone();
            ApplyPose();
        }
    }

    public void UpdatePose()
    {
        Skeleton.UpdatePose();
        ApplyPose();
    }

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

            if (first < 0 || first >= matrices.Length)
            {
                positions[vertex] = _bindVertices[vertex];
                normals[vertex] = _bindNormals[vertex];
                maxLengthSquared = MathF.Max(maxLengthSquared, positions[vertex].LengthSquared());
                continue;
            }

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

            var normal = Vector3.TransformNormal(_bindNormals[vertex], blended);
            var lengthSquared = normal.LengthSquared();
            normals[vertex] = lengthSquared > 0f ? normal / MathF.Sqrt(lengthSquared) : _bindNormals[vertex];

            if (tangents is not null && _bindTangents is not null)
            {
                var bind = _bindTangents[vertex];
                var tangent = Vector3.TransformNormal(new Vector3(bind.X, bind.Y, bind.Z), blended);

                tangents[vertex] = new Vector4(Vector3.Normalize(tangent), bind.W);
            }

            maxLengthSquared = MathF.Max(maxLengthSquared, position.LengthSquared());
        }

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
