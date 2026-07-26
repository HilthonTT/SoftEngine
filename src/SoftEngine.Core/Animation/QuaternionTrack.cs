using System.Numerics;

namespace SoftEngine.Core.Animation;

/// <summary>
/// A rotation curve: keyed <see cref="Quaternion"/> values, spherically blended along the
/// shorter of the two arcs between neighbours.
/// </summary>
public sealed class QuaternionTrack
{
    private readonly float[] _times;
    private readonly Quaternion[] _values;

    public QuaternionTrack(float[] times, Quaternion[] values)
    {
        ArgumentNullException.ThrowIfNull(times, nameof(times));
        ArgumentNullException.ThrowIfNull(values, nameof(values));

        if (times.Length != values.Length)
        {
            throw new ArgumentException("A track needs one value per key time.", nameof(values));
        }

        _times = times;
        _values = values;
    }

    public int Count => _times.Length;

    public bool IsEmpty => _times.Length == 0;

    public float Duration => _times.Length == 0 ? 0f : _times[^1];

    public Quaternion Sample(float time)
    {
        if (_times.Length == 0)
        {
            return Quaternion.Identity;
        }

        Keyframes.Locate(_times, time, out var index, out var blend);

        if (blend <= 0f)
        {
            return _values[index];
        }

        // q and -q are the same rotation but opposite ends of the sphere, so blending
        // neighbours written with opposite signs could take the long way round — a joint
        // spinning 300° to reach a pose 60° away. Slerp already negates one end when their
        // dot is negative, so the short arc is the one taken.
        return Quaternion.Normalize(Quaternion.Slerp(_values[index], _values[index + 1], blend));
    }
}
