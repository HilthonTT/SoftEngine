using System.Numerics;

namespace SoftEngine.Core.Tests;

internal static class Approx
{
    public const float Tolerance = 1e-4f;

    public static void Equal(Vector3 expected, Vector3 actual, float tolerance = Tolerance)
    {
        if ((expected - actual).Length() > tolerance)
        {
            Assert.Fail($"Expected {expected} but got {actual} (tolerance {tolerance}).");
        }
    }

    public static void EqualRotation(Quaternion expected, Quaternion actual, float tolerance = Tolerance)
    {
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            Equal(Vector3.Transform(axis, expected), Vector3.Transform(axis, actual), tolerance);
        }
    }
}
