using SoftEngine.Core.Geometry;
using SoftEngine.Core.Picking;
using SoftEngine.Core.Scenes;
using System.Numerics;

namespace SoftEngine.Core.Gizmos;

/// <summary>
/// The ray arithmetic a transform is measured with: where a cursor lands on an axis, on a plane,
/// or at what bearing around one.
///
/// <para>
/// Extracted from <see cref="TransformGizmo"/> when <see cref="ModalTransform"/> arrived needing
/// exactly the same answers. The two tools are different gestures — one grabs a drawn handle, the
/// other starts from a keystroke — but they are the same geometry underneath, and a second copy of
/// it would be a second set of edge cases to get wrong.
/// </para>
/// </summary>
internal static class GizmoMath
{
    /// <summary>
    /// The direction the camera is looking, in world space. The plane through a mesh with this as
    /// its normal is where an unconstrained drag happens: it faces the viewer, so the cursor and
    /// the mesh move together whatever the view has been turned to.
    /// </summary>
    public static Vector3 ViewDirection(Scene scene)
    {
        if (!Matrix4x4.Invert(scene.Camera.ViewMatrix, out var inverse))
        {
            return -Vector3.UnitZ;
        }

        return Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, inverse));
    }

    /// <summary>The distance along an axis of the point on it nearest to the ray.</summary>
    public static bool ClosestOnAxis(in Ray ray, Vector3 origin, Vector3 direction, out float parameter) =>
        Closest(ray, origin, direction, out parameter, out _);

    /// <summary>
    /// The classic line-to-line closest approach. It degenerates when the two are parallel —
    /// which here means looking straight down the handle, where there is no drag direction to
    /// read anyway, so the caller is told to leave the mesh alone rather than given a number.
    /// </summary>
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

    /// <summary>Where the ray crosses the plane through a point with a given normal.</summary>
    public static bool PlanePoint(in Ray ray, Vector3 origin, Vector3 normal, out Vector3 point, out float alongRay)
    {
        point = Vector3.Zero;
        alongRay = 0f;

        var denominator = Vector3.Dot(ray.Direction, normal);

        // Edge-on: the ray runs along the plane and either misses it or lies in it, and the
        // ring's angle would be meaningless either way.
        if (MathF.Abs(denominator) < 1e-4f)
        {
            return false;
        }

        alongRay = Vector3.Dot(origin - ray.Origin, normal) / denominator;
        point = ray.Origin + ray.Direction * alongRay;

        return true;
    }

    /// <summary>An angle around an axis, measured in a basis built from the axis itself.</summary>
    public static float Angle(Vector3 offset, Vector3 axis)
    {
        var reference = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;

        var u = Vector3.Normalize(Vector3.Cross(axis, reference));
        var v = Vector3.Cross(axis, u);

        return MathF.Atan2(Vector3.Dot(offset, v), Vector3.Dot(offset, u));
    }

    /// <summary>The shortest way round to the same angle, so a drag across ±π does not spin back.</summary>
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

    /// <summary>Carries a world-space offset into the space the mesh's own Position lives in.</summary>
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
