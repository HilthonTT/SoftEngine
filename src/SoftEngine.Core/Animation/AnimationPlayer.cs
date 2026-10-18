using SoftEngine.Core.Scenes.Graph;

namespace SoftEngine.Core.Animation;

public sealed class AnimationPlayer
{
    private readonly SceneNode?[] _targets;

    public AnimationPlayer(SceneNode root, AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(root, nameof(root));
        ArgumentNullException.ThrowIfNull(clip, nameof(clip));

        Root = root;
        Clip = clip;

        var byName = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
        foreach (var node in root.SelfAndDescendants())
        {
            byName.TryAdd(node.Name, node);
        }

        _targets = new SceneNode?[clip.Channels.Count];
        for (var i = 0; i < _targets.Length; i++)
        {
            _targets[i] = byName.GetValueOrDefault(clip.Channels[i].TargetName);
            if (_targets[i] is not null)
            {
                BoundChannelCount++;
            }
        }
    }

    public SceneNode Root { get; }

    public AnimationClip Clip { get; }

    public int BoundChannelCount { get; }

    public float Time { get; set; }

    public float Speed { get; set; } = 1f;

    public bool Loop { get; set; } = true;

    public bool IsPlaying { get; set; } = true;

    public float Duration => Clip.Duration;

    public float Weight { get; set; } = 1f;

    public SceneNode? TargetOf(int channel) => _targets[channel];

    public void Advance(float deltaSeconds)
    {
        if (IsPlaying)
        {
            Time = Wrap(Time + deltaSeconds * Speed);
        }
    }

    public void Update(float deltaSeconds)
    {
        Advance(deltaSeconds);
        Apply();
    }

    public void Apply()
    {
        var channels = Clip.Channels;

        for (var i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] is { } node)
            {
                channels[i].Apply(node, Time);
            }
        }
    }

    public void Restart()
    {
        Time = 0f;
        Apply();
    }

    private float Wrap(float time)
    {
        var duration = Clip.Duration;

        if (duration <= 0f)
        {
            return 0f;
        }

        if (!Loop)
        {
            return System.Math.Clamp(time, 0f, duration);
        }

        time %= duration;

        return time < 0f ? time + duration : time;
    }
}
