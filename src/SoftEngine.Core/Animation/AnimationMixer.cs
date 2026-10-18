using SoftEngine.Core.Scenes.Graph;
using System.Numerics;

namespace SoftEngine.Core.Animation;

public sealed class AnimationMixer
{
    private struct Pose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    private readonly List<AnimationPlayer> _layers = [];
    private readonly Dictionary<SceneNode, int> _slots = [];

    private SceneNode[] _nodes = [];

    private Pose[] _rest = [];
    private Pose[] _accumulator = [];

    public AnimationMixer(SceneNode root)
    {
        ArgumentNullException.ThrowIfNull(root, nameof(root));

        Root = root;
    }

    public SceneNode Root { get; }

    public IReadOnlyList<AnimationPlayer> Layers => _layers;

    public AnimationPlayer Add(AnimationClip clip, float weight = 1f)
    {
        ArgumentNullException.ThrowIfNull(clip, nameof(clip));

        var player = new AnimationPlayer(Root, clip) { Weight = weight };

        Add(player);

        return player;
    }

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

    public void CaptureRestPose()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            _rest[i] = PoseOf(_nodes[i]);
        }
    }

    public void Update(float deltaSeconds)
    {
        foreach (var layer in _layers)
        {
            layer.Advance(deltaSeconds);
        }

        Apply();
    }

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
