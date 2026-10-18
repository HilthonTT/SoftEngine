using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Animation;

internal static class Keyframes
{
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

    public static Vector3 Hermite(Vector3 from, Vector3 outTangent, Vector3 to, Vector3 inTangent, float t, float span)
    {
        var (a, b, c, d) = Weights(t, span);
        return a * from + b * outTangent + c * to + d * inTangent;
    }

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
