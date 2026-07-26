using System.Numerics;

namespace SoftEngine.Core.Animation;

/// <summary>A translation or scale curve: keyed <see cref="Vector3"/> values, linearly blended.</summary>
public sealed class Vector3Track
{
    private readonly float[] _times;
    private readonly Vector3[] _values;

    public Vector3Track(float[] times, Vector3[] values)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);

        if (times.Length != values.Length)
        {
            throw new ArgumentException("A track needs one value per key time.", nameof(values));
        }

        _times = times;
        _values = values;
    }

    public int Count => _times.Length;

    public bool IsEmpty => _times.Length == 0;

    /// <summary>The time of the last key, or 0 for an empty track.</summary>
    public float Duration => _times.Length == 0 ? 0f : _times[^1];

    public Vector3 Sample(float time)
    {
        if (_times.Length == 0)
        {
            return Vector3.Zero;
        }

        Keyframes.Locate(_times, time, out var index, out var blend);

        return blend <= 0f
            ? _values[index]
            : Vector3.Lerp(_values[index], _values[index + 1], blend);
    }
}
