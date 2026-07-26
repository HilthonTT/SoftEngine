using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Skinning;

/// <summary>
/// The joints a skinned mesh is bound to, and the matrices that take it from its bind pose to
/// whatever pose the joints are currently in.
///
/// Each joint contributes two things. Its <em>inverse bind matrix</em> undoes the transform it
/// had when the mesh was modelled, moving a vertex out of model space and into that joint's
/// local frame; its current world matrix then puts the vertex back down wherever the joint has
/// moved to. The product of the two is the only thing skinning needs per joint, so it is
/// computed once per pose rather than once per vertex.
/// </summary>
public sealed class Skeleton
{
    private readonly Matrix4x4[] _skinningMatrices;
    private readonly Dictionary<string, int> _indexByName;

    /// <param name="root">The node the skeleton hangs off — usually the joints' common ancestor.</param>
    /// <param name="joints">The joint nodes, in the order the skin's weights index them.</param>
    /// <param name="inverseBindMatrices">One per joint: the inverse of its world matrix in the bind pose.</param>
    public Skeleton(SceneNode root, SceneNode[] joints, Matrix4x4[] inverseBindMatrices)
    {
        ArgumentNullException.ThrowIfNull(root, nameof(root));
        ArgumentNullException.ThrowIfNull(joints, nameof(joints));
        ArgumentNullException.ThrowIfNull(inverseBindMatrices, nameof(inverseBindMatrices));

        if (joints.Length != inverseBindMatrices.Length)
        {
            throw new ArgumentException(
                "A skeleton needs one inverse bind matrix per joint.",
                nameof(inverseBindMatrices));
        }

        Root = root;
        Joints = joints;
        InverseBindMatrices = inverseBindMatrices;

        _skinningMatrices = new Matrix4x4[joints.Length];
        Array.Fill(_skinningMatrices, Matrix4x4.Identity);

        _indexByName = new Dictionary<string, int>(joints.Length, StringComparer.Ordinal);
        for (var i = 0; i < joints.Length; i++)
        {
            _indexByName.TryAdd(joints[i].Name, i);
        }
    }

    public SceneNode Root { get; }

    public SceneNode[] Joints { get; }

    public Matrix4x4[] InverseBindMatrices { get; }

    public int JointCount => Joints.Length;

    /// <summary>
    /// Bind-pose model space to posed model space, one per joint, as of the last
    /// <see cref="UpdatePose"/>. Exposed so a mesh can read them without copying.
    /// </summary>
    public IReadOnlyList<Matrix4x4> SkinningMatrices => _skinningMatrices;

    /// <summary>The same matrices as the array itself, for the per-vertex loop that reads them.</summary>
    internal Matrix4x4[] SkinningMatrixArray => _skinningMatrices;

    /// <summary>
    /// Refreshes the node world matrices under <see cref="Root"/> and rebuilds the skinning
    /// matrices from them. Call once per frame, after the pose has been set and before any
    /// mesh bound to this skeleton deforms itself.
    /// </summary>
    public void UpdatePose()
    {
        Root.UpdateWorldMatrices();
        UpdateSkinningMatrices();
    }

    /// <summary>
    /// Rebuilds the skinning matrices from the joints' current world matrices, without
    /// touching the node hierarchy. Use when something else already updated it.
    /// </summary>
    public void UpdateSkinningMatrices()
    {
        for (var i = 0; i < Joints.Length; i++)
        {
            _skinningMatrices[i] = InverseBindMatrices[i] * Joints[i].WorldMatrix;
        }
    }

    public int IndexOf(string jointName) => _indexByName.GetValueOrDefault(jointName, -1);

    /// <summary>
    /// Builds a skeleton by taking each joint's current world matrix as its bind pose — the
    /// usual way to rig geometry that was modelled in the same pose the nodes are already in.
    /// </summary>
    public static Skeleton FromBindPose(SceneNode root, SceneNode[] joints)
    {
        ArgumentNullException.ThrowIfNull(root, nameof(root));
        ArgumentNullException.ThrowIfNull(joints, nameof(joints));

        root.UpdateWorldMatrices();

        var inverseBinds = new Matrix4x4[joints.Length];
        for (var i = 0; i < joints.Length; i++)
        {
            inverseBinds[i] = Matrix4x4.Invert(joints[i].WorldMatrix, out var inverse)
                ? inverse
                : Matrix4x4.Identity;
        }

        return new Skeleton(root, joints, inverseBinds);
    }
}
