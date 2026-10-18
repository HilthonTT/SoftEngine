using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

internal static class GizmoMath
{
    public static Vector3 ViewDirection(Scene scene)
    {
        if (!Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverse))
        {
            return -Vector3.UnitZ;
        }

        return Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, inverse));
    }

    public static bool ClosestOnAxis(in Ray ray, Vector3 origin, Vector3 direction, out float parameter) =>
        Closest(ray, origin, direction, out parameter, out _);

    public static bool Closest(in Ray ray, Vector3 origin, Vector3 direction, out float onAxis, out float onRay)
    {
        onAxis = 0f;
        onRay = 0f;

        var between = origin - ray.Origin;

        var dd = Vector3.Dot(ray.Direction, ray.Direction);
        var da = Vector3.Dot(ray.Direction, direction);
        var aa = Vector3.Dot(direction, direction);

        var determinant = dd * aa - da * da;

        if (MathF.Abs(determinant) < 1e-7f || dd < 1e-12f)
        {
            return false;
        }

        var db = Vector3.Dot(ray.Direction, between);
        var ab = Vector3.Dot(direction, between);

        onRay = (aa * db - da * ab) / determinant;
        onAxis = (da * db - dd * ab) / determinant;

        return true;
    }

    public static bool PlanePoint(in Ray ray, Vector3 origin, Vector3 normal, out Vector3 point, out float alongRay)
    {
        point = Vector3.Zero;
        alongRay = 0f;

        var denominator = Vector3.Dot(ray.Direction, normal);

        if (MathF.Abs(denominator) < 1e-4f)
        {
            return false;
        }

        alongRay = Vector3.Dot(origin - ray.Origin, normal) / denominator;
        point = ray.Origin + ray.Direction * alongRay;

        return true;
    }

    public static float Angle(Vector3 offset, Vector3 axis)
    {
        var reference = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;

        var u = Vector3.Normalize(Vector3.Cross(axis, reference));
        var v = Vector3.Cross(axis, u);

        return MathF.Atan2(Vector3.Dot(offset, v), Vector3.Dot(offset, u));
    }

    public static float Wrap(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    public static Vector3 ToLocal(IMesh mesh, Vector3 worldDelta)
    {
        if (mesh.Parent is not { } parent)
        {
            return worldDelta;
        }

        return Matrix4x4.Invert(parent.WorldMatrix, out var inverse)
            ? Vector3.TransformNormal(worldDelta, inverse)
            : worldDelta;
    }
}
