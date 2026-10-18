using SoftEngine.Core.Scenes.Lights;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SoftEngine.Core.Shading;

public readonly struct ShaderLight
{
    private const float NotASpot = -2f;

    public readonly Vector3 Vector;

    public readonly Vector3 Axis;

    public readonly LinearColor Color;

    public readonly float InverseRangeSquared;

    public readonly float CosOuter;

    public readonly float InverseConeFalloff;

    public readonly bool IsDirectional;

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

    public static ShaderLight From(ILight light, bool castsShadow = false)
    {
        ArgumentNullException.ThrowIfNull(light, nameof(light));

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
