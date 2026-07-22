using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

/// <summary>A light radiating from a position; the light direction varies per surface point.</summary>
public sealed class PointLight : ILight
{
    public Vector3 Position { get; set; }

    public float Intensity { get; set; } = 1f;

    public Vector3 DirectionFrom(Vector3 worldPosition) => Vector3.Normalize(Position - worldPosition);
}
