using System.Numerics;

namespace SoftEngine.Core.Pipeline.Shadows;

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

    public bool Corners(float near, float far, Span<Vector3> corners)
    {
        if (corners.Length < 8 || !Matrix4x4.Invert(View, out var inverseView))
        {
            return false;
        }

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

            corners[index++] = Vector3.Transform(new Vector3(-halfWidth, -halfHeight, -distance), inverseView);
            corners[index++] = Vector3.Transform(new Vector3(halfWidth, -halfHeight, -distance), inverseView);
            corners[index++] = Vector3.Transform(new Vector3(-halfWidth, halfHeight, -distance), inverseView);
            corners[index++] = Vector3.Transform(new Vector3(halfWidth, halfHeight, -distance), inverseView);
        }

        return true;
    }
}
