using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Animation;

/// <summary>
/// Plays several clips at once over one scene-graph subtree, blending them by weight instead
/// of letting the last one win.
///
/// <para>
/// A single <see cref="AnimationPlayer"/> writes each channel's value straight into the node,
/// so two of them over the same skeleton is not two clips playing — it is the second clip
/// overwriting the first, every frame, and a crossfade is not available at any weight. The
/// mixer separates the two halves the player fuses: every layer is <em>sampled</em> first,
/// nothing is written until all of them have been asked, and the answer each node gets is the
/// blend.
/// </para>
///
/// <para>
/// Layers compose in order, each blended over the result of the ones before it by its own
/// <see cref="AnimationPlayer.Weight"/>. That one rule covers both things this is for. A
/// crossfade from A to B is A at weight 1 with B above it at a weight run from 0 to 1. A
/// layered clip — a head turn over a walk — is a clip that keys only the nodes it means to
/// take over, at whatever weight it should take them over by.
/// </para>
/// </summary>
public sealed class AnimationMixer
{
    /// <summary>One node's local transform, as the three components a clip keys separately.</summary>
    private struct Pose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    private readonly List<AnimationPlayer> _layers = [];
    private readonly Dictionary<SceneNode, int> _slots = [];

    private SceneNode[] _nodes = [];

    // The pose every frame's blend starts from, captured when a node first joins the mixer.
    //
    // It has to be captured rather than read back off the node, and that is the whole reason
    // this array exists. The mixer writes its result into the nodes, so by the next frame a
    // node's "current" transform is last frame's output — and starting a weighted blend from
    // it would feed the result back into itself, creeping a half-weighted clip toward full
    // over a few seconds and never settling.
    private Pose[] _rest = [];
    private Pose[] _accumulator = [];

    public AnimationMixer(SceneNode root)
    {
        ArgumentNullException.ThrowIfNull(root, nameof(root));

        Root = root;
    }

    public SceneNode Root { get; }

    public IReadOnlyList<AnimationPlayer> Layers => _layers;

    /// <summary>Adds a clip as the topmost layer and returns the player driving it.</summary>
    public AnimationPlayer Add(AnimationClip clip, float weight = 1f)
    {
        ArgumentNullException.ThrowIfNull(clip, nameof(clip));

        var player = new AnimationPlayer(Root, clip) { Weight = weight };

        Add(player);

        return player;
    }

    /// <summary>
    /// Adds an existing player as the topmost layer. Its own root need not be this mixer's —
    /// what matters is which nodes its channels resolved to.
    /// </summary>
    public void Add(AnimationPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player, nameof(player));

        _layers.Add(player);

        for (var i = 0; i < player.Clip.Channels.Count; i++)
        {
            if (player.TargetOf(i) is { } node)
            {
                Track(node);
            }
        }
    }

    public bool Remove(AnimationPlayer player) => _layers.Remove(player);

    /// <summary>
    /// Re-reads the rest pose from the nodes as they stand now. For a caller that has moved a
    /// joint deliberately and wants the blend to start from where it put it, rather than from
    /// where the node was when the mixer first saw it.
    /// </summary>
    public void CaptureRestPose()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            _rest[i] = PoseOf(_nodes[i]);
        }
    }

    /// <summary>Advances every layer's playhead, then poses the nodes from the blend.</summary>
    public void Update(float deltaSeconds)
    {
        foreach (var layer in _layers)
        {
            layer.Advance(deltaSeconds);
        }

        Apply();
    }

    /// <summary>Blends every layer at its current time and writes the result into the nodes.</summary>
    public void Apply()
    {
        var count = _slots.Count;

        if (count == 0)
        {
            return;
        }

        Array.Copy(_rest, _accumulator, count);

        foreach (var layer in _layers)
        {
            var weight = System.Math.Clamp(layer.Weight, 0f, 1f);

            if (weight <= 0f)
            {
                continue;
            }

            var channels = layer.Clip.Channels;
            var time = layer.Time;

            for (var i = 0; i < channels.Count; i++)
            {
                if (layer.TargetOf(i) is not { } node || !_slots.TryGetValue(node, out var slot))
                {
                    continue;
                }

                var channel = channels[i];

                ref var pose = ref _accumulator[slot];

                if (channel.SampleTranslation(time, out var translation))
                {
                    pose.Position = Vector3.Lerp(pose.Position, translation, weight);
                }

                if (channel.SampleRotation(time, out var rotation))
                {
                    // Slerp rather than a component lerp, and normalized after: the halfway
                    // point between two rotations is not the average of their components, and
                    // blending them as if it were shears a joint on its way between two poses.
                    // Slerp also negates one end when the two point at opposite halves of the
                    // sphere, so a 60° blend never takes the 300° way round.
                    pose.Rotation = Quaternion.Normalize(Quaternion.Slerp(pose.Rotation, rotation, weight));
                }

                if (channel.SampleScale(time, out var scale))
                {
                    pose.Scale = Vector3.Lerp(pose.Scale, scale, weight);
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            var node = _nodes[i];
            ref readonly var pose = ref _accumulator[i];

            node.Position = pose.Position;
            node.Rotation = pose.Rotation;
            node.Scale = pose.Scale;
        }
    }

    private void Track(SceneNode node)
    {
        if (_slots.ContainsKey(node))
        {
            return;
        }

        var slot = _slots.Count;

        if (slot >= _nodes.Length)
        {
            var capacity = System.Math.Max(8, _nodes.Length * 2);

            Array.Resize(ref _nodes, capacity);
            Array.Resize(ref _rest, capacity);
            Array.Resize(ref _accumulator, capacity);
        }

        _slots[node] = slot;
        _nodes[slot] = node;
        _rest[slot] = PoseOf(node);
    }

    private static Pose PoseOf(SceneNode node) => new()
    {
        Position = node.Position,
        Rotation = node.Rotation,
        Scale = node.Scale,
    };
}
