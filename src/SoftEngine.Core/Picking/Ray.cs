using System.Numerics;

namespace SoftEngine.Core.Picking;

public readonly struct Ray(Vector3 origin, Vector3 direction)
{
    public Vector3 Origin { get; } = origin;

    public Vector3 Direction { get; } = direction;

    public Vector3 At(float distance) => Origin + Direction * distance;

    public Ray Transform(in Matrix4x4 matrix) => new(
        Vector3.Transform(Origin, matrix),
        Vector3.TransformNormal(Direction, matrix));

    public bool IntersectsSphere(Vector3 center, float radius, out float distance)
    {
        distance = 0f;

        if (float.IsPositiveInfinity(radius))
        {
            return true;
        }

        var toCenter = center - Origin;
        var lengthSquared = Direction.LengthSquared();

        if (lengthSquared < 1e-20f)
        {
            return false;
        }

        var closestParameter = Vector3.Dot(toCenter, Direction) / lengthSquared;
        var missed = toCenter - Direction * closestParameter;

        var radiusSquared = radius * radius;
        var missedSquared = missed.LengthSquared();

        if (missedSquared > radiusSquared)
        {
            return false;
        }

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
