using SoftEngine.Core.Animation;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Tests;

public class AnimationTests
{
    private static Vector3Track Track(params (float Time, Vector3 Value)[] keys) =>
        new([.. keys.Select(k => k.Time)], [.. keys.Select(k => k.Value)]);

    [Fact]
    public void Vector3Track_BetweenKeys_Interpolates()
    {
        var track = Track((0f, Vector3.Zero), (2f, new Vector3(10, 20, 30)));

        Approx.Equal(new Vector3(5, 10, 15), track.Sample(1f));
    }

    [Fact]
    public void Vector3Track_OutsideItsSpan_HoldsTheEndKeys()
    {
        var track = Track((1f, new Vector3(4, 0, 0)), (3f, new Vector3(8, 0, 0)));

        // Held, not extrapolated: a clip says nothing about what happens outside its own span.
        Approx.Equal(new Vector3(4, 0, 0), track.Sample(-5f));
        Approx.Equal(new Vector3(8, 0, 0), track.Sample(99f));
    }

    [Fact]
    public void Vector3Track_ManyKeys_FindsTheRightPair()
    {
        var keys = Enumerable.Range(0, 50).Select(i => ((float)i, new Vector3(i, 0, 0))).ToArray();
        var track = Track(keys);

        // The binary search has to land on key 37, not merely somewhere plausible.
        Approx.Equal(new Vector3(37.25f, 0, 0), track.Sample(37.25f));
    }

    [Fact]
    public void Vector3Track_SingleKey_IsConstant()
    {
        var track = Track((5f, new Vector3(1, 2, 3)));

        Approx.Equal(new Vector3(1, 2, 3), track.Sample(0f));
        Approx.Equal(new Vector3(1, 2, 3), track.Sample(100f));
    }

    [Fact]
    public void Vector3Track_Empty_SamplesToZero()
    {
        var track = new Vector3Track([], []);

        Assert.True(track.IsEmpty);
        Assert.Equal(Vector3.Zero, track.Sample(1f));
    }

    [Fact]
    public void Vector3Track_MismatchedLengths_Throws() =>
        Assert.Throws<ArgumentException>(() => new Vector3Track([0f, 1f], [Vector3.Zero]));

    [Fact]
    public void QuaternionTrack_BetweenKeys_Slerps()
    {
        var from = Quaternion.Identity;
        var to = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        var track = new QuaternionTrack([0f, 1f], [from, to]);

        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);

