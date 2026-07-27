using System.Numerics;

namespace SoftEngine.Core.Pipeline.Shadows;

/// <summary>
/// What the shadow pass needs to know about the camera in order to fit cascades: where it is
/// looking from, how wide it sees, and between which distances.
///
/// A single shadow map does not need any of this — it is fitted to the world, and the camera
/// only decides what part of the result is visible. Cascades are the opposite: they exist to
/// spend resolution where the <em>view</em> puts pixels, so they are fitted to slices of the
/// camera's own frustum and are meaningless without one.
/// </summary>
public readonly struct ShadowView
{
    public ShadowView(in Matrix4x4 view, in Matrix4x4 projection, float near, float far)
    {
        View = view;
        Projection = projection;
        Near = MathF.Max(near, 1e-4f);
        Far = MathF.Max(far, Near + 1e-3f);
    }

    public Matrix4x4 View { get; }

    public Matrix4x4 Projection { get; }

    public float Near { get; }

    public float Far { get; }

    /// <summary>
    /// The eight corners of the slice between two view distances, in world space.
    ///
    /// The projection's own scale factors give the frustum's half-extents per unit of depth,
    /// so the slice is read out of the matrix rather than out of a field-of-view the caller
    /// would have to keep in step with it.
    /// </summary>
    public bool Corners(float near, float far, Span<Vector3> corners)
    {
        if (corners.Length < 8 || !Matrix4x4.Invert(View, out var inverseView))
        {
            return false;
        }

        // M11 and M22 are cot(halfFov) on each axis; their reciprocals are the half-extents at
        // unit depth. A projection with either at zero is degenerate and has no slice.
        if (Projection.M11 == 0f || Projection.M22 == 0f)
        {
            return false;
        }

        var tanX = 1f / Projection.M11;
        var tanY = 1f / Projection.M22;

        var index = 0;

        foreach (var distance in stackalloc[] { near, far })
        {
            var halfWidth = distance * tanX;
            var halfHeight = distance * tanY;

            // The engine looks down -Z in view space.
            corners[index++] = Vector3.Transform(new Vector3(-halfWidth, -halfHeight, -distance), inverseView);
            corners[index++] = Vector3.Transform(new Vector3(halfWidth, -halfHeight, -distance), inverseView);
            corners[index++] = Vector3.Transform(new Vector3(-halfWidth, halfHeight, -distance), inverseView);
            corners[index++] = Vector3.Transform(new Vector3(halfWidth, halfHeight, -distance), inverseView);
        }

        return true;
    }
}
