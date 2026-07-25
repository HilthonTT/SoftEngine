using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

/// <summary>A light radiating from a position; the light direction varies per surface point.</summary>
public sealed class PointLight : ILight
{
    private float _range = float.PositiveInfinity;
    private float _invRangeSquared;

    public Vector3 Position { get; set; }

    public float Intensity { get; set; } = 1f;

    public ColorRGB Color { get; set; } = ColorRGB.White;

    /// <summary>
    /// Distance at which the light has fallen to nothing. Infinite — the default — means no
    /// distance falloff at all: the light reaches everything at full strength, which is how
    /// this light behaved before it could be given a range, and what keeps a scene lit
    /// without having to tune a number against the model's scale first.
    ///
    /// Give it a finite range and the falloff becomes inverse-square, windowed so it
    /// actually reaches zero at the range instead of trailing off forever. Bear in mind
    /// what scale the world is in: the range that lights a 2-unit skull leaves a
    /// 1500-unit elephant in the dark.
    /// </summary>
    public float Range
    {
        get => _range;
        set
        {
            _range = value;
            _invRangeSquared = value > 0f && !float.IsPositiveInfinity(value) ? 1f / (value * value) : 0f;
        }
    }

    /// <summary>1 / Range², or 0 when the light has no falloff. What the shader actually wants.</summary>
    internal float InverseRangeSquared => _invRangeSquared;

    public Vector3 DirectionFrom(Vector3 worldPosition) => Vector3.Normalize(Position - worldPosition);

    public float AttenuationAt(Vector3 worldPosition) =>
        Attenuation(Vector3.DistanceSquared(Position, worldPosition), _invRangeSquared);

    /// <summary>
    /// Windowed inverse-square falloff, normalized to 1 at the light itself and exactly 0
    /// at the range. The window is what makes the range a real boundary: unmodified
    /// inverse-square never reaches zero, so a light either has to be clipped — which shows
    /// as a hard ring — or kept in every shading loop forever.
    /// </summary>
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
