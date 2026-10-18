using SoftEngine.Core.Animation;
using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Tests.Animation;

public class AnimationBlendingTests
{
    private static Vector3Track Track(params (float Time, Vector3 Value)[] keys) =>
        new([.. keys.Select(k => k.Time)], [.. keys.Select(k => k.Value)]);

    private static SceneNode Rig() =>
        new("joint") { Position = Vector3.Zero, Scale = new Vector3(3f) };

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
