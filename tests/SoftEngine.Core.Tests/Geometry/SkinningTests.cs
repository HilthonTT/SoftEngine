using SoftEngine.Core.Animation;
using SoftEngine.Core.Geometry;
using SoftEngine.Core.Geometry.Skinning;
using SoftEngine.Core.Scenes;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class SkinningTests
{
    /// <summary>
    /// Two joints a unit apart on Y, and a quad whose lower vertices belong to the first and
    /// whose upper vertices belong to the second. Small enough that the right answer for any
    /// pose can be worked out by hand.
    /// </summary>
    private static (SceneNode Root, SceneNode Upper, SkinnedMesh Mesh) TwoJointQuad()
    {
        var root = new SceneNode("lower");
        var upper = root.Add(new SceneNode("upper") { Position = new Vector3(0, 1, 0) });

        var skeleton = Skeleton.FromBindPose(root, [root, upper]);

        Vector3[] vertices =
        [
            new(-1, 0, 0), new(1, 0, 0),
            new(-1, 1, 0), new(1, 1, 0),
        ];

        var builder = new SkinWeights.Builder(4);
        builder.Add(0, 0, 1f);
        builder.Add(1, 0, 1f);
        builder.Add(2, 1, 1f);
        builder.Add(3, 1, 1f);

        var mesh = new SkinnedMesh(
            vertices,
            [new Triangle(0, 1, 2), new Triangle(1, 3, 2)],
            skeleton,
            builder.Build(),
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);

        mesh.UpdatePose();

        return (root, upper, mesh);
    }

    [Fact]
    public void ApplyPose_AtBindPose_LeavesEveryVertexWhereItWas()
    {
        var (_, _, mesh) = TwoJointQuad();

        Approx.Equal(new Vector3(-1, 0, 0), mesh.Vertices[0]);
        Approx.Equal(new Vector3(1, 0, 0), mesh.Vertices[1]);
        Approx.Equal(new Vector3(-1, 1, 0), mesh.Vertices[2]);
        Approx.Equal(new Vector3(1, 1, 0), mesh.Vertices[3]);
    }

    [Fact]
    public void ApplyPose_TranslatedJoint_MovesOnlyItsOwnVertices()
    {
        var (root, upper, mesh) = TwoJointQuad();

        upper.Position = new Vector3(0, 2, 0);
        mesh.UpdatePose();

        // Bound to the root, which did not move.
        Approx.Equal(new Vector3(-1, 0, 0), mesh.Vertices[0]);
        Approx.Equal(new Vector3(1, 0, 0), mesh.Vertices[1]);

        // Bound to the joint, which rose by one unit.
        Approx.Equal(new Vector3(-1, 2, 0), mesh.Vertices[2]);
        Approx.Equal(new Vector3(1, 2, 0), mesh.Vertices[3]);

        Assert.Same(root, mesh.Skeleton.Root);
    }

    [Fact]
    public void ApplyPose_RigidVertex_MatchesTheJointMatrixExactly()
    {
        var (root, _, mesh) = TwoJointQuad();

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        root.Rotation = rotation;
        mesh.UpdatePose();

        // A vertex with a single full-weight influence is not a blend at all: it must land
        // exactly where transforming it by that one joint's matrix puts it.
        Approx.Equal(Vector3.Transform(new Vector3(1, 0, 0), rotation), mesh.Vertices[1]);
    }

    [Fact]
    public void ApplyPose_HalfWeightedVertex_LandsBetweenTheTwoJoints()
    {
        var root = new SceneNode("a");
        var second = root.Add(new SceneNode("b"));

        var skeleton = Skeleton.FromBindPose(root, [root, second]);

        var builder = new SkinWeights.Builder(1);
        builder.Add(0, 0, 0.5f);
        builder.Add(0, 1, 0.5f);

        var mesh = new SkinnedMesh(
            [Vector3.Zero],
            [new Triangle(0, 0, 0)],
            skeleton,
            builder.Build(),
            [Vector3.UnitZ]);

        second.Position = new Vector3(0, 10, 0);
        mesh.UpdatePose();

        // One joint says "stay", the other says "up ten"; half each is up five.
        Approx.Equal(new Vector3(0, 5, 0), mesh.Vertices[0]);
    }

    [Fact]
    public void ApplyPose_UnweightedVertex_StaysAtBindPose()
    {
        var root = new SceneNode("root");
        var skeleton = Skeleton.FromBindPose(root, [root]);

        // Nothing added to the builder: the vertex has no influence at all.
        var mesh = new SkinnedMesh(
            [new Vector3(3, 4, 5)],
            [new Triangle(0, 0, 0)],
            skeleton,
            new SkinWeights.Builder(1).Build(),
            [Vector3.UnitZ]);

        root.Position = new Vector3(100, 0, 0);
        mesh.UpdatePose();

        Approx.Equal(new Vector3(3, 4, 5), mesh.Vertices[0]);
    }

    [Fact]
    public void ApplyPose_Repeated_DoesNotCompound()
    {
        var (_, upper, mesh) = TwoJointQuad();

        upper.Position = new Vector3(0, 2, 0);

        for (var i = 0; i < 5; i++)
        {
            mesh.UpdatePose();
        }

        // Each pass has to deform the bind pose, not the previous pass's output — otherwise
        // the extra unit accumulates and this vertex climbs to y = 6 by the fifth call.
        Approx.Equal(new Vector3(1, 2, 0), mesh.Vertices[3]);
    }

    [Fact]
    public void ApplyPose_RotatedJoint_RotatesTheNormals()
    {
        var (root, _, mesh) = TwoJointQuad();

        root.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        mesh.UpdatePose();

        // A quarter turn about +X carries the quad's +Z normal round to -Y. A deformer that
        // moved the positions and left the normals behind would light the posed mesh as
        // though it were still in its bind pose.
        Approx.Equal(new Vector3(0, -1, 0), mesh.NormVertices[0]);
        Assert.Equal(1f, mesh.NormVertices[0].Length(), 4);
    }

    [Fact]
    public void ApplyPose_Blend_LeavesNormalsUnitLength()
    {
        var rig = BoneChain.Create(boneCount: 5);
        var clip = BoneChain.Wave(5);

        var player = new AnimationPlayer(rig.Root, clip);
        player.Update(0.7f);
        rig.Mesh.UpdatePose();

        foreach (var normal in rig.Mesh.NormVertices)
        {
            Assert.Equal(1f, normal.Length(), 3);
        }
    }

    [Fact]
    public void BoundingRadius_FollowsThePose()
    {
        var root = new SceneNode("root");
        var skeleton = Skeleton.FromBindPose(root, [root]);

        var builder = new SkinWeights.Builder(1);
        builder.Add(0, 0, 1f);

        var mesh = new SkinnedMesh(
            [new Vector3(1, 0, 0)],
            [new Triangle(0, 0, 0)],
            skeleton,
            builder.Build(),
            [Vector3.UnitZ]);

        Assert.Equal(1f, mesh.BoundingRadius, 4);

        // A raised arm reaches outside the sphere the bind pose fitted in, and the renderer
        // culls whole meshes against that sphere — so it has to grow with the pose.
        root.Position = new Vector3(9, 0, 0);
        mesh.UpdatePose();

        Assert.Equal(10f, mesh.BoundingRadius, 4);
    }

    [Fact]
    public void Skeleton_MismatchedInverseBindCount_Throws()
    {
        var root = new SceneNode("root");

        Assert.Throws<ArgumentException>(() => new Skeleton(root, [root], [Matrix4x4.Identity, Matrix4x4.Identity]));
    }

    [Fact]
    public void Skeleton_IndexOf_FindsJointsByName()
    {
        var root = new SceneNode("hip");
        var knee = root.Add(new SceneNode("knee"));

        var skeleton = Skeleton.FromBindPose(root, [root, knee]);

        Assert.Equal(0, skeleton.IndexOf("hip"));
        Assert.Equal(1, skeleton.IndexOf("knee"));
        Assert.Equal(-1, skeleton.IndexOf("elbow"));
    }

    [Fact]
    public void SkinWeightsBuilder_KeepsTheFourHeaviestAndRenormalizes()
    {
        var builder = new SkinWeights.Builder(1);

        builder.Add(0, 0, 0.05f);
        builder.Add(0, 1, 0.40f);
        builder.Add(0, 2, 0.30f);
        builder.Add(0, 3, 0.15f);
        builder.Add(0, 4, 0.10f);

        var weights = builder.Build();

        // Joint 0 is the lightest of the five and is the one dropped.
        Assert.Equal([1, 2, 3, 4], weights.JointIndices[..4]);

        var total = weights.Weights[..4].Sum();
        Assert.Equal(1f, total, 4);

        // The kept weights stay in proportion to each other after renormalizing.
        Assert.Equal(0.40f / 0.95f, weights.Weights[0], 4);
    }

    [Fact]
    public void SkinWeightsBuilder_UnpaintedVertex_GetsNoInfluences()
    {
        var weights = new SkinWeights.Builder(2).Build();

        Assert.Equal(-1, weights.JointIndices[0]);
        Assert.Equal(0f, weights.Weights[0]);
    }

    [Fact]
    public void SkinWeightsBuilder_IgnoresNegativeJointsAndZeroWeights()
    {
        var builder = new SkinWeights.Builder(1);

        // -1 is Collada's "the bind shape itself", and a zero weight moves nothing.
        builder.Add(0, -1, 0.5f);
        builder.Add(0, 2, 0f);
        builder.Add(0, 3, 1f);

        var weights = builder.Build();

        Assert.Equal(3, weights.JointIndices[0]);
        Assert.Equal(1f, weights.Weights[0], 4);
        Assert.Equal(-1, weights.JointIndices[1]);
    }

    [Fact]
    public void BoneChain_Wave_BendsTheTubeWithoutTearingIt()
    {
        var rig = BoneChain.Create(boneCount: 6, boneLength: 2f);
        var bind = rig.Mesh.Vertices.ToArray();

        var player = new AnimationPlayer(rig.Root, BoneChain.Wave(6));
        player.Time = 0.8f;
        player.Apply();
        rig.Mesh.UpdatePose();

        var moved = rig.Mesh.Vertices.Where((v, i) => (v - bind[i]).Length() > 0.01f).Count();

        Assert.True(moved > 0, "the wave should displace vertices");

        // The tube stays a tube: no vertex is flung anywhere near outside the chain's reach.
        foreach (var vertex in rig.Mesh.Vertices)
        {
            Assert.True(vertex.Length() < 6 * 2f + 2f, $"vertex escaped the chain: {vertex}");
        }
    }

    [Fact]
    public void BoneChain_Rig_IsWoundLikeTheEnginesOwnPrimitives()
    {
        var rig = BoneChain.Create(boneCount: 4, boneLength: 2f);
        var center = new Vector3(0, 4, 0);

        // Every face of a closed shape around the origin must have its geometric normal
        // pointing away from the centre, which is what the back-face test reads as front.
        foreach (var triangle in rig.Mesh.Triangles)
        {
            var v0 = rig.Mesh.Vertices[triangle.I0] - center;
            var v1 = rig.Mesh.Vertices[triangle.I1] - center;
            var v2 = rig.Mesh.Vertices[triangle.I2] - center;

            Assert.True(Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), (v0 + v1 + v2) / 3f) > 0f);
        }
    }

    [Fact]
    public void World_Update_PlaysAndReskinsInOneCall()
    {
        var rig = BoneChain.Create(boneCount: 5);
        var bind = rig.Mesh.Vertices.ToArray();

        var world = new SimpleWorld { Root = rig.Root, Meshes = [rig.Mesh] };
        world.Players.Add(new AnimationPlayer(rig.Root, BoneChain.Wave(5)));

        world.Update(0.5f);

        // One call has to have advanced the clip, refreshed the hierarchy and re-run the
        // deformer; skipping any of the three leaves every vertex at its bind position.
        var moved = rig.Mesh.Vertices.Where((vertex, i) => (vertex - bind[i]).Length() > 1e-3f).Count();

        Assert.True(moved > 0, "World.Update should have re-skinned the mesh");
        Assert.True(((IWorld)world).IsAnimated);
    }

    [Fact]
    public void World_Update_WithNothingAnimated_IsSafe()
    {
        var world = new SimpleWorld();

        world.Update(0.5f);

        Assert.False(((IWorld)world).IsAnimated);
    }
}
