using System.Numerics;

namespace SoftEngine.Core.Picking;

/// <summary>
/// A half-line in space: where it starts and which way it goes. What a click on the viewport
/// becomes once the projection has been undone.
/// </summary>
public readonly struct Ray
{
    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = direction;
    }

    public Vector3 Origin { get; }

    /// <summary>
    /// The direction travelled per unit of the parameter. Normalized for a ray in world
    /// space, so the parameter is a distance — but deliberately <em>not</em> renormalized by
    /// <see cref="Transform"/>, which is what lets a hit found in a mesh's own space report
    /// the distance in the space the query was asked in.
    /// </summary>
    public Vector3 Direction { get; }

    public Vector3 At(float distance) => Origin + Direction * distance;

    /// <summary>
    /// The same ray expressed in another space — typically a mesh's, by passing the inverse
    /// of its world matrix.
    ///
    /// The direction is transformed but not renormalized. Scaling the direction by exactly
    /// the amount the transform scales space keeps the parameter meaning what it meant
    /// before: a hit at t in model space is at t in world space, so a scaled mesh's hits can
    /// still be compared against an unscaled one's to find the nearest.
    /// </summary>
    public Ray Transform(in Matrix4x4 matrix) => new(
        Vector3.Transform(Origin, matrix),
        Vector3.TransformNormal(Direction, matrix));

    /// <summary>
    /// Where the ray enters a sphere, or false when it misses it entirely. Used to reject a
    /// whole mesh before any of its triangles are tested — the same bounding sphere the
    /// renderer culls with.
    ///
    /// A ray that starts inside the sphere counts as hitting it at distance zero: the camera
    /// standing inside a model must still be able to click on it.
    /// </summary>
    public bool IntersectsSphere(Vector3 center, float radius, out float distance)
    {
        distance = 0f;

        if (float.IsPositiveInfinity(radius))
        {
            // No bound to reject against — the mesh declines to be culled.
            return true;
        }

        var toCenter = center - Origin;
        var lengthSquared = Direction.LengthSquared();

        if (lengthSquared < 1e-20f)
        {
            return false;
        }

        // The parameter at the ray's closest approach to the centre, and how far the surface
        // is missed by there.
        var closestParameter = Vector3.Dot(toCenter, Direction) / lengthSquared;
        var missed = toCenter - Direction * closestParameter;

        var radiusSquared = radius * radius;
        var missedSquared = missed.LengthSquared();

        if (missedSquared > radiusSquared)
        {
            return false;
        }

        // Half the chord the ray cuts through the sphere, in the same parameter units.
        var half = MathF.Sqrt((radiusSquared - missedSquared) / lengthSquared);

        var entry = closestParameter - half;
        var exit = closestParameter + half;

        if (exit < 0f)
        {
            return false;
        }

        distance = MathF.Max(entry, 0f);
        return true;
    }
}
