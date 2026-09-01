using System.Numerics;

namespace SoftEngine.Core.Animation;

public sealed class QuaternionTrack
{
    private readonly float[] _times;
    private readonly Quaternion[] _values;

    private readonly Quaternion[]? _inTangents;
    private readonly Quaternion[]? _outTangents;

    public QuaternionTrack(float[] times, Quaternion[] values)
        : this(times, values, TrackInterpolation.Linear, null, null)
    {
    }

    public QuaternionTrack(
        float[] times,
        Quaternion[] values,
        TrackInterpolation interpolation,
        Quaternion[]? inTangents = null,
        Quaternion[]? outTangents = null)
    {
        ArgumentNullException.ThrowIfNull(times, nameof(times));
        ArgumentNullException.ThrowIfNull(values, nameof(values));

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

    public Quaternion Sample(float time)
    {
        if (_times.Length == 0)
        {
            return Quaternion.Identity;
        }

        Keyframes.Locate(_times, time, out var index, out var blend);

        if (blend <= 0f || Interpolation == TrackInterpolation.Step)
        {
            return _values[index];
        }

        if (Interpolation == TrackInterpolation.CubicSpline)
        {
            var value = Keyframes.Hermite(
                _values[index],
                _outTangents![index],
                _values[index + 1],
                _inTangents![index + 1],
                blend,
                _times[index + 1] - _times[index]);

            return value.LengthSquared() > 1e-12f ? Quaternion.Normalize(value) : _values[index];
        }

        return Quaternion.Normalize(Quaternion.Slerp(_values[index], _values[index + 1], blend));
    }
}
