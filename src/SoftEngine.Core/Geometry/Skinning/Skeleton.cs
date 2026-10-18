using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Geometry.Skinning;

public sealed class Skeleton
{
    private readonly Matrix4x4[] _skinningMatrices;
    private readonly Dictionary<string, int> _indexByName;

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

    public IReadOnlyList<Matrix4x4> SkinningMatrices => _skinningMatrices;

    internal Matrix4x4[] SkinningMatrixArray => _skinningMatrices;

    public void UpdatePose()
    {
        Root.UpdateWorldMatrices();
        UpdateSkinningMatrices();
    }

    public void UpdateSkinningMatrices()
    {
        for (var i = 0; i < Joints.Length; i++)
        {
            _skinningMatrices[i] = InverseBindMatrices[i] * Joints[i].WorldMatrix;
        }
    }

    public int IndexOf(string jointName) => _indexByName.GetValueOrDefault(jointName, -1);

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
