using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public interface ILight
{
    /// <summary>Brightness multiplier for the light's contribution (1 = full).</summary>
    float Intensity { get; }

    /// <summary>Unit vector from the surface point toward the light.</summary>
    Vector3 DirectionFrom(Vector3 worldPosition);
}
