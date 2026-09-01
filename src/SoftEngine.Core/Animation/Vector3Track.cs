using System.Numerics;

namespace SoftEngine.Core.Animation;

public sealed class Vector3Track
{
    private readonly float[] _times;
    private readonly Vector3[] _values;

    private readonly Vector3[]? _inTangents;
    private readonly Vector3[]? _outTangents;

    public Vector3Track(float[] times, Vector3[] values)
        : this(times, values, TrackInterpolation.Linear, null, null)
    {
    }

    public Vector3Track(
        float[] times,
        Vector3[] values,
        TrackInterpolation interpolation,
        Vector3[]? inTangents = null,
        Vector3[]? outTangents = null)
    {
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);

        if (times.Length != values.Length)
        {
            throw new ArgumentException("A track needs one value per key time.", nameof(values));
        }

        if (interpolation == TrackInterpolation.CubicSpline &&
            (inTangents is null || outTangents is null ||
             inTangents.Length != values.Length || outTangents.Length != values.Length))
        {
            interpolation = TrackInterpolation.Linear;
            inTangents = null;
            outTangents = null;
        }

        _times = times;
        _values = values;
        _inTangents = inTangents;
        _outTangents = outTangents;

        Interpolation = interpolation;
    }

    public int Count => _times.Length;

    public bool IsEmpty => _times.Length == 0;

    public TrackInterpolation Interpolation { get; }

    public float Duration => _times.Length == 0 ? 0f : _times[^1];

    public Vector3 Sample(float time)
    {
        if (_times.Length == 0)
        {
            return Vector3.Zero;
        }

        Keyframes.Locate(_times, time, out var index, out var blend);

        if (blend <= 0f || Interpolation == TrackInterpolation.Step)
        {
            return _values[index];
        }

        if (Interpolation == TrackInterpolation.CubicSpline)
        {
            return Keyframes.Hermite(
                _values[index],
                _outTangents![index],
                _values[index + 1],
                _inTangents![index + 1],
                blend,
                _times[index + 1] - _times[index]);
        }

        return Vector3.Lerp(_values[index], _values[index + 1], blend);
    }
}
