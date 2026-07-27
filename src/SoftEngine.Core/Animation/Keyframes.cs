using System.Numerics;
using System.Runtime.CompilerServices;

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

    /// <summary>
    /// Cubic Hermite between two keys, given the tangent leaving the first and the one
    /// arriving at the second.
    ///
    /// The tangents are expressed per unit of <em>time</em>, not per unit of the 0..1 blend,
    /// so they are scaled by the gap between the keys — which is what makes a curve keep its
    /// shape when the same motion is authored at a different frame rate.
    /// </summary>
    public static Vector3 Hermite(Vector3 from, Vector3 outTangent, Vector3 to, Vector3 inTangent, float t, float span)
    {
        var (a, b, c, d) = Weights(t, span);
        return a * from + b * outTangent + c * to + d * inTangent;
    }

    /// <inheritdoc cref="Hermite(Vector3, Vector3, Vector3, Vector3, float, float)"/>
    public static Quaternion Hermite(Quaternion from, Quaternion outTangent, Quaternion to, Quaternion inTangent, float t, float span)
    {
        var (a, b, c, d) = Weights(t, span);
        return from * a + outTangent * b + to * c + inTangent * d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (float From, float OutTangent, float To, float InTangent) Weights(float t, float span)
    {
        var t2 = t * t;
        var t3 = t2 * t;

        return (
            2f * t3 - 3f * t2 + 1f,
            span * (t3 - 2f * t2 + t),
            -2f * t3 + 3f * t2,
            span * (t3 - t2));
    }
}
