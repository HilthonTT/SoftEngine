using System.Numerics;

namespace SoftEngine.Core.Tests;

/// <summary>
/// Assertions for values that come out of a chain of floating-point transforms, where exact
/// equality is the wrong question. xUnit's <c>Assert.Equal(expected, actual, precision)</c>
/// covers single floats; these cover the vectors and quaternions the scene graph deals in.
/// </summary>
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

    /// <summary>
    /// Compares rotations by what they do rather than by their components, because q and -q
    /// are the same rotation and a slerp is free to return either.
    /// </summary>
    public static void EqualRotation(Quaternion expected, Quaternion actual, float tolerance = Tolerance)
    {
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            Equal(Vector3.Transform(axis, expected), Vector3.Transform(axis, actual), tolerance);
        }
    }
}
