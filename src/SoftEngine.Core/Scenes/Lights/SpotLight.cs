using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

/// <summary>
/// A point light restricted to a cone. Two angles rather than one: inside the inner cone
/// the light is at full strength, outside the outer cone it is off, and between them it
/// ramps — a single angle would give the beam an aliased edge that no amount of
/// supersampling can fix, because the discontinuity is in the lighting rather than in the
/// geometry.
/// </summary>
public sealed class SpotLight : ILight
{
    private Vector3 _direction = -Vector3.UnitY;
    private Vector3 _axis = -Vector3.UnitY;

    private float _range = float.PositiveInfinity;
    private float _invRangeSquared;

    private float _outerAngle = MathF.PI / 6f;   // 30°
    private float _innerAngle = MathF.PI / 9f;   // 20°
    private float _cosOuter = MathF.Cos(MathF.PI / 6f);
    private float _invFalloff = 1f / MathF.Max(MathF.Cos(MathF.PI / 9f) - MathF.Cos(MathF.PI / 6f), 1e-4f);

    public Vector3 Position { get; set; }

    /// <summary>The direction the beam points (it does not need to be normalized).</summary>
    public Vector3 Direction
    {
        get => _direction;
        set
        {
            _direction = value;
            _axis = value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : -Vector3.UnitY;
        }
    }

    public float Intensity { get; set; } = 1f;

    public ColorRGB Color { get; set; } = ColorRGB.White;

    /// <summary>Half-angle of the beam's full-strength core, in radians.</summary>
    public float InnerAngle
    {
        get => _innerAngle;
        set
        {
            _innerAngle = value;
            RebuildCone();
        }
    }

    /// <summary>Half-angle at which the beam has fallen to nothing, in radians.</summary>
    public float OuterAngle
    {
        get => _outerAngle;
        set
        {
            _outerAngle = value;
            RebuildCone();
        }
    }

    /// <summary>Distance falloff, as on <see cref="PointLight.Range"/>; infinite means none.</summary>
    public float Range
    {
        get => _range;
        set
        {
            _range = value;
            _invRangeSquared = value > 0f && !float.IsPositiveInfinity(value) ? 1f / (value * value) : 0f;
        }
    }

    internal Vector3 Axis => _axis;

    internal float InverseRangeSquared => _invRangeSquared;

    internal float CosOuter => _cosOuter;

    internal float InverseConeFalloff => _invFalloff;

    public Vector3 DirectionFrom(Vector3 worldPosition) => Vector3.Normalize(Position - worldPosition);

    public float AttenuationAt(Vector3 worldPosition)
    {
        var delta = Position - worldPosition;
        var distanceSquared = delta.LengthSquared();

        var attenuation = PointLight.Attenuation(distanceSquared, _invRangeSquared);
        if (attenuation <= 0f || distanceSquared < 1e-12f)
        {
            return attenuation;
        }

        var toLight = delta / MathF.Sqrt(distanceSquared);

        return attenuation * Cone(Vector3.Dot(_axis, -toLight), _cosOuter, _invFalloff);
    }

    /// <summary>
    /// The cone term, given the cosine of the angle between the beam's axis and the
    /// direction to the shaded point. Squared, so the ramp leaves the inner edge smoothly
    /// rather than with a visible crease.
    /// </summary>
    internal static float Cone(float cosAngle, float cosOuter, float invFalloff)
    {
        var t = System.Math.Clamp((cosAngle - cosOuter) * invFalloff, 0f, 1f);
        return t * t;
    }

    private void RebuildCone()
    {
        // An inner angle at or past the outer one would divide by zero; a hair of ramp is
        // kept instead, which is also the sharpest edge worth rasterizing.
        var outer = System.Math.Clamp(_outerAngle, 0f, MathF.PI * 0.5f);
        var inner = System.Math.Clamp(_innerAngle, 0f, outer);

        _cosOuter = MathF.Cos(outer);
        _invFalloff = 1f / MathF.Max(MathF.Cos(inner) - _cosOuter, 1e-4f);
    }
}
