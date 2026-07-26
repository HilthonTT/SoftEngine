using System.Numerics;

namespace SoftEngine.Core.Scenes.Graph;

/// <summary>
/// One transform in a hierarchy: a local translation, rotation and scale, a parent, and the
/// world matrix that composing the chain produces.
///
/// A mesh with nothing but its own position can only ever be placed absolutely — there is no
/// way to say "the hand goes where the arm puts it". A node says exactly that, and it is also
/// what animation drives: a clip poses nodes, and everything attached to them follows.
///
/// Rotation is a <see cref="Quaternion"/> rather than the Euler
/// <see cref="Math.Rotation3D"/> the meshes carry, because animation has to interpolate
/// between two rotations and Euler angles interpolate through gimbal lock.
/// </summary>
public sealed class SceneNode
{
    private readonly List<SceneNode> _children = [];

    public SceneNode(string name = "")
    {
        Name = name;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
        WorldMatrix = Matrix4x4.Identity;
    }

    public string Name { get; set; }

    /// <summary>
    /// What this node is for. Only the skeleton gizmo reads it — transforms are transforms as
    /// far as the hierarchy is concerned — but it is what lets that view show the rig without
    /// the lights and cameras exported alongside it.
    /// </summary>
    public SceneNodeKind Kind { get; set; } = SceneNodeKind.Transform;

    public SceneNode? Parent { get; private set; }

    public IReadOnlyList<SceneNode> Children => _children;

    public Vector3 Position { get; set; }

    public Quaternion Rotation { get; set; }

    public Vector3 Scale { get; set; }

    /// <summary>
    /// This node's transform relative to its parent — scale, then rotation, then translation,
    /// in the row-vector order the rest of the engine composes matrices in.
    /// </summary>
    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// The composed transform down from the root, as of the last
    /// <see cref="UpdateWorldMatrices()"/>. It is cached rather than walked on every read
    /// because a skinned mesh reads its joints' world matrices once per vertex influence —
    /// re-composing the chain each time would make a deep skeleton quadratic.
    /// </summary>
    public Matrix4x4 WorldMatrix { get; private set; }

    /// <summary>Replaces the local transform with a matrix, decomposed back into TRS.</summary>
    /// <remarks>
    /// Collada stores a node's transform (and every keyframe of an animated one) as a baked
    /// matrix, so the importer arrives with matrices and the node stores components. A matrix
    /// that will not decompose — mirrored or sheared — leaves the components alone rather than
    /// filling them with NaN.
    /// </remarks>
    public bool SetLocalMatrix(in Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation))
        {
            return false;
        }

        Scale = scale;
        Rotation = rotation;
        Position = translation;
        return true;
    }

    public SceneNode Add(SceneNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (ReferenceEquals(child, this))
        {
            throw new ArgumentException("A node cannot parent itself.", nameof(child));
        }

        child.Parent?._children.Remove(child);

        child.Parent = this;
        _children.Add(child);

        return child;
    }

    public void Remove(SceneNode child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
        }
    }

    /// <summary>Recomputes this subtree's world matrices from its parent's, or from identity at a root.</summary>
    public void UpdateWorldMatrices() => UpdateWorldMatrices(Parent?.WorldMatrix ?? Matrix4x4.Identity);

    public void UpdateWorldMatrices(in Matrix4x4 parentWorld)
    {
        WorldMatrix = LocalMatrix * parentWorld;

        // Iterative over the child list rather than recursive per child would need an explicit
        // stack; skeletons are shallow (tens of joints deep at worst), so recursion is fine.
        foreach (var child in _children)
        {
            child.UpdateWorldMatrices(WorldMatrix);
        }
    }

    /// <summary>This node and everything below it, parents before children.</summary>
    public IEnumerable<SceneNode> SelfAndDescendants()
    {
        yield return this;

        foreach (var child in _children)
        {
            foreach (var descendant in child.SelfAndDescendants())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>The first node in this subtree with the given name, or null.</summary>
    public SceneNode? Find(string name)
    {
        foreach (var node in SelfAndDescendants())
        {
            if (string.Equals(node.Name, name, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    public override string ToString() => string.IsNullOrEmpty(Name) ? "(node)" : Name;
}