        Approx.EqualRotation(expected, track.Sample(0.5f));
    }

    [Fact]
    public void QuaternionTrack_NeighboursWithOppositeSigns_TakesTheShortArc()
    {
        var from = Quaternion.Identity;
        var to = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        // Same rotation, opposite hemisphere. Blending naively would swing the long way.
        var track = new QuaternionTrack([0f, 1f], [from, -to]);

        Approx.EqualRotation(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f), track.Sample(0.5f));
    }

    [Fact]
    public void QuaternionTrack_Sample_StaysNormalized()
    {
        var track = new QuaternionTrack(
            [0f, 1f],
            [Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2.5f)]);

        Assert.Equal(1f, track.Sample(0.37f).Length(), 4);
    }

    [Fact]
    public void QuaternionTrack_Empty_SamplesToIdentity() =>
        Assert.Equal(Quaternion.Identity, new QuaternionTrack([], []).Sample(3f));

    [Fact]
    public void NodeChannel_Apply_LeavesComponentsWithNoCurveAlone()
    {
        var node = new SceneNode("node")
        {
            Position = new Vector3(1, 2, 3),
            Scale = new Vector3(2f),
        };

        var channel = new NodeChannel("node")
        {
            Rotation = new QuaternionTrack([0f], [Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f)]),
        };

        channel.Apply(node, 0f);

        Assert.Equal(new Vector3(1, 2, 3), node.Position);
        Assert.Equal(new Vector3(2f), node.Scale);
        Approx.EqualRotation(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1f), node.Rotation);
    }

    [Fact]
    public void NodeChannel_FromMatrices_SplitsIntoComponentTracks()
    {
        var first = Matrix4x4.CreateTranslation(new Vector3(0, 0, 0));
        var second =
            Matrix4x4.CreateScale(2f) *
            Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f) *
            Matrix4x4.CreateTranslation(new Vector3(10, 0, 0));

        var channel = NodeChannel.FromMatrices("joint", [0f, 1f], [first, second]);

        Assert.NotNull(channel.Translation);
        Assert.NotNull(channel.Rotation);
        Assert.NotNull(channel.Scale);

        Approx.Equal(new Vector3(10, 0, 0), channel.Translation!.Sample(1f));
        Approx.Equal(new Vector3(2f), channel.Scale!.Sample(1f));
        Approx.EqualRotation(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f), channel.Rotation!.Sample(1f));
    }

    [Fact]
    public void NodeChannel_FromMatrices_InterpolatesRotationAsRotation()
    {
        var half = NodeChannel.FromMatrices(
            "joint",
            [0f, 2f],
            [Matrix4x4.Identity, Matrix4x4.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f)]);

        // Blending the matrices component-wise would shrink the vector toward the origin;
        // decomposing first and slerping keeps it on the unit circle where a rotation lives.
        var rotated = Vector3.Transform(Vector3.UnitX, half.Rotation!.Sample(1f));

        Assert.Equal(1f, rotated.Length(), 4);
        Approx.Equal(new Vector3(MathF.Sqrt(0.5f), MathF.Sqrt(0.5f), 0), rotated);
    }

    [Fact]
    public void Clip_Duration_IsTheLatestKeyAcrossEveryChannel()
    {
        var clip = new AnimationClip("clip",
        [
            new NodeChannel("a") { Translation = Track((0f, Vector3.Zero), (1.5f, Vector3.One)) },
            new NodeChannel("b") { Translation = Track((0f, Vector3.Zero), (4f, Vector3.One)) },
        ]);

        Assert.Equal(4f, clip.Duration);
        Assert.False(clip.IsEmpty);
    }

    [Fact]
    public void Clip_WithOnlyEmptyChannels_IsEmpty()
    {
        var clip = new AnimationClip("clip", [new NodeChannel("a")]);

        Assert.True(clip.IsEmpty);
        Assert.Equal(0f, clip.Duration);
    }

    private static (SceneNode Root, AnimationClip Clip) Rig()
    {
        var root = new SceneNode("root");
        root.Add(new SceneNode("child"));

        var clip = new AnimationClip("clip",
        [
            new NodeChannel("child") { Translation = Track((0f, Vector3.Zero), (4f, new Vector3(0, 40, 0))) },
        ]);

        return (root, clip);
    }

    [Fact]
    public void Player_BindsChannelsToNodesByName()
    {
        var (root, _) = Rig();

        var clip = new AnimationClip("clip",
        [
            new NodeChannel("child") { Translation = Track((0f, Vector3.Zero)) },
            new NodeChannel("absent") { Translation = Track((0f, Vector3.Zero)) },
        ]);

        var player = new AnimationPlayer(root, clip);

        Assert.Equal(1, player.BoundChannelCount);
    }

    [Fact]
    public void Player_Update_AdvancesTimeAndPosesTheNodes()
    {
        var (root, clip) = Rig();
        var player = new AnimationPlayer(root, clip);

        player.Update(1f);

        Assert.Equal(1f, player.Time, 4);
        Approx.Equal(new Vector3(0, 10, 0), root.Find("child")!.Position);
    }

    [Fact]
    public void Player_Paused_HoldsThePoseInsteadOfResetting()
    {
        var (root, clip) = Rig();
        var player = new AnimationPlayer(root, clip) { Time = 2f, IsPlaying = false };

        player.Update(1f);

        Assert.Equal(2f, player.Time, 4);
        Approx.Equal(new Vector3(0, 20, 0), root.Find("child")!.Position);
    }

    [Fact]
    public void Player_Looping_WrapsPastTheEnd()
    {
        var (root, clip) = Rig();
        var player = new AnimationPlayer(root, clip) { Time = 3.5f };

        player.Update(1f);

        Assert.Equal(0.5f, player.Time, 4);
    }

    [Fact]
    public void Player_LoopingBackwards_WrapsPastTheStart()
    {
        var (root, clip) = Rig();
        var player = new AnimationPlayer(root, clip) { Time = 0.5f, Speed = -1f };

        player.Update(1f);

        Assert.Equal(3.5f, player.Time, 4);
    }

    [Fact]
    public void Player_NotLooping_ClampsAtTheEnd()
    {
        var (root, clip) = Rig();
        var player = new AnimationPlayer(root, clip) { Loop = false, Time = 3.5f };

        player.Update(10f);

        Assert.Equal(4f, player.Time, 4);
        Approx.Equal(new Vector3(0, 40, 0), root.Find("child")!.Position);
    }

    [Fact]
    public void Player_Speed_ScalesElapsedTime()
    {
        var (root, clip) = Rig();
        var player = new AnimationPlayer(root, clip) { Speed = 2f };

        player.Update(1f);

        Assert.Equal(2f, player.Time, 4);
    }

    [Fact]
    public void Player_EmptyClip_DoesNotDivideByZeroDuration()
    {
        var root = new SceneNode("root");
        var player = new AnimationPlayer(root, new AnimationClip("empty", []));

        player.Update(1f);

        Assert.Equal(0f, player.Time);
    }

    [Fact]
    public void Player_Restart_RewindsAndReposes()
    {
        var (root, clip) = Rig();
        var player = new AnimationPlayer(root, clip) { Time = 3f };
        player.Apply();

        player.Restart();

        Assert.Equal(0f, player.Time);
        Approx.Equal(Vector3.Zero, root.Find("child")!.Position);
    }
}
