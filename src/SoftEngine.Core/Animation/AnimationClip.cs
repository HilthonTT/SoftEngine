namespace SoftEngine.Core.Animation;

public sealed class AnimationClip(string name, IReadOnlyList<NodeChannel> channels)
{
    public string Name { get; } = name;

    public IReadOnlyList<NodeChannel> Channels { get; } = channels;

    public float Duration { get; } = LatestKeyTime(channels);

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
