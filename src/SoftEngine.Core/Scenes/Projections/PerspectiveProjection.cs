using System.Numerics;

namespace SoftEngine.Core.Scenes.Projections;

public sealed class PerspectiveProjection(float fov, float zNear, float zFar) : IProjection
{
    /// <summary>
    /// The vertical field of view, in radians. Exposed because a projection that cannot be read
    /// back cannot be written to a file — and a saved scene reopened at a different field of
    /// view is a different picture.
    /// </summary>
    public float FieldOfView { get; } = fov;

    public float ZNear { get; } = zNear;

    public float ZFar { get; } = zFar;

    public Matrix4x4 ProjectionMatrix(float width, float height) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, width / height, ZNear, ZFar);
}
