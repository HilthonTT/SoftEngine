using SoftEngine.Core.Animation;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Tests;

/// <summary>
/// Blending two clips over one rig: crossfading between them, and layering one over another.
///
/// What separates a mixer from two players is that a player writes as it samples, so the
/// second one overwrites the first and no weight exists that would mix them. Every test here
/// is a statement about a weight strictly between 0 and 1 producing something that is neither
/// clip.
/// </summary>
public class AnimationBlendingTests
{
    private static Vector3Track Track(params (float Time, Vector3 Value)[] keys) =>
        new([.. keys.Select(k => k.Time)], [.. keys.Select(k => k.Value)]);

    /// <summary>A one-joint rig at the origin, with a rest scale that is deliberately not 1.</summary>
    private static SceneNode Rig() =>
        new("joint") { Position = Vector3.Zero, Scale = new Vector3(3f) };

    /// <summary>A clip holding one joint at a constant translation.</summary>
    private static AnimationClip Holding(string name, Vector3 at) =>
        new(name, [new NodeChannel("joint") { Translation = Track((0f, at), (1f, at)) }]);

    [Fact]
    public void Mixer_TwoLayers_BlendsRatherThanOverwrites()
    {
        var joint = Rig();
        var mixer = new AnimationMixer(joint);

        mixer.Add(Holding("a", new Vector3(0, 0, 0)));
        mixer.Add(Holding("b", new Vector3(10, 0, 0)), weight: 0.25f);

        mixer.Apply();

        // Neither clip: a quarter of the way from the first to the second.
        Approx.Equal(new Vector3(2.5f, 0, 0), joint.Position);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 5f)]
    [InlineData(1f, 10f)]
    public void Mixer_Crossfade_RunsFromOneClipToTheOther(float weight, float expected)
    {
        var joint = Rig();
        var mixer = new AnimationMixer(joint);

        mixer.Add(Holding("from", Vector3.Zero));
        var to = mixer.Add(Holding("to", new Vector3(10, 0, 0)), weight);

        Assert.Equal(weight, to.Weight);

        mixer.Apply();

        Approx.Equal(new Vector3(expected, 0, 0), joint.Position);
    }

    /// <summary>
    /// The trap the mixer's captured rest pose exists to avoid. The blend writes its result
    /// into the node, so reading the node back as the base of the next frame's blend would feed
    /// the output into its own input — and a layer held at a constant half weight would creep
    /// toward full over a few seconds instead of standing still.
    /// </summary>
    [Fact]
    public void Mixer_AppliedRepeatedly_DoesNotCreepTowardTheTopLayer()
    {
        var joint = Rig();
        var mixer = new AnimationMixer(joint);

        mixer.Add(Holding("base", Vector3.Zero));
        mixer.Add(Holding("over", new Vector3(10, 0, 0)), weight: 0.5f);

        for (var frame = 0; frame < 200; frame++)
        {
            mixer.Update(1f / 60f);
        }

        Approx.Equal(new Vector3(5f, 0, 0), joint.Position);
    }

    /// <summary>
    /// A layer that keys only a rotation must leave the position and scale to the layers below
    /// it — which is what layering a head turn over a walk means. A channel with no curve
    /// contributing an identity would drag the joint's scale from 3 toward 1 by the weight.
    /// </summary>
    [Fact]
    public void Mixer_LayerKeyingOneComponent_LeavesTheOthersAlone()
    {
        var joint = Rig();
        joint.Position = new Vector3(1, 2, 3);

        var mixer = new AnimationMixer(joint);

        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        mixer.Add(new AnimationClip("turn",
            [new NodeChannel("joint") { Rotation = new QuaternionTrack([0f, 1f], [turn, turn]) }]));

        mixer.Apply();

        Approx.Equal(new Vector3(1, 2, 3), joint.Position);
        Approx.Equal(new Vector3(3f), joint.Scale);

        Assert.Equal(turn.Y, joint.Rotation.Y, 4);
    }

    /// <summary>
    /// Rotations blend on the sphere, not component by component. A half blend between two 90°
    /// turns about the same axis is 45°, and a lerp of the components is not — it is the same
    /// axis at a slightly wrong angle, and off the unit sphere besides.
    /// </summary>
    [Fact]
    public void Mixer_BlendedRotations_SlerpAndStayNormalized()
    {
        var joint = Rig();
        var mixer = new AnimationMixer(joint);

        var identity = Quaternion.Identity;
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);

        mixer.Add(new AnimationClip("rest",
            [new NodeChannel("joint") { Rotation = new QuaternionTrack([0f, 1f], [identity, identity]) }]));

        mixer.Add(new AnimationClip("turn",
            [new NodeChannel("joint") { Rotation = new QuaternionTrack([0f, 1f], [quarter, quarter]) }]),
            weight: 0.5f);

        mixer.Apply();

        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);

        Assert.Equal(1f, joint.Rotation.Length(), 4);
        Assert.Equal(expected.Y, joint.Rotation.Y, 4);
        Assert.Equal(expected.W, joint.Rotation.W, 4);
    }

    [Fact]
    public void Mixer_LayerAtZeroWeight_ContributesNothing()
    {
        var joint = Rig();
        var mixer = new AnimationMixer(joint);

        mixer.Add(Holding("base", new Vector3(4, 0, 0)));
        mixer.Add(Holding("muted", new Vector3(99, 0, 0)), weight: 0f);

        mixer.Apply();

        Approx.Equal(new Vector3(4, 0, 0), joint.Position);
    }

    [Fact]
    public void Mixer_WithNoLayers_LeavesTheRigAlone()
    {
        var joint = Rig();
        joint.Position = new Vector3(7, 8, 9);

        new AnimationMixer(joint).Update(1f / 60f);

        Approx.Equal(new Vector3(7, 8, 9), joint.Position);
    }

    /// <summary>
    /// Layers advance independently, so two clips of different lengths crossfade without one
    /// dragging the other's playhead.
    /// </summary>
    [Fact]
    public void Mixer_Update_AdvancesEveryLayersOwnPlayhead()
    {
        var joint = Rig();
        var mixer = new AnimationMixer(joint);

        var slow = mixer.Add(Holding("slow", Vector3.Zero));
        var fast = mixer.Add(Holding("fast", new Vector3(10, 0, 0)), weight: 0.5f);

        slow.Loop = false;
        fast.Loop = false;
        fast.Speed = 2f;

        mixer.Update(0.25f);

        Assert.Equal(0.25f, slow.Time, 4);
        Assert.Equal(0.5f, fast.Time, 4);
    }

    /// <summary>
    /// A player still works on its own, unblended, exactly as it did — the mixer is an addition
    /// rather than a replacement, and the demos drive one clip through a player directly.
    /// </summary>
    [Fact]
    public void Player_OnItsOwn_IgnoresWeightEntirely()
    {
        var joint = Rig();

        var player = new AnimationPlayer(joint, Holding("clip", new Vector3(10, 0, 0)))
        {
            Weight = 0.1f,
        };

        player.Apply();

        Approx.Equal(new Vector3(10, 0, 0), joint.Position);
    }

    [Fact]
    public void Mixer_Remove_DropsTheLayersContribution()
    {
        var joint = Rig();
        var mixer = new AnimationMixer(joint);

        mixer.Add(Holding("base", Vector3.Zero));
        var over = mixer.Add(Holding("over", new Vector3(10, 0, 0)), weight: 1f);

        mixer.Apply();
        Approx.Equal(new Vector3(10, 0, 0), joint.Position);

        Assert.True(mixer.Remove(over));

        mixer.Apply();
        Approx.Equal(Vector3.Zero, joint.Position);
    }
}
