using System.Numerics;

namespace SoftEngine.Core.Shading;

public static class Refraction
{
    public static bool Refract(Vector3 incident, Vector3 normal, float eta, out Vector3 refracted)
    {
        var cosIncident = -Vector3.Dot(normal, incident);
        var k = 1f - eta * eta * (1f - cosIncident * cosIncident);

        if (k < 0f)
        {
            refracted = Vector3.Reflect(incident, normal);
            return false;
        }

        refracted = Vector3.Normalize(eta * incident + (eta * cosIncident - MathF.Sqrt(k)) * normal);
        return true;
    }

    public static float SchlickF0(float indexOfRefraction)
    {
        var ratio = (indexOfRefraction - 1f) / (indexOfRefraction + 1f);

        return ratio * ratio;
    }
}
