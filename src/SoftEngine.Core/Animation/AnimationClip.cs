namespace SoftEngine.Core.Animation;

/// <summary>
/// One named animation: a set of per-node curves and the span of time they cover.
/// A clip is data, and holds no playback state — that belongs to
/// <see cref="AnimationPlayer"/>, so two things can play the same clip at different times.
/// </summary>
public sealed class AnimationClip(string name, IReadOnlyList<NodeChannel> channels)
{
    public string Name { get; } = name;

    public IReadOnlyList<NodeChannel> Channels { get; } = channels;

    /// <summary>
    /// How long the clip runs: the latest key time in any of its channels. Zero for a clip
    /// with no keys, which a player treats as a static pose rather than dividing by.
    /// </summary>
    public float Duration { get; } = LatestKeyTime(channels);

    /// <summary>Whether any channel carries keys. Collada files routinely declare empty ones.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var channel in Channels)
            {
                if (!channel.IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static float LatestKeyTime(IReadOnlyList<NodeChannel> channels)
    {
        var duration = 0f;

        foreach (var channel in channels)
        {
            duration = MathF.Max(duration, channel.Duration);
        }

        return duration;
    }
}
