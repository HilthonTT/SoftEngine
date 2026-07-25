using SoftEngine.Core.Scenes.Lights;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

/// <summary>
/// One light flattened into the handful of numbers a shader actually needs, resolved once
/// per frame instead of per pixel.
///
/// The point is to get the <see cref="ILight"/> interface out of the inner loop. A shader
/// that called through it would pay a virtual dispatch — and forfeit inlining — at every
/// pixel of every triangle the light touches; here the type is a two-way branch on a
/// field, and everything that varies per light is already a float in a struct.
/// </summary>
public readonly struct ShaderLight
{
    private const float NotASpot = -2f;

    /// <summary>Unit vector toward the light for a directional light, world position otherwise.</summary>
    public readonly Vector3 Vector;

    /// <summary>Unit direction the beam points; only read for a spot.</summary>
    public readonly Vector3 Axis;

    /// <summary>The light's colour times its intensity, in linear light.</summary>
    public readonly LinearColor Color;

    /// <summary>1 / Range², or 0 for a light with no distance falloff.</summary>
    public readonly float InverseRangeSquared;

    /// <summary>Cosine of the outer cone half-angle, or <see cref="NotASpot"/>.</summary>
    public readonly float CosOuter;

    public readonly float InverseConeFalloff;

    public readonly bool IsDirectional;

    /// <summary>
    /// Whether this is the light the frame's shadow map was rendered from. Only one is:
    /// the map is a single depth buffer taken from a single point of view, so a second
    /// shadowed light would need a second pass and a second buffer.
    /// </summary>
    public readonly bool CastsShadow;

    private ShaderLight(
        Vector3 vector,
        Vector3 axis,
        LinearColor color,
        float inverseRangeSquared,
        float cosOuter,
        float inverseConeFalloff,
        bool isDirectional,
        bool castsShadow)
    {
        Vector = vector;
        Axis = axis;
        Color = color;
        InverseRangeSquared = inverseRangeSquared;
        CosOuter = cosOuter;
        InverseConeFalloff = inverseConeFalloff;
        IsDirectional = isDirectional;
        CastsShadow = castsShadow;
    }

    /// <summary>
    /// Flattens a scene light. Anything that is not one of the three built-in types is
    /// treated as directional, sampled once at the world origin — the same fallback the
    /// painters used when they could only carry one light.
    /// </summary>
    public static ShaderLight From(ILight light, bool castsShadow = false)
    {
        ArgumentNullException.ThrowIfNull(light);

        LinearColor color = light.Color;
        var scaled = light.Intensity * color;

        return light switch
        {
            DirectionalLight directional => new ShaderLight(
                directional.DirectionFrom(Vector3.Zero), Vector3.Zero, scaled,
                0f, NotASpot, 0f, true, castsShadow),

            PointLight point => new ShaderLight(
                point.Position, Vector3.Zero, scaled,
                point.InverseRangeSquared, NotASpot, 0f, false, castsShadow),

            SpotLight spot => new ShaderLight(
                spot.Position, spot.Axis, scaled,
                spot.InverseRangeSquared, spot.CosOuter, spot.InverseConeFalloff, false, castsShadow),

            _ => new ShaderLight(
                light.DirectionFrom(Vector3.Zero), Vector3.Zero, scaled,
                0f, NotASpot, 0f, true, castsShadow),
        };
    }

    /// <summary>
    /// Resolves the light at a world point: the unit vector toward it and how much of it
    /// arrives. Returns false when none of it does — behind a spot, past a range — so the
    /// caller can skip the dot products and the shadow lookup entirely.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Sample(Vector3 worldPosition, out Vector3 toLight, out float attenuation)
    {
        if (IsDirectional)
        {
            toLight = Vector;
            attenuation = 1f;
            return true;
        }

        var delta = Vector - worldPosition;
        var distanceSquared = delta.LengthSquared();

        if (distanceSquared < 1e-12f)
        {
            // Standing exactly on the light. Any direction is as wrong as any other; pick
            // one rather than dividing by zero.
            toLight = Vector3.UnitY;
            attenuation = 1f;
            return true;
        }

        toLight = delta * (1f / MathF.Sqrt(distanceSquared));

        attenuation = PointLight.Attenuation(distanceSquared, InverseRangeSquared);
        if (attenuation <= 0f)
        {
            return false;
        }

        if (CosOuter > NotASpot)
        {
            attenuation *= SpotLight.Cone(Vector3.Dot(Axis, -toLight), CosOuter, InverseConeFalloff);
        }

        return attenuation > 0f;
    }
}
