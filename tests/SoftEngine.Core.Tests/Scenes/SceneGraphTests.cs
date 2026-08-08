using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Primitives;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class SceneGraphTests
{
    [Fact]
    public void WorldMatrix_Child_ComposesParentTransform()
    {
        var parent = new SceneNode("parent") { Position = new Vector3(10, 0, 0) };
        var child = parent.Add(new SceneNode("child") { Position = new Vector3(0, 5, 0) });

        parent.UpdateWorldMatrices();

        Approx.Equal(new Vector3(10, 5, 0), child.WorldMatrix.Translation);
    }

    [Fact]
    public void WorldMatrix_RotatedParent_MovesChildAroundIt()
    {
        // A quarter turn about Y sends the child's local +X offset to -Z.
        var parent = new SceneNode("parent")
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
        };
        var child = parent.Add(new SceneNode("child") { Position = new Vector3(2, 0, 0) });

        parent.UpdateWorldMatrices();

        Approx.Equal(new Vector3(0, 0, -2), child.WorldMatrix.Translation);
    }

    [Fact]
    public void WorldMatrix_ScaledParent_ScalesChildOffset()
    {
        var parent = new SceneNode("parent") { Scale = new Vector3(3f) };
        var child = parent.Add(new SceneNode("child") { Position = new Vector3(0, 2, 0) });

        parent.UpdateWorldMatrices();

        Approx.Equal(new Vector3(0, 6, 0), child.WorldMatrix.Translation);
    }

    [Fact]
    public void UpdateWorldMatrices_DeepChain_AccumulatesEveryLevel()
    {
        var root = new SceneNode("0");
        var node = root;

        for (var i = 1; i <= 5; i++)
        {
            node = node.Add(new SceneNode(i.ToString()) { Position = new Vector3(0, 1, 0) });
        }

        root.UpdateWorldMatrices();

        Approx.Equal(new Vector3(0, 5, 0), node.WorldMatrix.Translation);
    }

    [Fact]
    public void Add_NodeWithExistingParent_MovesIt()
    {
        var first = new SceneNode("first");
        var second = new SceneNode("second");
        var child = first.Add(new SceneNode("child"));

        second.Add(child);

        Assert.Empty(first.Children);
        Assert.Same(second, child.Parent);
        Assert.Single(second.Children);
    }

    [Fact]
    public void Add_Self_Throws() =>
        Assert.Throws<ArgumentException>(() =>
        {
            var node = new SceneNode("node");
            node.Add(node);
        });

    /// <summary>
    /// A cycle is the one malformed hierarchy that cannot be survived: both
    /// <see cref="SceneNode.UpdateWorldMatrices()"/> and
    /// <see cref="SceneNode.SelfAndDescendants"/> recurse down the child list, so a loop is an
    /// unbounded recursion and a stack overflow — which no caller can catch.
    /// </summary>
    [Fact]
    public void Add_Ancestor_Throws()
    {
        var root = new SceneNode("root");
        var branch = root.Add(new SceneNode("branch"));
        var leaf = branch.Add(new SceneNode("leaf"));

        Assert.Throws<ArgumentException>(() => leaf.Add(root));
        Assert.Throws<ArgumentException>(() => leaf.Add(branch));

        // Refused, not half-applied: the tree the caller had is the tree it still has.
        Assert.Same(branch, leaf.Parent);
        Assert.Null(root.Parent);
        Assert.Empty(leaf.Children);
    }

    [Fact]
    public void IsAncestorOf_HoldsForItselfAndEveryNodeBelow()
    {
        var root = new SceneNode("root");
        var branch = root.Add(new SceneNode("branch"));
        var leaf = branch.Add(new SceneNode("leaf"));

        Assert.True(root.IsAncestorOf(leaf));
        Assert.True(root.IsAncestorOf(root));
        Assert.False(leaf.IsAncestorOf(root));
        Assert.False(root.IsAncestorOf(null));
    }

    [Fact]
    public void Remove_Child_ClearsItsParent()
    {
        var root = new SceneNode("root");
        var child = root.Add(new SceneNode("child"));

        root.Remove(child);

        Assert.Null(child.Parent);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void SetLocalMatrix_DecomposesIntoComponents()
    {
        var node = new SceneNode("node");
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, 2, 3)), 0.8f);

        var matrix =
            Matrix4x4.CreateScale(2f) *
            Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(new Vector3(4, 5, 6));

        Assert.True(node.SetLocalMatrix(matrix));

        Approx.Equal(new Vector3(2f), node.Scale);
        Approx.Equal(new Vector3(4, 5, 6), node.Position);

        // The components have to rebuild the matrix they came from, not merely look plausible.
        Approx.Equal(matrix.Translation, node.LocalMatrix.Translation);
        Approx.Equal(Vector3.Transform(Vector3.UnitX, matrix), Vector3.Transform(Vector3.UnitX, node.LocalMatrix));
    }

    [Fact]
    public void Find_ReturnsNodeAnywhereInSubtree()
    {
        var root = new SceneNode("root");
        var branch = root.Add(new SceneNode("branch"));
        var leaf = branch.Add(new SceneNode("leaf"));

        Assert.Same(leaf, root.Find("leaf"));
        Assert.Same(branch, root.Find("branch"));
        Assert.Null(root.Find("absent"));
    }

    [Fact]
    public void SelfAndDescendants_VisitsParentsBeforeChildren()
    {
        var root = new SceneNode("root");
        var a = root.Add(new SceneNode("a"));
        a.Add(new SceneNode("a1"));
        root.Add(new SceneNode("b"));

        Assert.Equal(["root", "a", "a1", "b"], root.SelfAndDescendants().Select(node => node.Name));
    }

    [Fact]
    public void MeshWorldMatrix_WithoutParent_IsItsOwnTransform()
    {
        IMesh mesh = new Cube { Position = new Vector3(1, 2, 3) };

        Approx.Equal(new Vector3(1, 2, 3), mesh.WorldMatrix.Translation);
    }

    [Fact]
    public void MeshWorldMatrix_WithParent_FollowsTheNode()
    {
        var node = new SceneNode("node") { Position = new Vector3(0, 10, 0) };
        node.UpdateWorldMatrices();

        IMesh mesh = new Cube { Parent = node, Position = new Vector3(1, 0, 0) };

        Approx.Equal(new Vector3(1, 10, 0), mesh.WorldMatrix.Translation);
    }

    [Fact]
    public void MeshWorldMatrix_ParentMoves_MeshFollowsWithoutBeingTouched()
    {
        var node = new SceneNode("node");
        IMesh mesh = new Cube { Parent = node };

        node.Position = new Vector3(0, 0, 7);
        node.UpdateWorldMatrices();

        Approx.Equal(new Vector3(0, 0, 7), mesh.WorldMatrix.Translation);
    }

    [Fact]
    public void MeshWorldMatrix_ParentedToScaledNode_InheritsTheScale()
    {
        var node = new SceneNode("node") { Scale = new Vector3(4f) };
        node.UpdateWorldMatrices();

        IMesh mesh = new Cube { Parent = node, Position = new Vector3(1, 0, 0) };

        // The offset is scaled by the parent, which is what makes a marker on a scaled rig
        // come out the wrong size unless the scale is divided back out.
        Approx.Equal(new Vector3(4, 0, 0), mesh.WorldMatrix.Translation);
    }
}
