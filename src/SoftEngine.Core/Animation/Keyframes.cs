namespace SoftEngine.Core.Animation;

/// <summary>
/// Locating a time in a sorted key list. Shared by every track type, because the search is
/// the same whatever is being interpolated — only the blend differs.
/// </summary>
internal static class Keyframes
{
    /// <summary>
    /// Finds the key at or before <paramref name="time"/> and how far past it the time sits,
    /// as a 0..1 fraction of the gap to the next key. Before the first key and after the last
    /// the value is held rather than extrapolated: a clip says nothing about what happens
    /// outside its own span, and guessing produces motion no animator authored.
    /// </summary>
    public static void Locate(ReadOnlySpan<float> times, float time, out int index, out float blend)
    {
        var last = times.Length - 1;

        if (time <= times[0])
        {
            index = 0;
            blend = 0f;
            return;
        }

        if (time >= times[last])
        {
            index = last;
            blend = 0f;
            return;
        }

        // Binary search for the last key at or before the time. A linear scan with a
        // remembered cursor would beat this for playback that only moves forward, but it
        // makes the track stateful, and scrubbing a timeline backwards is a real use.
        var low = 0;
        var high = last;

        while (low < high)
        {
            var middle = (low + high + 1) >> 1;

            if (times[middle] <= time)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        index = low;

        var span = times[index + 1] - times[index];
        blend = span > 0f ? (time - times[index]) / span : 0f;
    }
}
