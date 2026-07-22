using SoftEngine.Core.Scenes.Lights;
using System.Numerics;

namespace SoftEngine.Core.Shading;

internal static class LambertLighting
{
    /// <summary>
    /// Compute the Lambert (N·L) diffuse term for a surface point, scaled by the
    /// light's intensity.
    /// </summary>
    /// <returns>Returns a value between 0 and the light's intensity.</returns>
    internal static float ComputeNDotL(Vector3 worldPosition, Vector3 normal, ILight light) =>
        MathF.Max(0, Vector3.Dot(
            Vector3.Normalize(normal),
            light.DirectionFrom(worldPosition))) * light.Intensity;
}
