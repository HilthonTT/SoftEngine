using System.Numerics;

namespace SoftEngine.Core.Scenes.Graph;

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

    public SceneNodeKind Kind { get; set; } = SceneNodeKind.Transform;

    public SceneNode? Parent { get; private set; }

    public IReadOnlyList<SceneNode> Children => _children;

    public Vector3 Position { get; set; }

    public Quaternion Rotation { get; set; }

    public Vector3 Scale { get; set; }

    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);

    public Matrix4x4 WorldMatrix { get; private set; }

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
        ArgumentNullException.ThrowIfNull(child, nameof(child));

        if (ReferenceEquals(child, this))
        {
            throw new ArgumentException("A node cannot parent itself.", nameof(child));
        }

        if (child.IsAncestorOf(this))
        {
            throw new ArgumentException(
                $"'{child.Name}' is already an ancestor of '{Name}'; adding it here would make the hierarchy a cycle.",
                nameof(child));
        }

        child.Parent?._children.Remove(child);

        child.Parent = this;
        _children.Add(child);

        return child;
    }

    public bool IsAncestorOf(SceneNode? node)
    {
        for (var ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, this))
            {
                return true;
            }
        }

        return false;
    }

    public void Remove(SceneNode child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
        }
    }

    public void UpdateWorldMatrices() => UpdateWorldMatrices(Parent?.WorldMatrix ?? Matrix4x4.Identity);

    public void UpdateWorldMatrices(in Matrix4x4 parentWorld)
    {
        WorldMatrix = LocalMatrix * parentWorld;

        foreach (var child in _children)
        {
            child.UpdateWorldMatrices(WorldMatrix);
        }
    }

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
