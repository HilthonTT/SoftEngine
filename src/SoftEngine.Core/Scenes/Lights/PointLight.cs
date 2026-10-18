using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public sealed class PointLight : ILight
{
    private float _range = float.PositiveInfinity;
    private float _invRangeSquared;

    public Vector3 Position { get; set; }

    public float Intensity { get; set; } = 1f;

    public ColorRGB Color { get; set; } = ColorRGB.White;

    public float Range
    {
        get => _range;
        set
        {
            _range = value;
            _invRangeSquared = value > 0f && !float.IsPositiveInfinity(value) ? 1f / (value * value) : 0f;
        }
    }

    internal float InverseRangeSquared => _invRangeSquared;

    public Vector3 DirectionFrom(Vector3 worldPosition) => Vector3.Normalize(Position - worldPosition);

    public float AttenuationAt(Vector3 worldPosition) =>
        Attenuation(Vector3.DistanceSquared(Position, worldPosition), _invRangeSquared);

    internal static float Attenuation(float distanceSquared, float invRangeSquared)
    {
        if (invRangeSquared <= 0f)
        {
            return 1f;
        }

        var t = distanceSquared * invRangeSquared;
        if (t >= 1f)
        {
            return 0f;
        }

        var window = 1f - t * t;

        return window * window / (t + 1f);
    }
}
