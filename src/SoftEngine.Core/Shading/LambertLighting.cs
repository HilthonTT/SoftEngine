using System.Numerics;

namespace SoftEngine.Core.Shading;

internal static class LambertLighting
{
    /// <summary>
    /// Compute the cosine of the angle between the light vector and the normal vector
    /// </summary>
    /// <returns>Returns a value between 0 and 1</returns>
    internal static float ComputeNDotL(Vector3 vertexCenter, Vector3 normal, Vector3 lightPosition) =>
        MathF.Max(0, Vector3.Dot(
            Vector3.Normalize(normal),
            Vector3.Normalize(lightPosition - vertexCenter)));
}
