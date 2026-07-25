using SoftEngine.Core.Diagnostics;
using System.Numerics;

namespace SoftEngine.Core.Scenes.Lights;

public interface ILight
{
    /// <summary>Brightness multiplier for the light's contribution (1 = full).</summary>
    float Intensity { get; }

    /// <summary>
    /// The colour of the light itself, before <see cref="Intensity"/>. White by default,
    /// which is the only colour a light could have when every surface got a single scalar
    /// intensity.
    /// </summary>
    ColorRGB Color => ColorRGB.White;

    /// <summary>Unit vector from the surface point toward the light.</summary>
    Vector3 DirectionFrom(Vector3 worldPosition);

    /// <summary>
    /// How much of the light actually arrives at a point, in [0, 1] — distance falloff for
    /// a positional light, the cone for a spot. 1 everywhere by default: a light with no
    /// falloff illuminates the whole world evenly, which is what a
    /// <see cref="DirectionalLight"/> does and what <see cref="PointLight"/> did before it
    /// could be given a range.
    /// </summary>
    float AttenuationAt(Vector3 worldPosition) => 1f;
}
