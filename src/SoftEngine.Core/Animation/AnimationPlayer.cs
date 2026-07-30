using SoftEngine.Core.Scenes.Graph;

namespace SoftEngine.Core.Animation;

/// <summary>
/// Plays one <see cref="AnimationClip"/> against one scene-graph subtree: holds the playhead,
/// and on each update samples every channel into the node it targets.
///
/// Channels address nodes by name, and resolving a name means walking the tree — so the
/// player resolves all of them once, at construction, into an array parallel to the channel
/// list. A parrot with a hundred channels over sixty nodes would otherwise spend six thousand
/// string comparisons per frame to move a wing.
/// </summary>
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
            // First wins: exporters do emit duplicate names, and a stable choice at least
            // animates the same node every run.
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

    /// <summary>How many of the clip's channels found a node in this subtree.</summary>
    public int BoundChannelCount { get; }

    /// <summary>The playhead, in seconds.</summary>
    public float Time { get; set; }

    /// <summary>A multiplier on real time; negative plays the clip backwards.</summary>
    public float Speed { get; set; } = 1f;

    public bool Loop { get; set; } = true;

    public bool IsPlaying { get; set; } = true;

    public float Duration => Clip.Duration;

    /// <summary>
    /// How much of this clip reaches the nodes when an <see cref="AnimationMixer"/> is driving
    /// it: 1 is the clip as authored, 0 is nothing, and in between is a blend with whatever the
    /// layers under it produced. Ignored by <see cref="Update"/> and <see cref="Apply"/>, which
    /// play one clip on its own and have nothing to blend against.
    /// </summary>
    public float Weight { get; set; } = 1f;

    /// <summary>The node a channel was bound to at construction, or null when the name matched none.</summary>
    public SceneNode? TargetOf(int channel) => _targets[channel];

    /// <summary>
    /// Advances the playhead without posing anything. What a mixer calls, because a layered
    /// pose cannot be written a clip at a time.
    /// </summary>
    public void Advance(float deltaSeconds)
    {
        if (IsPlaying)
        {
            Time = Wrap(Time + deltaSeconds * Speed);
        }
    }

    /// <summary>
    /// Advances the playhead and poses the nodes. A paused player still poses them, so
    /// pausing holds the current frame rather than dropping back to the rest pose.
    /// </summary>
    public void Update(float deltaSeconds)
    {
        Advance(deltaSeconds);
        Apply();
    }

    /// <summary>Writes the clip's value at <see cref="Time"/> into every bound node.</summary>
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

    /// <summary>Rewinds to the start of the clip and poses the nodes there.</summary>
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

        // The remainder is negative when the playhead ran off the front, which happens at any
        // negative speed; adding one duration back puts it in range without a branch per frame
        // on how far it overran.
        time %= duration;

        return time < 0f ? time + duration : time;
    }
}
