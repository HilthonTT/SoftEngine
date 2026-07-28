using System.Numerics;

namespace SoftEngine.Core.Pipeline.Culling;

/// <summary>
/// The six planes of a view frustum, and the sphere test both culling passes ask them.
///
/// <para>
/// Extracted from the renderer when the occlusion pass arrived and needed the same question
/// answered: the two passes have to agree about what is on screen, and a second copy of plane
/// extraction is a second chance to get a sign wrong.
/// </para>
/// </summary>
public static class Frustum
{
    /// <summary>Planes a <see cref="Build"/> call fills.</summary>
    public const int PlaneCount = 6;

    /// <summary>
    /// Extracts the six view-space planes from a projection matrix (row-vector convention,
    /// clip z in [0, w]). Planes point inward, so <c>dot(normal, point) + distance ≥ 0</c>
    /// means inside. The normals are not normalized; the sphere test scales for that.
    /// </summary>
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

        planes[0] = c4 + c1; // left
        planes[1] = c4 - c1; // right
        planes[2] = c4 + c2; // bottom
        planes[3] = c4 - c2; // top
        planes[4] = c3;      // near (z >= 0)
        planes[5] = c4 - c3; // far
    }

    /// <summary>Whether a view-space sphere lies entirely outside any one of the planes.</summary>
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
