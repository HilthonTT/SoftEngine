using System.Numerics;

namespace SoftEngine.Core.Pipeline.Culling;

public static class Frustum
{
    public const int PlaneCount = 6;

    public static void Build(in Matrix4x4 projection, Span<Vector4> planes)
    {
        if (planes.Length < PlaneCount)
        {
            throw new ArgumentException($"Need room for {PlaneCount} planes.", nameof(planes));
        }

        var c1 = new Vector4(projection.M11, projection.M21, projection.M31, projection.M41);
        var c2 = new Vector4(projection.M12, projection.M22, projection.M32, projection.M42);
        var c3 = new Vector4(projection.M13, projection.M23, projection.M33, projection.M43);
        var c4 = new Vector4(projection.M14, projection.M24, projection.M34, projection.M44);

        planes[0] = c4 + c1;
        planes[1] = c4 - c1;
        planes[2] = c4 + c2;
        planes[3] = c4 - c2;
        planes[4] = c3;
        planes[5] = c4 - c3;
    }

    public static bool IsSphereOutside(ReadOnlySpan<Vector4> planes, Vector3 center, float radius)
    {
        foreach (var plane in planes)
        {
            var normal = new Vector3(plane.X, plane.Y, plane.Z);

            if (Vector3.Dot(normal, center) + plane.W < -radius * normal.Length())
            {
                return true;
            }
        }

        return false;
    }
}
